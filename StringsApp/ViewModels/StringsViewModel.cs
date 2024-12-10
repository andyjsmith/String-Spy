using System;
using Avalonia.Controls;
using Avalonia.Controls.Models.TreeDataGrid;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace StringsApp.ViewModels;

public partial class StringsViewModel : ViewModelBase
{
    private static int MinLen => 4;

    private const int BlockSize = 4 << 20; // 4 MiB

    [ObservableProperty] private List<StringResult> _allStringResults = [];

    [ObservableProperty] private ObservableCollection<StringResult> _filteredStrings = [];

    [ObservableProperty] private FlatTreeDataGridSource<StringResult> _stringsSource;

    [ObservableProperty] private bool _isSearchTextValid = true;

    private Task? SearchTask { get; set; }
    private CancellationTokenSource _searchTaskCts = new();

    [ObservableProperty] private int _searchProgress;

    private string _searchText = "";

    public string SearchText
    {
        get => _searchText;
        set
        {
            _searchText = value;
            OnPropertyChanged();
            RunSearch();
        }
    }

    private bool _isRegexEnabled = false;

    public bool IsRegexEnabled
    {
        get => _isRegexEnabled;
        set
        {
            _isRegexEnabled = value;
            RunSearch();
        }
    }

    private bool _isCaseSensitiveEnabled = false;

    public bool IsCaseSensitiveEnabled
    {
        get => _isCaseSensitiveEnabled;
        set
        {
            _isCaseSensitiveEnabled = value;
            RunSearch();
        }
    }

    private bool ValidateSearchText(string searchText)
    {
        if (searchText.Length == 0) return true;
        if (!IsRegexEnabled) return true;

        if (string.IsNullOrWhiteSpace(searchText)) return false;

        try
        {
            _ = Regex.Match("", searchText);
        }
        catch (ArgumentException)
        {
            return false;
        }

        return true;
    }

    private void CancelSearch()
    {
        if (!SearchTask?.IsCompleted ?? false)
        {
            _searchTaskCts.Cancel();
            Debug.WriteLine("Cancelling search task");
        }
    }

    [RelayCommand]
    public void CloseFile()
    {
        CancelSearch();
        AllStringResults.Clear();
        AllStringResults = [];
        FilteredStrings.Clear();
        FilteredStrings = [];
    }

    [RelayCommand]
    public void RunSearch()
    {
        IsSearchTextValid = ValidateSearchText(_searchText);
        if (!IsSearchTextValid) return;
        CancelSearch();

        _searchTaskCts = new CancellationTokenSource();
        SearchTask = Search(SearchText, _searchTaskCts.Token);
    }

