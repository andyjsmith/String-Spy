using System;
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
using StringsApp.Models;
using StringsApp.Settings;
using StringsApp.Strings;

namespace StringsApp.ViewModels;

// Using auto-generated OnChanged methods from ObservableProperty to call others, sometimes without using the value 
[SuppressMessage("ReSharper", "UnusedParameterInPartialMethod")]
public partial class StringsViewModel : ViewModelBase
{
    // Strings

    [ObservableProperty]
    private int _minimumStringLength = SettingsManager.Instance.AppSettings.DefaultMinimumStringLength;

    [ObservableProperty] private FontFamily _font = SettingsManager.Instance.AppSettings.Font;

    partial void OnMinimumStringLengthChanged(int value) => ReloadFile();

    private const int BlockSize = 4 << 20; // 4 MiB

    [ObservableProperty] private List<StringResult> _allStringResults = [];

    [ObservableProperty] private ObservableCollection<StringResult> _filteredStrings = [];

    [ObservableProperty] private FlatTreeDataGridSource<StringResult> _stringsSource;

    [ObservableProperty] private bool _isSearchTextValid = true;

    private Character.CharSet SelectedCharSet =>
        Character.StringToCharSet(SettingsManager.Instance.AppSettings.CharacterSet);

    // Search

    private Task? SearchTask { get; set; }
    private CancellationTokenSource _searchTaskCts = new();

    [ObservableProperty] private int _searchProgress;

    [ObservableProperty] private string _searchText = "";

    partial void OnSearchTextChanged(string value)
    {
        if (SettingsManager.Instance.AppSettings.AutomaticSearch) RunSearch();
    }

    [ObservableProperty] private bool _isRegexEnabled = SettingsManager.Instance.AppSettings.DefaultUseRegex;
    partial void OnIsRegexEnabledChanged(bool value) => RunSearch();

    [ObservableProperty]
    private bool _isCaseSensitiveEnabled = SettingsManager.Instance.AppSettings.DefaultCaseSensitive;

    partial void OnIsCaseSensitiveEnabledChanged(bool value) => RunSearch();

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

    [ObservableProperty] private string? _progressText;
    [ObservableProperty] private double _progressValue;

    private CancellationTokenSource? ProcessCancellationTokenSource { get; set; }

    [RelayCommand]
    public void CancelTask()
    {
        ProcessCancellationTokenSource?.Cancel();
        ProcessCancellationTokenSource = null;
        ProgressText = null;
    }

    [ObservableProperty] private string? _loadedFile;

    private void ClearResults()
    {
        CancelSearch();
        AllStringResults.Clear();
        AllStringResults = [];
        FilteredStrings.Clear();
        FilteredStrings = [];
    }

    [RelayCommand]
    public void CloseFile()
    {
        ClearResults();
        LoadedFile = null;
    }

    public event Action? FocusSearchBoxEvent;
    public void FocusSearchBox() => FocusSearchBoxEvent?.Invoke();

    public delegate void SelectionChangedAction(int index);
    public event SelectionChangedAction? SelectionChanged;
    
    private bool IsGoToDialogVisible { get; set; } = false;

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

    [RelayCommand]
    public void RunSearch()
    {
        IsSearchTextValid = ValidateSearchText(SearchText);
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
                FilteredStrings = new ObservableCollection<StringResult>(AllStringResults);
                Dispatcher.UIThread.Invoke(() => { StringsSource.Items = FilteredStrings; });
                SearchProgress = 0;
                return;
            }

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

