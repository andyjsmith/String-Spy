﻿using System;
using Avalonia.Controls;
using Avalonia.Controls.Models.TreeDataGrid;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StringSpy.Models;
using StringSpy.Settings;
using StringSpy.Strings;

namespace StringSpy.ViewModels;

// Using auto-generated OnChanged methods from ObservableProperty to call others, sometimes without using the value 
[SuppressMessage("ReSharper", "UnusedParameterInPartialMethod")]
public partial class StringsViewModel : ViewModelBase
{
    // Strings

    [ObservableProperty]
    private int _minimumStringLength = SettingsManager.Instance.Settings.DefaultMinimumStringLength;

    partial void OnMinimumStringLengthChanged(int value) => ReloadFile();

    private const int BlockSize = 4 << 20; // 4 MiB

    [ObservableProperty] private List<StringResult> _allStringResults = [];
    [ObservableProperty] private ObservableCollection<StringResult> _filteredStrings = [];
    [ObservableProperty] private FlatTreeDataGridSource<StringResult> _stringsSource;

    partial void OnStringsSourceChanged(FlatTreeDataGridSource<StringResult> value)
    {
        if (value.RowSelection == null) return;
        value.RowSelection.SelectionChanged +=
            (_, args) => SelectedString = args.SelectedItems.FirstOrDefault();
    }

    [ObservableProperty] private bool _isSearchTextValid = true;

    [ObservableProperty] private StringResult? _selectedString;

    private static Character.CharSet SelectedCharSet =>
        Character.StringToCharSet(SettingsManager.Instance.Settings.CharacterSet);

    // UI

    [ObservableProperty] private FontFamily _font = SettingsManager.Instance.Settings.Font;

    partial void OnFontChanged(FontFamily value) => OnPropertyChanged(nameof(Font));

    public static FontFamily FontValue => SettingsManager.Instance.Settings.FontValue;

    // Search

    private Task? SearchTask { get; set; }
    private CancellationTokenSource _searchTaskCts = new();

    [ObservableProperty] private int _searchProgress;
    [ObservableProperty] private string _searchText = "";

    partial void OnSearchTextChanged(string value)
    {
        if (SettingsManager.Instance.Settings.AutomaticSearch) RunSearch();
    }

    /// <summary>
    /// Treat string search text as regex
    /// </summary>
    [ObservableProperty] private bool _isRegexEnabled = SettingsManager.Instance.Settings.DefaultUseRegex;
    partial void OnIsRegexEnabledChanged(bool value) => RunSearch();

    /// <summary>
    /// Treat string search text as case-insensitive
    /// </summary>
    [ObservableProperty] private bool _isCaseSensitiveEnabled = SettingsManager.Instance.Settings.DefaultCaseSensitive;
    partial void OnIsCaseSensitiveEnabledChanged(bool value) => RunSearch();

    /// <summary>
    /// Validate that the given text is valid (non-zero length and valid regex if enabled)
    /// </summary>
    /// <param name="searchText">Text to search for</param>
    /// <returns>True if valid, false if invalid</returns>
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

    /// <summary>
    /// Cancel the search task if running
    /// </summary>
    private void CancelSearch()
    {
        if (SearchTask?.IsCompleted ?? true) return;
        _searchTaskCts.Cancel();
        Debug.WriteLine("Cancelling search task");
    }

    [ObservableProperty] private string? _progressText;
    [ObservableProperty] private double _progressValue;

    private CancellationTokenSource? ProcessCancellationTokenSource { get; set; }

    /// <summary>
    /// Cancel the current strings loading task
    /// </summary>
    [RelayCommand]
    public void CancelTask()
    {
        ProcessCancellationTokenSource?.Cancel();
        ProcessCancellationTokenSource = null;
        ProgressText = null;
    }

    [ObservableProperty] private string? _loadedFile;

    /// <summary>
    /// Remove all string results, cancel search, and unset the open file
    /// </summary>
    [RelayCommand]
    public void CloseFile()
    {
        CancelSearch();
        AllStringResults.Clear();
        FilteredStrings.Clear();
        LoadedFile = null;
    }

