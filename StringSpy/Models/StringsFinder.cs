using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using StringSpy.Settings;

namespace StringSpy.Models;

public class StringsFinder
{
    private const int BlockSize = 4 << 20; // 4 MiB

    private readonly int _minimumLength;
    private readonly Encoding _encoding;
    private readonly CharSet _charSet;
    private readonly int _threadCount;
    private readonly string _path;

    /// <summary>
    /// Create a new file processor for finding strings
    /// </summary>
    /// <param name="path">Path to input file</param>
    /// <param name="minimumLength"></param>
    /// <param name="encoding">Encoding to use when decoding the file bytes to text</param>
    /// <param name="charSet">Character set to use when determining if a character is printable</param>
    /// <param name="threadCount">Number of threads to use for parallel searching</param>
    public StringsFinder(string path, Encoding encoding, CharSet charSet, int minimumLength = 4,
        int threadCount = 1)
    {
        _minimumLength = minimumLength;
        _encoding = (Encoding)encoding.Clone();
        _charSet = charSet;
        _threadCount = threadCount;
        _path = path;

        // Set the encoding to use null byte as replacement character when a char isn't valid for the encoding,
        // this will just act as a non-printable char and end the string.
        _encoding.DecoderFallback = new DecoderReplacementFallback("\0");
    }

    /// <summary>
    /// Process an input file to find strings
    /// </summary>
    /// <param name="progressCallback">Callback for setting progress, 0 to 1</param>
    /// <param name="useMemoryMappedFile">Try to use a memory mapped file instead of file stream</param>
    /// <param name="ct">Cancellation token</param>
    public List<StringResult> FindStrings(Action<double>? progressCallback = null, bool useMemoryMappedFile = true,
        CancellationToken ct = default)
    {
        long fileSize = new FileInfo(_path).Length;
        long chunkSize = fileSize / _threadCount;
        var bytesProcessed = new long[_threadCount];
        var resultsPerThread = new List<StringResult>[_threadCount];

        MemoryMappedFile? mmf = null;
        try
        {
            if (useMemoryMappedFile)
            {
                mmf = MemoryMappedFile.CreateFromFile(_path, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
            }
        }
        catch (Exception e) when (e is ArgumentOutOfRangeException or IOException or PathTooLongException
                                      or SecurityException)
        {
            Debug.WriteLine("Could not memory map file: " + e.Message);
        }

        try
        {
            Parallel.For(0, _threadCount, threadIndex =>
            {
                long start = threadIndex * chunkSize;
                long end = threadIndex == _threadCount - 1 ? fileSize : start + chunkSize;
                Debug.WriteLine(
                    $"Starting parallel chunk processor: {threadIndex + 1:D2}/{_threadCount}, start: {start}, end: {end}");

                var stringResults = ProcessFileChunk(start, end, b =>
                {
                    bytesProcessed[threadIndex] = b;
                    progressCallback?.Invoke((double)bytesProcessed.Sum() / fileSize);
                    // ReSharper disable once AccessToDisposedClosure
                }, mmf, ct);
                resultsPerThread[threadIndex] = stringResults;
            });
        }
        finally
        {
            mmf?.Dispose();
        }

        var results = resultsPerThread.SelectMany(x => x).ToList();

        // Remove overlapping strings
        for (int i = results.Count - 1; i > 0; i--)
        {
            if (results[i].Position <= results[i - 1].EndPosition) results.RemoveAt(i);
        }

        return results;
    }


    /// <summary>
    /// Process a specified chunk of a file to find strings
    /// </summary>
    /// <param name="start">Address in the file to start searching</param>
    /// <param name="end">Address in the file to end searching</param>
    /// <param name="progressCallback">Progress callback with number of bytes that have been processed in this chunk</param>
    /// <param name="mmf">Memory mapped file to use. If null, falls back to FileStream.</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Found strings</returns>
    private List<StringResult> ProcessFileChunk(long start, long end, Action<long>? progressCallback,
        MemoryMappedFile? mmf,
        CancellationToken ct)
    {
        Stream? fs = null;
        if (mmf != null)
        {
            try
            {
                fs = mmf.CreateViewStream(0, 0, MemoryMappedFileAccess.Read);
            }
            catch (Exception e) when (e is UnauthorizedAccessException or IOException)
            {
                Debug.WriteLine(e.Message);
            }
        }

        // Fallback to FileStream if memory mapped file is null or fails
        fs ??= new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read, BlockSize,
            FileOptions.SequentialScan);

        try
        {
            // Skip to this chunk's start point
            fs.Seek(start, SeekOrigin.Begin);

            // Get max byte count of a char in this encoding
            int maxBytesPerChar = _encoding.GetMaxByteCount(1);
            int maxCharCount = _encoding.GetMaxCharCount(maxBytesPerChar);
            Debug.Assert(maxBytesPerChar > 0);

            // Create a char buffer of that length
            var charDecodeBuff = new char[maxCharCount];

            StringBuilder currentString = new();
            long currentStringStart = 0;

            List<StringResult> foundStrings = [];

            var readBuff = new byte[maxBytesPerChar];

            long i = start;
            while (i < end || currentString.Length > 0)
            {
                if (i % 1_000_000 == 0)
                {
                    if (ct.IsCancellationRequested) return [];
                    progressCallback?.Invoke(i - start);
                }

                // TODO: What if buffer isn't div by 4? Might have 1-3 bytes left over that need to be rolled into beginning of next
                // TODO: Potential solution is to limit BlockSize to a multiple of 4...

                // Read a minimum of one character
                fs.Seek(i, SeekOrigin.Begin);
                int numBytesRead = fs.Read(readBuff, 0, maxBytesPerChar);

                // Decode the character(s)
                int numCharsDecoded = _encoding.GetChars(readBuff, 0, numBytesRead, charDecodeBuff, 0);

                if (numCharsDecoded == 0 || !Character.IsPrintable(charDecodeBuff[0], _encoding, _charSet))
                {
                    // Not printable here, reset and advance
                    if (currentString.Length >= _minimumLength)
                    {
                        foundStrings.Add(new StringResult(currentString.ToString(), currentStringStart));
                    }

                    currentString.Clear();

                    // Advance by one byte, since next string could start less than a full char away
                    i++;
                }
                else
                {
                    // Char is printable
                    if (currentString.Length == 0)
                    {
                        // We're at the start of a new string
                        currentStringStart = i;
                    }

                    currentString.Append(charDecodeBuff[0]);

                    // Advance by the byte length of that char
                    i += _encoding.GetByteCount(charDecodeBuff, 0, 1);
                }
            }

            if (currentString.Length >= _minimumLength)
            {
                foundStrings.Add(new StringResult(currentString.ToString(), currentStringStart));
            }

            return foundStrings;
        }
        finally
        {
            fs.Dispose();
        }
    }