    private Task Search(string searchText, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            List<StringResult> filtered = [];

            if (searchText == string.Empty)
            {
                filtered = AllStringResults;
            }
            else
            {
                try
                {
                    bool useMultithreadedSearch = true;

                    Func<StringResult, bool> filterFunc;
                    if (IsRegexEnabled)
                    {
                        var regexOptions = RegexOptions.Compiled;
                        if (!IsCaseSensitiveEnabled) regexOptions |= RegexOptions.IgnoreCase;

                        Regex re;
                        try
                        {
                            re = new(searchText, regexOptions);
                        }
                        catch (ArgumentException)
                        {
                            return;
                        }

                        filterFunc = s => re.IsMatch(s.Content);
                    }
                    else
                    {
                        StringComparison comparisonType = StringComparison.OrdinalIgnoreCase;
                        if (IsCaseSensitiveEnabled) comparisonType = StringComparison.Ordinal;
                        filterFunc = s => s.Content.Contains(searchText, comparisonType);
                    }

                    if (useMultithreadedSearch)
                    {
                        int threadCount = Environment.ProcessorCount;
                        int chunkSize = AllStringResults.Count / threadCount;

                        List<StringResult>[] results = new List<StringResult>[threadCount];
                        Parallel.For(0, threadCount, i =>
                        {
                            int start = i * chunkSize;
                            int end = (i == threadCount - 1) ? AllStringResults.Count : start + chunkSize;
                            Debug.WriteLine(
                                $"Starting string processor: {i + 1:D2}/{threadCount}, start: {start}, end: {end}");
                            results[i] = new List<StringResult>();
                            for (int j = start; j < end; j++)
                            {
                                if (ct.IsCancellationRequested)
                                {
                                    return;
                                }

                                if (filterFunc(AllStringResults[j]))
                                {
                                    results[i].Add(AllStringResults[j]);
                                }

                                if ((j - start) % 100000 == 0)
                                {
#pragma warning disable MVVMTK0034
                                    Interlocked.Add(ref _searchProgress, 100000);
#pragma warning restore MVVMTK0034
                                    OnPropertyChanged(nameof(SearchProgress));
                                }
                            }
                        });

                        foreach (List<StringResult> result in results)
                        {
                            filtered.AddRange(result);
                        }
                    }
                    else
                    {
                        int i = -1;
                        foreach (var s in AllStringResults)
                        {
                            i++;
                            if (i % 100000 == 0)
                            {
                                SearchProgress = i;
                            }

                            ct.ThrowIfCancellationRequested();

                            if (s.Content.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                            {
                                filtered.Add(s);
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    Debug.WriteLine("Search cancelled with exception");
                    return;
                }
            }

            FilteredStrings = new ObservableCollection<StringResult>(filtered);
            Dispatcher.UIThread.Invoke(() => { StringsSource.Items = FilteredStrings; });
            SearchProgress = 0;
        }, ct);
    }

    private List<Encoding> AllEncodings { get; } = [];

    [ObservableProperty] private List<Encoding> _filteredEncodings;

    [ObservableProperty] private Encoding _selectedEncoding;
    private string _encodingFilter = "";

    private static readonly List<Encoding> PrioritizedEncodings =
    [
        Encoding.ASCII,
        Encoding.Latin1,
        Encoding.UTF8,
        Encoding.Unicode, // UTF16-LE
        Encoding.BigEndianUnicode, // UTF16-BE
        Encoding.UTF32, // UTF32-LE
        Encoding.GetEncoding(12001) // UTF32-BE
    ];

    public string EncodingFilter
    {
        get => _encodingFilter;
        set
        {
            _encodingFilter = value;
            OnPropertyChanged();
            if (string.IsNullOrWhiteSpace(_encodingFilter))
            {
                FilteredEncodings = AllEncodings;
                return;
            }

            List<Encoding> encodings = AllEncodings.Where(e =>
                e.EncodingName.Contains(value, StringComparison.OrdinalIgnoreCase) ||
                e.WebName.Contains(value, StringComparison.OrdinalIgnoreCase)).ToList();
            FilteredEncodings = encodings;
        }
    }

    public record OffsetFormatter(string Name, Func<StringResult, string> Format);

    private static readonly OffsetFormatter DecimalOffsetFormatter =
        new("Decimal", result => result.Position.ToString());

    private static readonly OffsetFormatter HexadecimalOffsetFormatter =
        new("Hexadecimal", result => "0x" + result.Position.ToString("X"));

    private static readonly OffsetFormatter OctalOffsetFormatter =
        new("Octal", result => "0" + Convert.ToString(result.Position, 8));

    public List<OffsetFormatter> OffsetFormatters { get; } =
    [
        DecimalOffsetFormatter,
        HexadecimalOffsetFormatter,
        OctalOffsetFormatter
    ];

    [ObservableProperty] private OffsetFormatter _selectedOffsetFormatter = DecimalOffsetFormatter;

    partial void OnSelectedOffsetFormatterChanged(OffsetFormatter value)
    {
        // Recreate the entire StringsSource to force the tree to rerender
        StringsSource = CreateStringsSource(value);
    }

    private FlatTreeDataGridSource<StringResult> CreateStringsSource(OffsetFormatter offsetFormatter)
    {
        return new FlatTreeDataGridSource<StringResult>(FilteredStrings)
        {
            Columns =
            {
                new TextColumn<StringResult, string>("Position", x => offsetFormatter.Format(x)),
                new TextColumn<StringResult, string>("String", x => x.Content)
            }
        };
    }

    public StringsViewModel()
    {
        // Register all code pages from the System.Text.Encoding.CodePages package
        // .NET core doesn't include them by default
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        // Add prioritized encodings to the top of encodings list for easier visibility,
        // followed by the rest of the non-prioritized encodings
        AllEncodings = PrioritizedEncodings.Concat(
            Encoding.GetEncodings()
                .Select(e => e.GetEncoding())
                .Except(PrioritizedEncodings)
                .OrderBy(e => e.EncodingName)
        ).ToList();

        FilteredEncodings = AllEncodings;
        SelectedEncoding = Encoding.ASCII;

        StringsSource = CreateStringsSource(DecimalOffsetFormatter);
    }

    public void RunParallel(string path, int threadCount, CancellationToken ct, Action<double>? progressCallback)
    {
        FileInfo fileInfo = new(path);
        long fileSize = fileInfo.Length;
        long chunkSize = fileSize / threadCount;
        long[] bytesProcessed = new long[threadCount];
        List<StringResult>[] bag = new List<StringResult>[threadCount];
        Parallel.For(0, threadCount, i =>
        {
            long start = i * chunkSize;
            long end = (i == threadCount - 1) ? fileSize : start + chunkSize;
            Debug.WriteLine($"Starting chunk processor: {i + 1:D2}/{threadCount}, start: {start}, end: {end}");
            var results = ProcessChunk(path, start, end, Encoding.ASCII, ct, b =>
            {
                bytesProcessed[i] = b;
                progressCallback?.Invoke((double)bytesProcessed.Sum() / fileSize);
            });
            bag[i] = results;
        });

        List<StringResult> results = new();

        foreach (List<StringResult> result in bag)
        {
            results.AddRange(result);
        }

        AllStringResults = results;
        Debug.WriteLine($"Found strings: {results.Count}");
        FilteredStrings = new ObservableCollection<StringResult>(AllStringResults);
        Dispatcher.UIThread.Invoke(() => { StringsSource.Items = FilteredStrings; });
    }

    private bool IsPrintable(byte c)
    {
        if (c is >= 0x20 and <= 0x7e or 0x09 /*|| c == 0x0a*/)
        {
            return true;
        }

        return false;
    }

    private List<StringResult> ProcessChunk(string path, long start, long end, Encoding encoding, CancellationToken ct,
        Action<long>? progressCallback)
    {
        // Skip to this chunk's start point
        using FileStream fs = new(path, FileMode.Open, FileAccess.Read);

        byte[] buff = new byte[BlockSize];

        StringBuilder currentString = new();
        long currentStringStart = 0;

        List<StringResult> foundStrings = [];

        int numRead;
        var totalRead = 0;
        bool isFirstChunk = start == 0;

        bool hasSkippedPastFirstNonString = true;

        if (isFirstChunk)
        {
            fs.Seek(start, SeekOrigin.Begin);
        }
        else
        {
            // For every chunk besides the first, we need to read one byte prior and check if it's non-printable
            // If printable, then the first character here is a part of the previous chunk's last string.
            // If non-printable, then we can continue parsing.
            // TODO: This still won't work for multibyte encodings though!
            hasSkippedPastFirstNonString = false;

            fs.Seek(start - 1, SeekOrigin.Begin);
            byte[] c = new byte[1];
            numRead = fs.Read(c, 0, 1);
            if (numRead == 0)
            {
                throw new InvalidDataException("Could not read previous chunk's last byte");
            }

            if (IsPrintable(c[0]))
            {
                hasSkippedPastFirstNonString = true;
            }
        }

        while ((numRead = fs.Read(buff, 0, BlockSize)) > 0)
        {
            if (ct.IsCancellationRequested) return [];
            progressCallback?.Invoke(totalRead);

            totalRead += numRead;

            if (numRead < buff.Length)
            {
                // We're reusing the buffer, need to clear any previous data out
                Array.Fill(buff, (byte)0, numRead, buff.Length - numRead);
            }

            for (var i = 0; i < numRead; i++)
            {
                byte c = buff[i];

                if (IsPrintable(c))
                {
                    // Is a string

                    // If we're not the first chunk, need to skip to the first non-string character, since before that will be
                    // handled by the previous chunk's processor as part of overlap
                    // TODO: There is definitely an edge case here -- what if previous chunk ended at a null? Then first char of this chunk will be string char that we need to use, not skip
                    if (!hasSkippedPastFirstNonString)
                    {
                        // Still in the previous chunk's string, skip
                        continue;
                    }

                    if (currentString.Length == 0)
                    {
                        currentStringStart = fs.Position - numRead + i;
                    }

                    currentString.Append((char)c);
                }
                else
                {
                    // Not a string
                    if (!hasSkippedPastFirstNonString) hasSkippedPastFirstNonString = true;

                    if (currentString.Length >= MinLen)
                    {
                        foundStrings.Add(new StringResult(currentString.ToString(), currentStringStart));
                    }

                    currentString.Clear();
                    if (fs.Position - numRead + i >= end)
                    {
                        return foundStrings;
                    }
                }
            }
        }

        if (currentString.Length >= MinLen)
        {
            foundStrings.Add(new StringResult(currentString.ToString(), currentStringStart));
        }

        return foundStrings;
    }
}