    /// <summary>
    /// Event to trigger code-behind to focus the search box
    /// </summary>
    public event Action? FocusSearchBoxEvent;

    /// <summary>
    /// Put focus / the cursor into the search box 
    /// </summary>
    public void FocusSearchBox() => FocusSearchBoxEvent?.Invoke();

    public delegate void SelectionChangedAction(int index);

    /// <summary>
    /// Event to tell code-behind that the tree row selection has changed, since TreeDataGrid doesn't yet support this
    /// </summary>
    public event SelectionChangedAction? SelectionChanged;

    private bool IsGoToDialogVisible { get; set; } = false;

    /// <summary>
    /// Open the goto dialog and select and scroll to the resulting string
    /// </summary>
    [RelayCommand]
    public async Task ShowGoToDialog()
    {
        // Only allow one instance
        if (IsGoToDialogVisible) return;

        IsGoToDialogVisible = true;
        var vm = new GoToDialogViewModel();
        long? val = await vm.ShowAsync();
        IsGoToDialogVisible = false;
        if (val == null) return;
        if (StringsSource.Rows.Count == 0) return;

        // Scroll to address
        for (int i = FilteredStrings.Count - 1; i >= 0; i--)
        {
            if (val < FilteredStrings[i].Position) continue;

            StringsSource.RowSelection!.SelectedIndex = i;
            SelectionChanged?.Invoke(i);
            return;
        }

        // Value is less than first index, so select the first row
        StringsSource.RowSelection!.SelectedIndex = 0;
        SelectionChanged?.Invoke(0);
    }

    /// <summary>
    /// Validate search text and run search
    /// </summary>
    [RelayCommand]
    public void RunSearch()
    {
        IsSearchTextValid = ValidateSearchText(SearchText);
        if (!IsSearchTextValid) return;
        CancelSearch();

        _searchTaskCts = new CancellationTokenSource();
        SearchTask = Search(SearchText, _searchTaskCts.Token);
    }

    /// <summary>
    /// How many strings to search between progress update events
    /// </summary>
    private const int SearchProgressUpdateInterval = 10_000;

    /// <summary>
    /// Search all strings for the provided text using options set in the GUI and settings
    /// </summary>
    /// <param name="searchText">Text to search for</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Search task</returns>
    private Task Search(string searchText, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            List<StringResult> filtered = [];

            if (searchText == string.Empty)
            {
                FilteredStrings = new ObservableCollection<StringResult>(AllStringResults);
                Dispatcher.UIThread.Invoke(() => { StringsSource.Items = FilteredStrings; });
                SearchProgress = 0;
                return;
            }

            try
            {
                bool useMultithreadedSearch = SettingsManager.Instance.Settings.ParallelSearchThreshold switch
                {
                    < 0 => false,
                    0 => true,
                    _ => AllStringResults.Count >= SettingsManager.Instance.Settings.ParallelSearchThreshold
                };

                Func<StringResult, bool> filterFunc;
                if (IsRegexEnabled)
                {
                    var regexOptions = RegexOptions.Compiled;
                    if (!IsCaseSensitiveEnabled) regexOptions |= RegexOptions.IgnoreCase;

                    Regex re;
                    try
                    {
                        re = new Regex(searchText, regexOptions);
                    }
                    catch (ArgumentException)
                    {
                        return;
                    }

                    filterFunc = s => re.IsMatch(s.Content);
                }
                else
                {
                    var comparisonType = StringComparison.OrdinalIgnoreCase;
                    if (IsCaseSensitiveEnabled) comparisonType = StringComparison.Ordinal;
                    filterFunc = s => s.Content.Contains(searchText, comparisonType);
                }

                if (useMultithreadedSearch)
                {
                    int threadCount = Environment.ProcessorCount;
                    int chunkSize = AllStringResults.Count / threadCount;

                    var results = new List<StringResult>[threadCount];
                    Parallel.For(0, threadCount, i =>
                    {
                        int start = i * chunkSize;
                        int end = (i == threadCount - 1) ? AllStringResults.Count : start + chunkSize;
                        Debug.WriteLine(
                            $"Starting string processor: {i + 1:D2}/{threadCount}, start: {start}, end: {end}");
                        results[i] = [];
                        for (int j = start; j < end; j++)
                        {
                            if (ct.IsCancellationRequested) return;

                            if (filterFunc(AllStringResults[j])) results[i].Add(AllStringResults[j]);

                            if ((j - start) % SearchProgressUpdateInterval != 0) continue;
#pragma warning disable MVVMTK0034
                            Interlocked.Add(ref _searchProgress, SearchProgressUpdateInterval);
#pragma warning restore MVVMTK0034
                            OnPropertyChanged(nameof(SearchProgress));
                        }
                    });

                    foreach (var result in results)
                    {
                        filtered.AddRange(result);
                    }
                }
                else
                {
                    for (var i = 0; i < AllStringResults.Count; i++)
                    {
                        if (i % SearchProgressUpdateInterval == 0) SearchProgress = i;
                        if (filterFunc(AllStringResults[i])) filtered.Add(AllStringResults[i]);
                        ct.ThrowIfCancellationRequested();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("Search cancelled with exception");
                return;
            }

            FilteredStrings = new ObservableCollection<StringResult>(filtered);
            Dispatcher.UIThread.Invoke(() => { StringsSource.Items = FilteredStrings; });
            SearchProgress = 0;
        }, ct);
    }

