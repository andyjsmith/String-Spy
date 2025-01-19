using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
    /// <param name="ct">Cancellation token</param>
    public List<StringResult> FindStrings(Action<double>? progressCallback = null, CancellationToken? ct = null)
    {
        ct ??= CancellationToken.None;
        FileInfo fileInfo = new(_path);
        long fileSize = fileInfo.Length;
        long chunkSize = fileSize / _threadCount;
        var bytesProcessed = new long[_threadCount];
        var bag = new List<StringResult>[_threadCount];
        if (_threadCount == 1)
        {
            long start = 0;
            long end = fileSize;
            Debug.WriteLine($"Starting chunk processor: start: {start}, end: {end}");
            var stringResults = ProcessFileChunk(start, end, b =>
            {
                bytesProcessed[0] = b;
                progressCallback?.Invoke((double)bytesProcessed.Sum() / fileSize);
            }, (CancellationToken)ct);
            bag[0] = stringResults;
        }
        else
        {
            Parallel.For(0, _threadCount, threadIndex =>
            {
                long start = threadIndex * chunkSize;
                long end = (threadIndex == _threadCount - 1) ? fileSize : start + chunkSize;
                Debug.WriteLine(
                    $"Starting parallel chunk processor: {threadIndex + 1:D2}/{_threadCount}, start: {start}, end: {end}");
                var stringResults = ProcessFileChunk(start, end, b =>
                {
                    bytesProcessed[threadIndex] = b;
                    progressCallback?.Invoke((double)bytesProcessed.Sum() / fileSize);
                }, (CancellationToken)ct);
                bag[threadIndex] = stringResults;
            });
        }

        List<StringResult> results = [];
        foreach (var result in bag)
        {
            results.AddRange(result);
        }

        // Remove overlapping strings
        for (int i = results.Count - 1; i > 0; i--)
        {
            if (results[i].Position <= results[i - 1].EndPosition)
            {
                results.RemoveAt(i);
            }
        }

        return results;
    }


    /// <summary>
    /// Process a specified chunk of a file to find strings
    /// </summary>
    /// <param name="start">Address in the file to start searching</param>
    /// <param name="end">Address in the file to end searching</param>
    /// <param name="progressCallback">Progress callback with number of bytes that have been processed in this chunk</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Found strings</returns>
    private List<StringResult> ProcessFileChunk(long start, long end, Action<long>? progressCallback,
        CancellationToken ct)
    {
        // Skip to this chunk's start point
        using FileStream fs = new(_path, FileMode.Open, FileAccess.Read);
        fs.Seek(start, SeekOrigin.Begin);

        // Get max byte count of a char in this encoding
        int maxBytesPerChar = _encoding.GetMaxByteCount(1);
        int maxCharCount = _encoding.GetMaxCharCount(maxBytesPerChar);
        Debug.Assert(maxBytesPerChar > 0);

        // Create a char buffer of that length
        var charDecodeBuff = new char[maxCharCount];

        // Buffer to decode the bytes to, extending extra for leftover bytes in multibyte chars
        var buff = new byte[BlockSize + maxBytesPerChar - 1];

        StringBuilder currentString = new();
        long currentStringStart = 0;

        List<StringResult> foundStrings = [];

        int numBytesRead;
        var totalBytesRead = 0;

        bool breakOuter = false;
        while (!breakOuter && (numBytesRead = fs.Read(buff, 0, BlockSize)) > 0)
        {
            if (ct.IsCancellationRequested) return [];
            progressCallback?.Invoke(totalBytesRead);
            totalBytesRead += numBytesRead;

            long bufferStartOffset = fs.Position - numBytesRead;

            if (numBytesRead < buff.Length)
            {
                // We're reusing the buffer, need to clear any previous data out
                Array.Clear(buff, numBytesRead, buff.Length - numBytesRead);
            }

            var i = 0;
            int bufferLength = Math.Min(numBytesRead, BlockSize);
            while (i < bufferLength)
            {
                // TODO: What if buffer isn't div by 4? Might have 1-3 bytes left over that need to be rolled into beginning of next
                // TODO: Potential solution is to limit BlockSize to a multiple of 4...
                Array.Clear(charDecodeBuff, 0, charDecodeBuff.Length);
                int numCharsDecoded = _encoding.GetChars(buff, i, maxBytesPerChar, charDecodeBuff, 0);

                if (numCharsDecoded == 0 || !Character.IsPrintable(charDecodeBuff[0], _encoding, _charSet))
                {
                    // Not printable here, reset and advance
                    if (currentString.Length >= _minimumLength)
                    {
                        foundStrings.Add(new StringResult(currentString.ToString(), currentStringStart));
                    }

                    currentString.Clear();

                    if ((bufferStartOffset + i) >= end)
                    {
                        breakOuter = true;
                        break;
                    }

                    // Advance by one byte, since next string could start less than a full char away
                    i++;
                }
                else
                {
                    // Char printable
                    if (currentString.Length == 0)
                    {
                        // We're at the start of a new string
                        currentStringStart = bufferStartOffset + i;
                    }

                    currentString.Append(charDecodeBuff[0]);

                    // Advance by the byte length of that char
                    i += _encoding.GetByteCount(charDecodeBuff, 0, 1);
                }
            }
        }

        if (currentString.Length >= _minimumLength)
        {
            foundStrings.Add(new StringResult(currentString.ToString(), currentStringStart));
        }

        return foundStrings;
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
        bool useRegex,
        bool caseSensitive, Action<int>? progressCallback,
        CancellationToken ct = default)
    {
        List<StringResult> filtered = [];

        if (searchText == string.Empty)
        {
            return stringResults;
        }

        try
        {
            bool useMultithreadedSearch = SettingsManager.Instance.Settings.ParallelSearchThreshold switch
            {
                < 0 => false,
                0 => true,
                _ => stringResults.Count >= SettingsManager.Instance.Settings.ParallelSearchThreshold
            };

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

            if (useMultithreadedSearch)
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