            FilteredStrings = new ObservableCollection<StringResult>(filtered);
            Dispatcher.UIThread.Invoke(() => { StringsSource.Items = FilteredStrings; });
            SearchProgress = 0;
        }, ct);
    }

    private List<Encoding> AllEncodings { get; }

    [ObservableProperty] private List<Encoding> _filteredEncodings;

    [ObservableProperty] private Encoding _selectedEncoding;
    partial void OnSelectedEncodingChanged(Encoding value) => ReloadFile();

    [ObservableProperty] private string _encodingFilter = "";

    partial void OnEncodingFilterChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(EncodingFilter))
        {
            FilteredEncodings = AllEncodings;
            return;
        }

        List<Encoding> encodings = AllEncodings.Where(e =>
            e.EncodingName.Contains(value, StringComparison.OrdinalIgnoreCase) ||
            e.WebName.Contains(value, StringComparison.OrdinalIgnoreCase)).ToList();
        FilteredEncodings = encodings;
    }

    [ObservableProperty] private OffsetFormatter _selectedOffsetFormatter =
        StringToOffsetFormatter(SettingsManager.Instance.AppSettings.DefaultAddressFormat);

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

    private static OffsetFormatter StringToOffsetFormatter(string s)
    {
        return s switch
        {
            "Hexadecimal" => OffsetFormatter.Hexadecimal,
            "Octal" => OffsetFormatter.Octal,
            "Decimal" => OffsetFormatter.Decimal,
            _ => OffsetFormatter.Hexadecimal
        };
    }

    public List<OffsetFormatter> OffsetFormatters { get; } =
    [
        OffsetFormatter.Hexadecimal,
        OffsetFormatter.Decimal,
        OffsetFormatter.Octal
    ];

    private void ReloadFile()
    {
        if (LoadedFile is null) return;
        OpenFile(LoadedFile);
    }

    public async void OpenFile(string path)
    {
        // TODO: Handle exceptions here (insufficient permissions to open file, etc.)
        ClearResults();
        LoadedFile = path;
        Debug.WriteLine($"Loading file: {path}");
        if (ProcessCancellationTokenSource != null)
        {
            await ProcessCancellationTokenSource.CancelAsync();
        }

        ProcessCancellationTokenSource = new CancellationTokenSource();

        ProgressValue = 0;
        ProgressText = $"Loading file: {path}";

        Stopwatch sw = Stopwatch.StartNew();
        await Task.Run(() =>
        {
            RunParallel(path, Environment.ProcessorCount,
                progress => { ProgressValue = progress * 100.0; },
                ProcessCancellationTokenSource.Token);
        }, ProcessCancellationTokenSource.Token);
        sw.Stop();
        Debug.WriteLine($"Finished loading file in {sw.Elapsed}");
        ProgressValue = 100;
        ProgressText = null;
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
            SelectedEncoding = Encoding.GetEncoding(SettingsManager.Instance.AppSettings.DefaultEncoding);
        }
        catch (ArgumentException)
        {
            SelectedEncoding = Encoding.ASCII;
        }

        StringsSource = CreateStringsSource(SelectedOffsetFormatter);
    }

    public void RunParallel(string path, int threadCount, Action<double>? progressCallback, CancellationToken ct)
    {
        FileInfo fileInfo = new(path);
        long fileSize = fileInfo.Length;
        long chunkSize = fileSize / threadCount;
        long[] bytesProcessed = new long[threadCount];
        var bag = new List<StringResult>[threadCount];
        if (threadCount == 1)
        {
            long start = 0;
            long end = fileSize;
            Debug.WriteLine($"Starting chunk processor: start: {start}, end: {end}");
            var stringResults = ProcessChunk(path, start, end, SelectedEncoding, SelectedCharSet, ct, b =>
            {
                bytesProcessed[0] = b;
                progressCallback?.Invoke((double)bytesProcessed.Sum() / fileSize);
            });
            bag[0] = stringResults;
        }
        else
        {
            Parallel.For(0, threadCount, i =>
            {
                long start = i * chunkSize;
                long end = (i == threadCount - 1) ? fileSize : start + chunkSize;
                Debug.WriteLine(
                    $"Starting parallel chunk processor: {i + 1:D2}/{threadCount}, start: {start}, end: {end}");
                var results = ProcessChunk(path, start, end, SelectedEncoding, SelectedCharSet, ct, b =>
                {
                    bytesProcessed[i] = b;
                    progressCallback?.Invoke((double)bytesProcessed.Sum() / fileSize);
                });
                bag[i] = results;
            });
        }

        List<StringResult> results = [];
        foreach (List<StringResult> result in bag)
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

    private List<StringResult> ProcessChunk(string path, long start, long end, Encoding encoding,
        Character.CharSet charSet, CancellationToken ct, Action<long>? progressCallback)
    {
        // Skip to this chunk's start point
        using FileStream fs = new(path, FileMode.Open, FileAccess.Read);
        fs.Seek(start, SeekOrigin.Begin);

        // Get max byte count of a char in this encoding
        int maxBytesPerChar = encoding.GetMaxByteCount(1);
        int maxCharCount = encoding.GetMaxCharCount(maxBytesPerChar);
        Debug.Assert(maxBytesPerChar > 0);

        byte[] buff = new byte[BlockSize + maxBytesPerChar - 1];

        StringBuilder currentString = new();
        long currentStringStart = 0;

        List<StringResult> foundStrings = [];

        int numBytesRead;
        var totalBytesRead = 0;

        // Set the encoding to use null byte as replacement character when a char isn't valid for the encoding,
        // this will just act as a non-printable char and end the string.
        encoding = (Encoding)encoding.Clone();
        encoding.DecoderFallback = new DecoderReplacementFallback("\0");

        // Create a char buffer of that length
        // TODO: Reuse a char buffer instead
        var charDecodeBuff = new char[maxCharCount];

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
}