    /// <summary>
    /// All available encodings to use for string decoding
    /// </summary>
    private List<Encoding> AllEncodings { get; }

    private Encoding _selectedEncoding = Encoding.ASCII;
    /// <summary>
    /// Encoding to use for byte decoding string searching
    /// </summary>
    [AllowNull]
    public Encoding SelectedEncoding
    {
        get => _selectedEncoding;
        set
        {
            if (value == null) return;
            _selectedEncoding = value;
            ReloadFile();
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Filtered encodings using EncodingFilter
    /// </summary>
    [ObservableProperty] private List<Encoding> _filteredEncodings;

    /// <summary>
    /// Search string for filtering encoding options
    /// </summary>
    [ObservableProperty] private string _encodingFilter = "";

    partial void OnEncodingFilterChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(EncodingFilter))
        {
            FilteredEncodings = AllEncodings;
            return;
        }

        // Filter encodings case-insensitively based on EncodingName or WebName
        FilteredEncodings = AllEncodings.Where(e =>
            e.EncodingName.Contains(value, StringComparison.OrdinalIgnoreCase) ||
            e.WebName.Contains(value, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    [ObservableProperty] private OffsetFormatter _selectedOffsetFormatter =
        OffsetFormatter.StringToOffsetFormatter(SettingsManager.Instance.Settings.DefaultAddressFormat);

    partial void OnSelectedOffsetFormatterChanged(OffsetFormatter value)
    {
        // Recreate the entire StringsSource to force the tree to rerender
        StringsSource = CreateStringsSource(value);
    }

    public static IReadOnlyList<OffsetFormatter> AllOffsetFormatters => OffsetFormatter.Formatters;

    /// <summary>
    /// Create a new StringsSource. Used to dynamically change the offset formatter.
    /// </summary>
    /// <param name="offsetFormatter">Offset formatter to use for addresses</param>
    /// <returns>Source to use for TreeDataGrid</returns>
    private FlatTreeDataGridSource<StringResult> CreateStringsSource(OffsetFormatter offsetFormatter)
    {
        return new FlatTreeDataGridSource<StringResult>(FilteredStrings)
        {
            Columns =
            {
                new TextColumn<StringResult, string>("Start", x => offsetFormatter.Format(x.Position))
                {
                    Options = { TextAlignment = TextAlignment.End }
                },
                new TextColumn<StringResult, string>("End", x => offsetFormatter.Format(x.EndPosition))
                {
                    Options = { TextAlignment = TextAlignment.End }
                },
                new TextColumn<StringResult, string>("String", x => x.Content)
                {
                    Options = { TextTrimming = TextTrimming.CharacterEllipsis }
                }
            }
        };
    }

    /// <summary>
    /// Reprocess the already loaded file, using any new parameters if applicable
    /// </summary>
    private void ReloadFile()
    {
        if (LoadedFile is null) return;
        OpenFile(LoadedFile);
    }

    /// <summary>
    /// Open a file and process it for strings, handling UI updates
    /// </summary>
    /// <param name="path"></param>
    public async void OpenFile(string path)
    {
        CloseFile();
        LoadedFile = path;
        Debug.WriteLine($"Loading file: {path}");
        if (ProcessCancellationTokenSource != null)
        {
            await ProcessCancellationTokenSource.CancelAsync();
        }

        ProcessCancellationTokenSource = new CancellationTokenSource();

        ProgressValue = 0;
        ProgressText = $"Loading file: {path}";

        try
        {
            var sw = Stopwatch.StartNew();
            await Task.Run(() =>
            {
                try
                {
                    ProcessFile(path, Environment.ProcessorCount,
                        progress => { ProgressValue = progress * 100.0; },
                        ProcessCancellationTokenSource.Token);
                }
                catch (Exception e)
                {
                    ShowErrorDialog(e);
                }
            }, ProcessCancellationTokenSource.Token);
            sw.Stop();
            Debug.WriteLine($"Finished loading file in {sw.Elapsed}");
        }
        catch (TaskCanceledException)
        {
            Debug.WriteLine("Loading file cancelled");
        }

        ProgressValue = 100;
        ProgressText = null;
    }

    /// <summary>
    /// Export the string results to a file, one string per line
    /// </summary>
    /// <param name="path">Path of the output file</param>
    /// <param name="exportAll">True to export all strings, false to only export the search-filtered strings</param>
    /// <returns>The export task</returns>
    public Task Export(string path, bool exportAll = false)
    {
        return Task.Run(async () =>
        {
            ProgressValue = 0;
            ProgressText = $"Exporting to file: {path}";

            Debug.WriteLine($"Exporting to file: {path}");
            try
            {
                await using FileStream file = File.Open(path, FileMode.Create);
                IList<StringResult> results = FilteredStrings;
                if (exportAll) results = AllStringResults;

                byte[] newline = Encoding.UTF8.GetBytes(Environment.NewLine);
                for (var i = 0; i < results.Count; i++)
                {
                    file.Write(Encoding.UTF8.GetBytes(results[i].Content));
                    file.Write(newline);
                    if (i % 10000 == 0) ProgressValue = i * 100.0 / results.Count;
                }
            }
            catch (Exception e)
            {
                ShowErrorDialog(e);
            }

            ProgressValue = 100;
            ProgressText = null;
        });
    }

    /// <summary>
    /// Process an input file to find strings
    /// </summary>
    /// <param name="path">Path to input file</param>
    /// <param name="threadCount">Number of threads to use for parallel searching</param>
    /// <param name="progressCallback">Callback for setting progress, 0 to 1</param>
    /// <param name="ct">Cancellation token</param>
    public void ProcessFile(string path, int threadCount, Action<double>? progressCallback, CancellationToken ct)
    {
        FileInfo fileInfo = new(path);
        long fileSize = fileInfo.Length;
        long chunkSize = fileSize / threadCount;
        var bytesProcessed = new long[threadCount];
        var bag = new List<StringResult>[threadCount];
        if (threadCount == 1)
        {
            long start = 0;
            long end = fileSize;
            Debug.WriteLine($"Starting chunk processor: start: {start}, end: {end}");
            var stringResults = ProcessFileChunk(path, start, end, SelectedEncoding, SelectedCharSet, b =>
            {
                bytesProcessed[0] = b;
                progressCallback?.Invoke((double)bytesProcessed.Sum() / fileSize);
            }, ct);
            bag[0] = stringResults;
        }
        else
        {
            Parallel.For(0, threadCount, threadIndex =>
            {
                long start = threadIndex * chunkSize;
                long end = (threadIndex == threadCount - 1) ? fileSize : start + chunkSize;
                Debug.WriteLine(
                    $"Starting parallel chunk processor: {threadIndex + 1:D2}/{threadCount}, start: {start}, end: {end}");
                var stringResults = ProcessFileChunk(path, start, end, SelectedEncoding, SelectedCharSet, b =>
                {
                    bytesProcessed[threadIndex] = b;
                    progressCallback?.Invoke((double)bytesProcessed.Sum() / fileSize);
                }, ct);
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

        AllStringResults = results;
        Debug.WriteLine($"Found strings: {results.Count}");
        FilteredStrings = new ObservableCollection<StringResult>(AllStringResults);
        Dispatcher.UIThread.Invoke(() => { StringsSource.Items = FilteredStrings; });
    }

    /// <summary>
    /// Process a specified chunk of a file to find strings
    /// </summary>
    /// <param name="path">Path to input file</param>
    /// <param name="start">Address in the file to start searching</param>
    /// <param name="end">Address in the file to end searching</param>
    /// <param name="encoding">Encoding to use when decoding the file bytes to text</param>
    /// <param name="charSet">Character set to use when determining if a character is printable</param>
    /// <param name="progressCallback">Progress callback with numbere of bytes that have been processed in this chunk</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Found strings</returns>
    private List<StringResult> ProcessFileChunk(string path, long start, long end, Encoding encoding,
        Character.CharSet charSet, Action<long>? progressCallback, CancellationToken ct)
    {
        // Skip to this chunk's start point
        using FileStream fs = new(path, FileMode.Open, FileAccess.Read);
        fs.Seek(start, SeekOrigin.Begin);

        // Get max byte count of a char in this encoding
        int maxBytesPerChar = encoding.GetMaxByteCount(1);
        int maxCharCount = encoding.GetMaxCharCount(maxBytesPerChar);
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

        // Set the encoding to use null byte as replacement character when a char isn't valid for the encoding,
        // this will just act as a non-printable char and end the string.
        encoding = (Encoding)encoding.Clone();
        encoding.DecoderFallback = new DecoderReplacementFallback("\0");

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
                int numCharsDecoded = encoding.GetChars(buff, i, maxBytesPerChar, charDecodeBuff, 0);

                if (numCharsDecoded == 0 || !Character.IsPrintable(charDecodeBuff[0], encoding, charSet))
                {
                    // Not printable here, reset and advance
                    if (currentString.Length >= MinimumStringLength)
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
                    i += encoding.GetByteCount(charDecodeBuff, 0, 1);
                }
            }
        }

        if (currentString.Length >= MinimumStringLength)
        {
            foundStrings.Add(new StringResult(currentString.ToString(), currentStringStart));
        }

        return foundStrings;
    }

    public StringsViewModel()
    {
        // Register all code pages from the System.Text.Encoding.CodePages package
        // .NET core doesn't include them by default
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        List<Encoding> prioritizedEncodings =
        [
            Encoding.ASCII,
            Encoding.UTF8,
            Encoding.Unicode, // UTF16-LE
            Encoding.BigEndianUnicode, // UTF16-BE
            Encoding.UTF32, // UTF32-LE
            Encoding.GetEncoding(12001), // UTF32-BE
            Encoding.Latin1,
            Encoding.GetEncoding(1252) // Windows-1252
        ];

        // Add prioritized encodings to the top of encodings list for easier visibility,
        // followed by the rest of the non-prioritized encodings
        AllEncodings = prioritizedEncodings.Concat(
            Encoding.GetEncodings()
                .Select(e => e.GetEncoding())
                .Except(prioritizedEncodings)
                .OrderBy(e => e.EncodingName)
        ).ToList();

        FilteredEncodings = AllEncodings;
        try
        {
            SelectedEncoding = Encoding.GetEncoding(SettingsManager.Instance.Settings.DefaultEncoding);
        }
        catch (ArgumentException)
        {
        }

        StringsSource = CreateStringsSource(SelectedOffsetFormatter);
    }
}