    /// <summary>
    /// How many strings to search between progress update events
    /// </summary>
    private const int SearchProgressUpdateInterval = 10_000;

    /// <summary>
    /// Search all strings for the provided text using options set in the GUI and settings
    /// </summary>
    /// <param name="stringResults">Results to filter</param>
    /// <param name="searchText">Text to search for</param>
    /// <param name="useRegex">Enable regex search</param>
    /// <param name="caseSensitive">Enable case-sensitive search</param>
    /// <param name="progressCallback">Callback with number of strings that have been searched since the last callback</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Search task</returns>
    /// <exception cref="OperationCanceledException">Throws if search was cancelled</exception>
    public static List<StringResult>? FilterStrings(List<StringResult> stringResults, string searchText,
        bool useRegex, bool caseSensitive, Action<int>? progressCallback, CancellationToken ct = default)
    {
        List<StringResult> filtered = [];

        if (searchText == string.Empty) return stringResults;

        try
        {
            Func<StringResult, bool> filterFunc;
            if (useRegex)
            {
                var regexOptions = RegexOptions.Compiled;
                if (!caseSensitive) regexOptions |= RegexOptions.IgnoreCase;

                Regex re;
                try
                {
                    re = new Regex(searchText, regexOptions);
                }
                catch (ArgumentException)
                {
                    return null;
                }

                filterFunc = s => re.IsMatch(s.Content);
            }
            else
            {
                var comparisonType = StringComparison.OrdinalIgnoreCase;
                if (caseSensitive) comparisonType = StringComparison.Ordinal;
                filterFunc = s => s.Content.Contains(searchText, comparisonType);
            }

            if (SettingsManager.Instance.Settings.MultithreadedFiltering && stringResults.Count > 10_000)
            {
                int threadCount = Environment.ProcessorCount;
                int chunkSize = stringResults.Count / threadCount;

                var results = new List<StringResult>[threadCount];
                Parallel.For(0, threadCount, i =>
                {
                    int start = i * chunkSize;
                    int end = (i == threadCount - 1) ? stringResults.Count : start + chunkSize;
                    Debug.WriteLine(
                        $"Starting string processor: {i + 1:D2}/{threadCount}, start: {start}, end: {end}");
                    results[i] = [];
                    for (int j = start; j < end; j++)
                    {
                        if (ct.IsCancellationRequested) return;

                        if (filterFunc(stringResults[j])) results[i].Add(stringResults[j]);

                        if ((j - start) % SearchProgressUpdateInterval != 0) continue;
                        progressCallback?.Invoke(SearchProgressUpdateInterval);
                    }
                });

                foreach (var result in results)
                {
                    filtered.AddRange(result);
                }
            }
            else
            {
                for (var i = 0; i < stringResults.Count; i++)
                {
                    if (i % SearchProgressUpdateInterval == 0)
                        progressCallback?.Invoke(SearchProgressUpdateInterval);
                    if (filterFunc(stringResults[i])) filtered.Add(stringResults[i]);
                    ct.ThrowIfCancellationRequested();
                }
            }
        }
        catch (OperationCanceledException)
        {
            Debug.WriteLine("Search cancelled with exception");
            return null;
        }

        return filtered;
    }
}