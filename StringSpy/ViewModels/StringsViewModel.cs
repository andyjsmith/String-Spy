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
using StringSpy.Models;
using StringSpy.Settings;

namespace StringSpy.ViewModels;

// Using auto-generated OnChanged methods from ObservableProperty to call others, sometimes without using the value 
[SuppressMessage("ReSharper", "UnusedParameterInPartialMethod")]
public partial class StringsViewModel : ViewModelBase
{
    // Strings

    [ObservableProperty]
    private int _minimumStringLength = SettingsManager.Instance.Settings.DefaultMinimumStringLength;

    partial void OnMinimumStringLengthChanged(int value) => ReloadFile();

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

    private static CharSet SelectedCharSet =>
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

        SearchTask = Task.Run(() =>
        {
            var searchResults = StringsFinder.FilterStrings(AllStringResults, SearchText, IsRegexEnabled,
                IsCaseSensitiveEnabled,
                progress =>
                {
#pragma warning disable MVVMTK0034
                    Interlocked.Add(ref _searchProgress, progress);
#pragma warning restore MVVMTK0034
                    OnPropertyChanged(nameof(SearchProgress));
                }, _searchTaskCts.Token);

            SearchProgress = 0;
            if (searchResults == null) return;
            FilteredStrings = new ObservableCollection<StringResult>(searchResults);
            Dispatcher.UIThread.Invoke(() => { StringsSource.Items = FilteredStrings; });
        });
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
                    var sf = new StringsFinder(path, SelectedEncoding, SelectedCharSet, MinimumStringLength,
                        Environment.ProcessorCount);
                    var results = sf.FindStrings(progress => { ProgressValue = progress * 100.0; },
                        ProcessCancellationTokenSource.Token);

                    AllStringResults = results;
                    Debug.WriteLine($"Found strings: {results.Count}");
                    FilteredStrings = new ObservableCollection<StringResult>(AllStringResults);
                    Dispatcher.UIThread.Invoke(() => { StringsSource.Items = FilteredStrings; });
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