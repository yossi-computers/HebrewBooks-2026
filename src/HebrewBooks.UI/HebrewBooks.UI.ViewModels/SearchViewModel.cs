using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;
using CommunityToolkit.Mvvm.Input;
using HebrewBooks.Core.Abstractions;
using HebrewBooks.Core.Models;
using HebrewBooks.Infrastructure.Settings;
using HebrewBooks.Services.Catalog;
using HebrewBooks.Services.Search;
using HebrewBooks.UI.Collections;
using HebrewBooks.UI.Controls;
using HebrewBooks.UI.Navigation;
using HebrewBooks.UI.Resources;
using HebrewBooks.UI.Services;
using Serilog;

namespace HebrewBooks.UI.ViewModels;

public partial class SearchViewModel : ObservableObject, IHitStripNavigator
{
	private readonly SearchOrchestrator _orchestrator;

	private readonly UsageTelemetryService _usage;

	private bool _autoSelectingResult;

	private readonly RemoteSearchClient _remote = new RemoteSearchClient();

	private readonly WebApiClient _webApi = new WebApiClient();

	private readonly IPathResolver _paths;

	private readonly SearchHistoryStore _history;

	private readonly SearchResultsCacheStore _resultsCache;

	private readonly JsonSettingsStore _settings;

	private readonly SynonymLookup _synonyms;

	private readonly PerformanceAdvisor _perfAdvisor;

	private bool _settingsLoaded;

	[ObservableProperty]
	private string _queryText = string.Empty;

	[ObservableProperty]
	private int _maxProximity = 30;

	[ObservableProperty]
	private bool _hybur;

	[ObservableProperty]
	private bool _rasheyTevot;

	[ObservableProperty]
	private bool _rootSearch;

	[ObservableProperty]
	private bool _expandNumberGender;

	[ObservableProperty]
	private bool _expandGematria;

	[ObservableProperty]
	private bool _expandSpelling;

	[ObservableProperty]
	private bool _expandAramaic;

	[ObservableProperty]
	private bool _expandRashiOcr;

	[ObservableProperty]
	private bool _expandWeakLetters;

	[ObservableProperty]
	private int _fuzziness;

	[ObservableProperty]
	private bool _requireWordOrder;

	[ObservableProperty]
	[NotifyCanExecuteChangedFor("CancelSearchCommand")]
	private bool _isBusy;

	[ObservableProperty]
	private string _statusText = SharedStrings.StatusReady;

	[ObservableProperty]
	private SearchResultRow? _selectedRow;

	[ObservableProperty]
	private string? _pdfPath;

	[ObservableProperty]
	private int _pdfPage = 1;

	[ObservableProperty]
	private bool _isTextMode;

	[ObservableProperty]
	private string? _currentTextRelativePath;

	[ObservableProperty]
	private bool _hasMultipleSources;

	[ObservableProperty]
	private DataGridRowDetailsVisibilityMode _rowDetailsMode = DataGridRowDetailsVisibilityMode.VisibleWhenSelected;

	private bool _suppressCorpusSync;

	private const string AllCorpora = "All";

	private HashSet<string> _selectedCorpora = new HashSet<string>(StringComparer.Ordinal);

	private int _individualCorpusCount;

	[ObservableProperty]
	private SearchScopeOption? _selectedScope;

	[ObservableProperty]
	private InBookHitInfo? _currentBookHits;

	[ObservableProperty]
	private string? _markedPdfPath;

	[ObservableProperty]
	private bool _isBookLoading;

	[ObservableProperty]
	private int _currentMatchIndex = -1;

	[ObservableProperty]
	private string _matchPositionText = string.Empty;

	[ObservableProperty]
	private string _inBookSearchStatus = string.Empty;

	[ObservableProperty]
	private int _currentResultIndex = -1;

	[ObservableProperty]
	private string _resultPositionText = string.Empty;

	[ObservableProperty]
	private string _resultFilterText = string.Empty;

	private ICollectionView? _resultsView;

	private bool _categoriesInitialized;

	private CancellationTokenSource? _openDebounce;

	private CancellationTokenSource? _hitFetchCts;

	private CancellationTokenSource? _searchCts;

	private long _searchStartedTicks;

	private string? _displayedFileId;

	private string? _displayedQueryAtOpen;

	private readonly RasheyTevotMap _rasheyTevotMap;

	private readonly HebAramMap _hebAramMap;

	private readonly ICatalogRepository _catalogRepo;

	private readonly MainViewModel _main;

	private readonly ISearchScopeContext _scope;

	private readonly NavigationHistory _navigation = new NavigationHistory();

	private bool _suppressNavigationRecord;

	private bool _resultsShown;

	[ObservableProperty]
	private string _activeChipsSummary = string.Empty;

	[ObservableProperty]
	private string _spellCorrectedFrom = string.Empty;

	private bool _skipSpellCorrection;

	private bool _chipsEnabled;

	private const int MaxSelectedChips = 6;

	private bool _revertingChip;

	private bool _wasCancelled;

	private string? _scopeFallbackHint;

	private const int MaxInlineXfilterFiles = 500;

	private static readonly TimeSpan SpellSuggestBudget = TimeSpan.FromSeconds(8.0);

	[ObservableProperty]
	[NotifyCanExecuteChangedFor("CancelSearchCommand")]
	private bool _isStopping;

	[ObservableProperty]
	private string _inBookSearchText = string.Empty;

	private CancellationTokenSource? _chipCts;

	[ObservableProperty]
	private int? _selectedHitPage;

	private bool _syncingSelectionFromViewer;

















	public IReadOnlyList<string> CurrentTextTerms { get; private set; } = Array.Empty<string>();

	public ObservableCollection<CorpusFilterOption> CorpusFilters { get; }

	public string CorpusFilterSummary
	{
		get
		{
			List<CorpusFilterOption> list = CorpusFilters.Where((CorpusFilterOption o) => o.Value != "All").ToList();
			List<CorpusFilterOption> list2 = list.Where((CorpusFilterOption o) => o.IsSelected).ToList();
			if (list2.Count == list.Count)
			{
				return SharedStrings.S741;
			}
			if (list2.Count == 0)
			{
				return SharedStrings.S742;
			}
			return string.Join(", ", list2.Select((CorpusFilterOption o) => o.Label));
		}
	}

	public IReadOnlyList<SearchScopeOption> ScopeOptions { get; } = new SearchScopeOption[3]
	{
		new SearchScopeOption(SearchScope.All, SharedStrings.S743),
		new SearchScopeOption(SearchScope.Displayed, SharedStrings.S879),
		new SearchScopeOption(SearchScope.Marked, SharedStrings.S880)
	};

	public RangeObservableCollection<SearchResultRow> Results { get; } = new RangeObservableCollection<SearchResultRow>();

	public TopicFilterViewModel Topics { get; } = new TopicFilterViewModel();

	public YearFilterViewModel Years { get; } = new YearFilterViewModel();

	public SortFilterViewModel Sorting { get; } = new SortFilterViewModel();

	public ObservableCollection<string> SearchHistory { get; } = new ObservableCollection<string>();

	public string ActiveQueryText { get; private set; } = string.Empty;

	public MainViewModel Main => _main;

	public NavigationHistory Navigation => _navigation;

	public bool CanGoBack => _navigation.CanGoBack;

	public bool CanGoForward => _navigation.CanGoForward;

	public ObservableCollection<SynonymChipGroup> SynonymGroups { get; } = new ObservableCollection<SynonymChipGroup>();

	private IEnumerable<SynonymChipVm> AllChips => SynonymGroups.SelectMany((SynonymChipGroup g) => g.Chips);

	public bool HasSynonymChips => SynonymGroups.Count > 0;

	public bool HasSelectedChips => AllChips.Any((SynonymChipVm c) => c.IsSelected);

	public string SearchWithSelectedLabel => $"{SharedStrings.S2267}{AllChips.Count((SynonymChipVm c) => c.IsSelected)})";

	public bool ShowChipSelector
	{
		get
		{
			if (HasSynonymChips)
			{
				return !_resultsShown;
			}
			return false;
		}
	}

	public bool HasActiveChips => !string.IsNullOrEmpty(ActiveChipsSummary);

	public bool HasSpellCorrection => !string.IsNullOrEmpty(SpellCorrectedFrom);

	public string StopButtonText
	{
		get
		{
			if (!IsStopping)
			{
				return SharedStrings.S24;
			}
			return SharedStrings.S769;
		}
	}

	public bool HasHitPages => (CurrentBookHits?.Pages.Count ?? 0) > 0;

	public bool ShowInBookChrome
	{
		get
		{
			if (HasHitPages)
			{
				return !IsTextMode;
			}
			return false;
		}
	}

	public bool ShowHitPagesStrip
	{
		get
		{
			if (ShowInBookChrome)
			{
				return _settings.Load().View.ShowHitPagesStrip;
			}
			return false;
		}
	}

	public bool CanSearchInBook
	{
		get
		{
			if (!string.IsNullOrEmpty(PdfPath))
			{
				return !IsTextMode;
			}
			return false;
		}
	}

	public string InBookDisplayQuery
	{
		get
		{
			if (!string.IsNullOrWhiteSpace(InBookSearchText))
			{
				return InBookSearchText;
			}
			return QueryText ?? string.Empty;
		}
	}






















































	public event Action<int>? ScrollToPageRequested;

	[RelayCommand]
	private void ToggleFirstWord()
	{
		string text = QueryText ?? string.Empty;
		if (text.StartsWith("▶", StringComparison.Ordinal))
		{
			string text2 = text;
			int length = "▶".Length;
			QueryText = text2.Substring(length, text2.Length - length).TrimStart();
		}
		else
		{
			QueryText = "▶ " + text.TrimStart();
		}
	}

	[RelayCommand]
	private void ToggleLastWord()
	{
		string text = QueryText ?? string.Empty;
		if (text.EndsWith("◀", StringComparison.Ordinal))
		{
			string text2 = text;
			int length = "◀".Length;
			QueryText = text2.Substring(0, text2.Length - length).TrimEnd();
		}
		else
		{
			QueryText = text.TrimEnd() + " ◀";
		}
	}

	private void OnShowRowDetailsChanged(bool value)
	{
		RowDetailsMode = (value ? DataGridRowDetailsVisibilityMode.VisibleWhenSelected : DataGridRowDetailsVisibilityMode.Collapsed);
	}

	private void RecomputeCorpusSelection()
	{
		_selectedCorpora = (from o in CorpusFilters
			where o.Value != "All" && o.IsSelected
			select o.Value).ToHashSet<string>(StringComparer.Ordinal);
		_individualCorpusCount = CorpusFilters.Count((CorpusFilterOption o) => o.Value != "All");
	}

	private void OnCorpusOptionChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName != "IsSelected" || _suppressCorpusSync)
		{
			return;
		}
		_suppressCorpusSync = true;
		try
		{
			CorpusFilterOption obj = (CorpusFilterOption)sender;
			CorpusFilterOption corpusFilterOption = CorpusFilters.First((CorpusFilterOption o) => o.Value == "All");
			List<CorpusFilterOption> list = CorpusFilters.Where((CorpusFilterOption o) => o.Value != "All").ToList();
			if (obj.Value == "All")
			{
				foreach (CorpusFilterOption item in list)
				{
					item.IsSelected = corpusFilterOption.IsSelected;
				}
			}
			else
			{
				corpusFilterOption.IsSelected = list.All((CorpusFilterOption o) => o.IsSelected);
			}
		}
		finally
		{
			_suppressCorpusSync = false;
		}
		RecomputeCorpusSelection();
		OnPropertyChanged("CorpusFilterSummary");
		_resultsView?.Refresh();
		RefreshVisibleStatus();
	}

	private void OnSortingChanged()
	{
		if (_resultsView is ListCollectionView listCollectionView)
		{
			listCollectionView.CustomSort = LibraryViewModel.ResultSortComparer(Sorting.Layers);
		}
	}

	public async Task EnsureCategoriesLoadedAsync()
	{
		if (_categoriesInitialized)
		{
			return;
		}
		_categoriesInitialized = true;
		try
		{
			IReadOnlyList<string> all = await _catalogRepo.GetDistinctCategoriesAsync();
			Topics.Initialize(all);
		}
		catch
		{
		}
	}

	public SearchViewModel(SearchOrchestrator orchestrator, IPathResolver paths, SearchHistoryStore history, SearchResultsCacheStore resultsCache, JsonSettingsStore settings, RasheyTevotMap rasheyTevotMap, HebAramMap hebAramMap, ICatalogRepository catalogRepo, MainViewModel main, ISearchScopeContext scope, SynonymLookup synonyms, UsageTelemetryService usage, PerformanceAdvisor perfAdvisor)
	{
		_scope = scope;
		_orchestrator = orchestrator;
		_usage = usage;
		_perfAdvisor = perfAdvisor;
		_paths = paths;
		_history = history;
		_resultsCache = resultsCache;
		_settings = settings;
		_synonyms = synonyms;
		_rasheyTevotMap = rasheyTevotMap;
		_hebAramMap = hebAramMap;
		_catalogRepo = catalogRepo;
		_main = main;
		_navigation.StateChanged += delegate
		{
			OnPropertyChanged("CanGoBack");
			OnPropertyChanged("CanGoForward");
			GoBackCommand.NotifyCanExecuteChanged();
			GoForwardCommand.NotifyCanExecuteChanged();
		};
		RowDetailsMode = (_settings.Load().View.ShowRowDetails ? DataGridRowDetailsVisibilityMode.VisibleWhenSelected : DataGridRowDetailsVisibilityMode.Collapsed);
		SettingsViewModel.ShowRowDetailsChanged += OnShowRowDetailsChanged;
		SettingsViewModel.SearchOptionsChanged += OnSharedSearchOptionsChanged;
		_chipsEnabled = _settings.Load().View.EnableSynonymChips;
		SettingsViewModel.SynonymChipsEnabledChanged += OnSynonymChipsEnabledChanged;
		Topics.Changed += delegate
		{
			_resultsView?.Refresh();
			RefreshVisibleStatus();
		};
		Years.Changed += delegate
		{
			_resultsView?.Refresh();
			RefreshVisibleStatus();
		};
		_resultsView = CollectionViewSource.GetDefaultView(Results);
		_resultsView.Filter = MatchesFilter;
		CorpusFilters = new ObservableCollection<CorpusFilterOption>
		{
			new CorpusFilterOption("All", SharedStrings.S741, isSelected: true),
			new CorpusFilterOption("PDF", "HebrewBooks", isSelected: true),
			new CorpusFilterOption("Text", SharedStrings.S568, isSelected: true),
			new CorpusFilterOption("Personal", SharedStrings.S55, isSelected: true)
		};
		foreach (CorpusFilterOption corpusFilter in CorpusFilters)
		{
			corpusFilter.PropertyChanged += OnCorpusOptionChanged;
		}
		RecomputeCorpusSelection();
		SelectedScope = ScopeOptions[0];
		Sorting.Changed += OnSortingChanged;
		LoadSettings();
		ReloadHistory();
	}

	[RelayCommand]
	private async Task SearchTypedExactAsync()
	{
		if (!HasSpellCorrection)
		{
			return;
		}
		QueryText = SpellCorrectedFrom;
		_skipSpellCorrection = true;
		try
		{
			await SearchAsync();
		}
		finally
		{
			_skipSpellCorrection = false;
		}
	}

	private void OnSynonymChipsEnabledChanged(bool enabled)
	{
		_chipsEnabled = enabled;
		if (!enabled && SynonymGroups.Count > 0)
		{
			ClearSynonymChips();
		}
	}

	private void ClearSynonymChips()
	{
		foreach (SynonymChipVm allChip in AllChips)
		{
			allChip.PropertyChanged -= OnChipSelectionChanged;
		}
		SynonymGroups.Clear();
		OnPropertyChanged("HasSynonymChips");
		OnPropertyChanged("ShowChipSelector");
		OnPropertyChanged("HasSelectedChips");
		OnPropertyChanged("SearchWithSelectedLabel");
	}

	private void RefreshSynonymChips(string rawQuery)
	{
		foreach (SynonymChipVm allChip in AllChips)
		{
			allChip.PropertyChanged -= OnChipSelectionChanged;
		}
		SynonymGroups.Clear();
		if (_chipsEnabled)
		{
			foreach (SynonymGroup item in _synonyms.LookupGrouped(rawQuery))
			{
				IEnumerable<SynonymChipVm> chips = item.Chips.Select(delegate(string term)
				{
					SynonymChipVm synonymChipVm = new SynonymChipVm(term);
					synonymChipVm.PropertyChanged += OnChipSelectionChanged;
					return synonymChipVm;
				});
				SynonymGroups.Add(new SynonymChipGroup(item.Source, chips));
			}
		}
		OnPropertyChanged("HasSynonymChips");
		OnPropertyChanged("ShowChipSelector");
		OnPropertyChanged("HasSelectedChips");
		OnPropertyChanged("SearchWithSelectedLabel");
	}

	private void OnChipSelectionChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (!(e.PropertyName != "IsSelected"))
		{
			if (!_revertingChip && sender is SynonymChipVm { IsSelected: not false } synonymChipVm && AllChips.Count((SynonymChipVm c) => c.IsSelected) > 6)
			{
				_revertingChip = true;
				synonymChipVm.IsSelected = false;
				_revertingChip = false;
				StatusText = $"{SharedStrings.S2268}{6}{SharedStrings.S2269}";
			}
			else
			{
				OnPropertyChanged("HasSelectedChips");
				OnPropertyChanged("SearchWithSelectedLabel");
			}
		}
	}

	private IReadOnlyDictionary<string, IReadOnlyList<string>> SelectedSynonymsBySource()
	{
		Dictionary<string, IReadOnlyList<string>> dictionary = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
		foreach (SynonymChipGroup synonymGroup in SynonymGroups)
		{
			List<string> list = (from c in synonymGroup.Chips
				where c.IsSelected
				select c.Term).ToList();
			if (list.Count > 0)
			{
				dictionary[synonymGroup.Source] = list;
			}
		}
		return dictionary;
	}

	private static string BuildWithSynonyms(string query, IReadOnlyDictionary<string, IReadOnlyList<string>> selBySource, QueryBuildOptions opts)
	{
		return SynonymQueryBuilder.Build(query, selBySource, opts);
	}

	[RelayCommand]
	private void SearchWithSelectedChips()
	{
		if (HasSelectedChips && SearchCommand.CanExecute(null))
		{
			SearchCommand.Execute(null);
		}
	}

	private void LoadSettings()
	{
		SearchOptions search = _settings.Load().Search;
		_settingsLoaded = false;
		try
		{
			Hybur = search.Hybur;
			RasheyTevot = search.RasheyTevot;
			RootSearch = search.RootSearch;
			ExpandNumberGender = search.ExpandNumberGender;
			ExpandGematria = search.ExpandGematria;
			ExpandSpelling = search.ExpandSpelling;
			ExpandAramaic = search.ExpandAramaic;
			ExpandRashiOcr = search.ExpandRashiOcr;
			ExpandWeakLetters = search.ExpandWeakLetters;
			MaxProximity = search.MaxProximity;
			Fuzziness = Math.Clamp(search.Fuzziness, 0, 10);
			RequireWordOrder = search.RequireWordOrder;
		}
		finally
		{
			_settingsLoaded = true;
		}
	}

	private void SaveSettings()
	{
		if (!_settingsLoaded)
		{
			return;
		}
		try
		{
			_settings.Update(delegate(BookshelfOptions o)
			{
				o.Search.Hybur = Hybur;
				o.Search.RasheyTevot = RasheyTevot;
				o.Search.RootSearch = RootSearch;
				o.Search.ExpandNumberGender = ExpandNumberGender;
				o.Search.ExpandGematria = ExpandGematria;
				o.Search.ExpandSpelling = ExpandSpelling;
				o.Search.ExpandAramaic = ExpandAramaic;
				o.Search.ExpandRashiOcr = ExpandRashiOcr;
				o.Search.ExpandWeakLetters = ExpandWeakLetters;
				o.Search.MaxProximity = MaxProximity;
				o.Search.Fuzziness = Math.Clamp(Fuzziness, 0, 10);
				o.Search.RequireWordOrder = RequireWordOrder;
			});
			SettingsViewModel.RaiseSearchOptionsChanged();
		}
		catch
		{
		}
	}

	private void OnSharedSearchOptionsChanged()
	{
		LoadSettings();
	}

	public void RefreshSearchOptionsFromDisk()
	{
		LoadSettings();
	}

	private void ReloadHistory()
	{
		SearchHistory.Clear();
		foreach (string item in _history.Recent)
		{
			SearchHistory.Add(item);
		}
	}

	[RelayCommand]
	private void ClearHistory()
	{
		_history.Clear();
		SearchHistory.Clear();
		_resultsCache.Clear();
	}

	[RelayCommand]
	private async Task UseHistoryEntry(string? entry)
	{
		if (!string.IsNullOrWhiteSpace(entry))
		{
			QueryText = entry;
			await SearchAsync();
		}
	}

	private bool MatchesFilter(object? obj)
	{
		if (!(obj is SearchResultRow searchResultRow))
		{
			return false;
		}
		if (Topics.SelectedSet.Count > 0 && !CategoryFilter.MatchesAny(searchResultRow.Book, Topics.SelectedSet))
		{
			return false;
		}
		if (_selectedCorpora.Count < _individualCorpusCount)
		{
			string item = (string.IsNullOrEmpty(searchResultRow.Book.SourceType) ? "PDF" : searchResultRow.Book.SourceType);
			if (!_selectedCorpora.Contains(item))
			{
				return false;
			}
		}
		if (!Years.Range.Matches(searchResultRow.Book.PrintYear))
		{
			return false;
		}
		string value = ResultFilterText?.Trim();
		if (string.IsNullOrEmpty(value))
		{
			return true;
		}
		string? bookName = searchResultRow.Book.BookName;
		if (bookName == null || !bookName.Contains(value, StringComparison.OrdinalIgnoreCase))
		{
			return searchResultRow.Book.AuthorName?.Contains(value, StringComparison.OrdinalIgnoreCase) ?? false;
		}
		return true;
	}

	private void RefreshVisibleStatus()
	{
		if (_resultsView != null && !IsBusy && Results.Count != 0)
		{
			int num = _resultsView.Cast<object>().Count();
			StatusText = ((num == Results.Count) ? string.Format(SharedStrings.StatusFoundResults, Results.Count) : $"{SharedStrings.S2270}{num}{SharedStrings.S2271}{Results.Count}");
		}
	}

	private async void DebouncedOpen()
	{
		_ = 1;
		try
		{
			_openDebounce?.Cancel();
			_openDebounce = new CancellationTokenSource();
			CancellationToken ct = _openDebounce.Token;
			try
			{
				await Task.Delay(50, ct);
			}
			catch (OperationCanceledException)
			{
				return;
			}
			if (!ct.IsCancellationRequested)
			{
				await OpenSelectedAsync(ct);
			}
		}
		catch (Exception ex2)
		{
			StatusText = SharedStrings.S2272 + ex2.Message;
		}
	}

	[RelayCommand(CanExecute = "CanSearch")]
	private async Task SearchAsync()
	{
		if (string.IsNullOrWhiteSpace(QueryText))
		{
			return;
		}
		SpellCorrectedFrom = string.Empty;
		if (!_skipSpellCorrection && HebrewKeyboard.LooksMistyped(QueryText))
		{
			string text = QueryText.Trim();
			QueryText = HebrewKeyboard.ToHebrew(text);
			SpellCorrectedFrom = text;
		}
		ReportAbandonedSearch();
		_searchCts?.Cancel();
		CancellationTokenSource myCts = (_searchCts = new CancellationTokenSource());
		CancellationToken ct = myCts.Token;
		Stopwatch stopwatch = Stopwatch.StartNew();
		_searchStartedTicks = Environment.TickCount64;
		_wasCancelled = false;
		try
		{
			IsBusy = true;
			StatusText = SharedStrings.StatusSearching;
			ResultFilterText = string.Empty;
			Results.Clear();
			CurrentResultIndex = -1;
			UpdateResultPosition();
			List<string> list = (from c in AllChips
				where c.IsSelected
				select c.Term).ToList();
			ActiveChipsSummary = ((list.Count > 0) ? string.Join("  ·  ", list) : string.Empty);
			int maxFiles = Math.Max(1, _settings.Load().Search.MaxFilesToRetrieve);
			_scopeFallbackHint = null;
			IProgress<SearchResultRow> progress = null;
			Progress<int> liveHitCount = new Progress<int>(delegate(int n)
			{
				if (_searchCts == myCts)
				{
					StatusText = string.Format(SharedStrings.StatusFoundResults, n);
				}
			});
			IReadOnlyList<string> restrictFileIds = null;
			switch (SelectedScope?.Scope)
			{
			case SearchScope.Displayed:
			{
				IReadOnlyCollection<string> displayedFileIds = _scope.DisplayedFileIds;
				if (displayedFileIds.Count > 0)
				{
					restrictFileIds = displayedFileIds.ToList();
				}
				else
				{
					_scopeFallbackHint = SharedStrings.S9077;
				}
				break;
			}
			case SearchScope.Marked:
			{
				IReadOnlyCollection<string> markedFileIds = _scope.MarkedFileIds;
				if (markedFileIds.Count > 0)
				{
					restrictFileIds = markedFileIds.ToList();
				}
				else
				{
					_scopeFallbackHint = SharedStrings.S9078;
				}
				break;
			}
			}
			string onlineUrl = OnlineServiceUrl();
			string remoteUrl = ((onlineUrl == null) ? RemoteSearchUrl() : null);
			IReadOnlyList<SearchResultRow> rows;
			bool fuzzyOn;
			QueryBuildOptions qbOpts;
			IReadOnlyDictionary<string, IReadOnlyList<string>> selSyns;
			string t;
			int num;
			string rfp;
			if (onlineUrl != null)
			{
				IReadOnlyDictionary<string, IReadOnlyList<string>> bySource = SelectedSynonymsBySource();
				ActiveQueryText = QueryText.Trim();
				HashSet<string> corporaSelected = ((_selectedCorpora.Count > 0 && _selectedCorpora.Count < _individualCorpusCount) ? _selectedCorpora : null);
				WebApiClient.Options o = new WebApiClient.Options(Math.Max(1, MaxProximity), Hybur, RootSearch, ExpandGematria, ExpandSpelling, ExpandNumberGender, ExpandAramaic, RasheyTevot, RequireWordOrder, ExpandRashiOcr, Math.Clamp(Fuzziness, 0, 10), maxFiles, MapCorpusForApi(corporaSelected), "hitCount", restrictFileIds, FlattenSynonyms(bySource));
				WebApiClient.SearchOutcome searchOutcome = await _webApi.SearchAsync(onlineUrl, QueryText.Trim(), o, progress, ct);
				rows = searchOutcome.Rows;
				if (rows.Count > 0 && !_skipSpellCorrection && !string.IsNullOrWhiteSpace(searchOutcome.CorrectedQuery) && !string.Equals(searchOutcome.CorrectedQuery, searchOutcome.OriginalQuery ?? QueryText.Trim(), StringComparison.Ordinal))
				{
					SpellCorrectedFrom = searchOutcome.OriginalQuery ?? QueryText.Trim();
					QueryText = searchOutcome.CorrectedQuery;
					ActiveQueryText = searchOutcome.CorrectedQuery;
				}
			}
			else
			{
				if (remoteUrl == null)
				{
					await _orchestrator.EnsureIndexOpenAsync(ct);
					fuzzyOn = Math.Clamp(Fuzziness, 0, 10) > 0;
					bool listExpandsOn = RootSearch || ExpandSpelling || ExpandGematria || ExpandNumberGender || ExpandAramaic || RasheyTevot || ExpandRashiOcr || ExpandWeakLetters;
					qbOpts = new QueryBuildOptions(Math.Max(1, MaxProximity), Hybur, FirstWordOnly: false, LastWordOnly: false, (!fuzzyOn && RasheyTevot) ? _rasheyTevotMap : null, !fuzzyOn && RootSearch, !fuzzyOn && ExpandNumberGender, !fuzzyOn && ExpandGematria, !fuzzyOn && ExpandSpelling, (!fuzzyOn && ExpandAramaic) ? _hebAramMap : null, _orchestrator.Engine.FilterIndexedWords, 200, RequireWordOrder: RequireWordOrder, ExpandRashiOcr: !fuzzyOn && ExpandRashiOcr, ExpandWeakLetters: !fuzzyOn && ExpandWeakLetters);
					string rawQuery = QueryText.Trim();
					selSyns = SelectedSynonymsBySource();
					t = (ActiveQueryText = await Task.Run(() => BuildWithSynonyms(rawQuery, selSyns, qbOpts), ct));
					if (fuzzyOn && listExpandsOn)
					{
						_scopeFallbackHint += SharedStrings.S9073;
					}
					if (restrictFileIds != null && restrictFileIds.Count > 0)
					{
						IReadOnlyList<string> readOnlyList = restrictFileIds;
						if (readOnlyList.Count <= 500)
						{
							num = (readOnlyList.All(IsPdfStem) ? 1 : 0);
							goto IL_0a9d;
						}
					}
					num = 0;
					goto IL_0a9d;
				}
				IReadOnlyDictionary<string, IReadOnlyList<string>> readOnlyDictionary = SelectedSynonymsBySource();
				string text3 = QueryText.Trim();
				if (readOnlyDictionary.Count > 0)
				{
					bool flag = Math.Clamp(Fuzziness, 0, 10) > 0;
					text3 = BuildWithSynonyms(QueryText.Trim(), readOnlyDictionary, new QueryBuildOptions(Math.Max(1, MaxProximity), Hybur, FirstWordOnly: false, LastWordOnly: false, (!flag && RasheyTevot) ? _rasheyTevotMap : null, !flag && RootSearch, !flag && ExpandNumberGender, !flag && ExpandGematria, !flag && ExpandSpelling, (!flag && ExpandAramaic) ? _hebAramMap : null, null, 200, RequireWordOrder: RequireWordOrder, ExpandRashiOcr: !flag && ExpandRashiOcr));
				}
				ActiveQueryText = text3;
				HashSet<string> hashSet = ((_selectedCorpora.Count > 0 && _selectedCorpora.Count < _individualCorpusCount) ? _selectedCorpora : null);
				RemoteSearchClient.Options options = new RemoteSearchClient.Options(Math.Max(1, MaxProximity), Hybur, RootSearch, ExpandGematria, ExpandSpelling, ExpandNumberGender, ExpandAramaic, RasheyTevot, RequireWordOrder, ExpandRashiOcr, Math.Clamp(Fuzziness, 0, 10), maxFiles, (hashSet == null) ? null : MapCorporaForService(hashSet), restrictFileIds);
				rfp = SearchResultsCacheStore.RemoteFingerprint(remoteUrl, text3, options.Proximity, options.Hybur, options.Roots, options.Gematria, options.Spelling, options.NumberGender, options.Aramaic, options.RasheyTevot, options.RequireWordOrder, options.RashiOcr, options.Fuzziness, options.MaxFiles, options.Corpora, options.RestrictFileIds);
				IReadOnlyList<SearchResultRow> readOnlyList2 = _resultsCache.TryLoadRows(rfp);
				if (readOnlyList2 != null)
				{
					rows = readOnlyList2;
				}
				else
				{
					rows = await _remote.SearchAsync(remoteUrl, text3, options, progress, ct);
					if (_searchCts == myCts)
					{
						_resultsCache.SaveRows(rfp, rows);
					}
				}
			}
			goto IL_1027;
			IL_0a9d:
			bool canAccelerate = (byte)num != 0;
			string text4 = ApplyScope(t);
			if (restrictFileIds != null && restrictFileIds.Count > 0 && !canAccelerate)
			{
				_scopeFallbackHint = SharedStrings.S9079;
			}
			IReadOnlyList<string> restrictToIndexPaths = null;
			if (_selectedCorpora.Count > 0 && _selectedCorpora.Count < _individualCorpusCount)
			{
				List<string> list2 = new List<string>();
				if (_selectedCorpora.Contains("PDF"))
				{
					list2.Add(_paths.IndexesRoot);
				}
				if (_selectedCorpora.Contains("Text"))
				{
					list2.Add(_paths.OtzrayaIndexPath);
				}
				if (_selectedCorpora.Contains("Personal"))
				{
					list2.Add(_paths.PersonalIndexPath);
				}
				if (list2.Count > 0)
				{
					restrictToIndexPaths = list2;
				}
			}
			SearchQuery query = new SearchQuery(text4, MaxProximity, Hybur, IncludeNumbers: true, maxFiles, Math.Clamp(Fuzziness, 0, 10), restrictFileIds, restrictToIndexPaths, QueryBuilder.CountMatchWords(QueryText.Trim()));
			rfp = SearchResultsCacheStore.Fingerprint(query);
			long idxStamp = CurrentIndexStamp();
			IReadOnlyList<SearchHit> readOnlyList3 = _resultsCache.TryLoad(rfp, idxStamp);
			if (readOnlyList3 != null)
			{
				rows = await _orchestrator.RehydrateAsync(readOnlyList3, ct);
			}
			else
			{
				rows = await _orchestrator.RunAsync(query, SortMode.HitCount, progress, ct, liveHitCount);
				if (_searchCts == myCts)
				{
					_resultsCache.Save(rfp, idxStamp, rows.Select((SearchResultRow r) => new SearchHit(r.Book.FileID ?? string.Empty, r.HitCount, r.Location, r.PageNumber)).ToList());
				}
			}
			if (rows.Count == 0 && !fuzzyOn && !_skipSpellCorrection && !ct.IsCancellationRequested)
			{
				SpellSuggestionService.Correction correction = await SpellSuggestionService.SuggestAsync(_orchestrator.Engine, QueryText.Trim(), SpellSuggestBudget, ct);
				if ((object)correction != null)
				{
					string corrected = correction.Corrected;
					string fixedText = await Task.Run(() => BuildWithSynonyms(corrected, selSyns, qbOpts), ct);
					if (!string.IsNullOrEmpty(fixedText))
					{
						IReadOnlyList<SearchResultRow> readOnlyList4 = await _orchestrator.RunAsync(query with
						{
							Text = ApplyScope(fixedText),
							HitCountDivisor = QueryBuilder.CountMatchWords(correction.Corrected)
						}, SortMode.HitCount, progress, ct, liveHitCount);
						if (readOnlyList4.Count > 0)
						{
							rows = readOnlyList4;
							SpellCorrectedFrom = QueryText.Trim();
							QueryText = correction.Corrected;
							ActiveQueryText = fixedText;
						}
					}
				}
			}
			goto IL_1027;
			IL_1027:
			long elapsedMilliseconds = stopwatch.ElapsedMilliseconds;
			if (_searchCts != myCts)
			{
				return;
			}
			SearchResultRow selectedRow = SelectedRow;
			Results.ReplaceAll(rows);
			int num2 = (((object)selectedRow == null) ? (-1) : Results.IndexOf(selectedRow));
			CurrentResultIndex = ((num2 >= 0) ? num2 : ((rows.Count <= 0) ? (-1) : 0));
			if (CurrentResultIndex >= 0)
			{
				_autoSelectingResult = true;
				try
				{
					SelectedRow = Results[CurrentResultIndex];
				}
				finally
				{
					_autoSelectingResult = false;
				}
			}
			UpdateResultPosition();
			Log.Information("TIMING: Search[Split] engine={EngineMs}ms populate={PopulateMs}ms total={TotalMs}ms rows={Rows}", elapsedMilliseconds, stopwatch.ElapsedMilliseconds - elapsedMilliseconds, stopwatch.ElapsedMilliseconds, rows.Count);
			_perfAdvisor.ReportOperation(SlowStage.Search, elapsedMilliseconds);
			HashSet<string> hashSet2 = new HashSet<string>(StringComparer.Ordinal);
			foreach (SearchResultRow result in Results)
			{
				hashSet2.Add(string.IsNullOrEmpty(result.Book.SourceType) ? "PDF" : result.Book.SourceType);
			}
			HasMultipleSources = hashSet2.Count > 1;
			stopwatch.Stop();
			StatusText = ComposeFinalStatus(rows.Count, stopwatch.Elapsed, _wasCancelled);
			if (_scopeFallbackHint != null)
			{
				StatusText += _scopeFallbackHint;
			}
			StatusText = StatusText + LowProximityHint(QueryText, rows.Count) + ((onlineUrl == null && remoteUrl == null) ? IndexNotReadyHint() : string.Empty);
			if (!_resultsShown)
			{
				_resultsShown = true;
				OnPropertyChanged("ShowChipSelector");
			}
			_history.Add(QueryText.Trim());
			ReloadHistory();
			RecordNavIfNotSuppressed();
			string ApplyScope(string text5)
			{
				if (canAccelerate)
				{
					return $"({text5}) and ({string.Join(" or ", restrictFileIds.Select((string id) => "xfilter(name \"" + id + ".pdf\")"))})";
				}
				return text5;
			}
		}
		catch (OperationCanceledException)
		{
			if (_searchCts == myCts)
			{
				stopwatch.Stop();
				StatusText = ComposeFinalStatus(Results.Count, stopwatch.Elapsed, cancelled: true);
				_history.Add(QueryText.Trim());
				ReloadHistory();
				RecordNavIfNotSuppressed();
			}
		}
		catch (Exception ex2)
		{
			if (_searchCts == myCts)
			{
				StatusText = ex2.Message;
			}
		}
		finally
		{
			if (_searchCts == myCts)
			{
				IsBusy = false;
				IsStopping = false;
				_searchStartedTicks = 0L;
			}
		}
	}

	private string? RemoteSearchUrl()
	{
		return _settings.Load().EffectiveSearchServiceUrl();
	}

	private string? OnlineServiceUrl()
	{
		return _settings.Load().EffectiveOnlineServiceUrl();
	}

	private static string MapCorpusForApi(IReadOnlySet<string>? corporaSelected)
	{
		if (corporaSelected == null || corporaSelected.Count != 1)
		{
			return "all";
		}
		if (corporaSelected.Contains("PDF"))
		{
			return "pdf";
		}
		if (corporaSelected.Contains("Text"))
		{
			return "otzraya";
		}
		return "all";
	}

	private static IReadOnlyCollection<KeyValuePair<string, string>>? FlattenSynonyms(IReadOnlyDictionary<string, IReadOnlyList<string>>? bySource)
	{
		if (bySource == null || bySource.Count == 0)
		{
			return null;
		}
		List<KeyValuePair<string, string>> list = new List<KeyValuePair<string, string>>();
		foreach (KeyValuePair<string, IReadOnlyList<string>> item in bySource)
		{
			foreach (string item2 in item.Value)
			{
				list.Add(new KeyValuePair<string, string>(item.Key, item2));
			}
		}
		if (list.Count <= 0)
		{
			return null;
		}
		return list;
	}

	private long CurrentIndexStamp()
	{
		long num = 0L;
		string[] array = new string[3] { _paths.IndexesRoot, _paths.OtzrayaIndexPath, _paths.PersonalIndexPath };
		foreach (string text in array)
		{
			try
			{
				if (!string.IsNullOrEmpty(text) && Directory.Exists(text))
				{
					num = Math.Max(num, Directory.GetLastWriteTimeUtc(text).Ticks);
				}
			}
			catch
			{
			}
		}
		return num;
	}

	private RemoteSearchClient.InBookOptions InBookOpts()
	{
		return new RemoteSearchClient.InBookOptions(Hybur, RootSearch, ExpandGematria, ExpandSpelling, ExpandNumberGender, ExpandAramaic, RasheyTevot, RequireWordOrder, ExpandRashiOcr, Math.Clamp(Fuzziness, 0, 10));
	}

	private WebApiClient.InBookOptions WebInBookOpts()
	{
		return new WebApiClient.InBookOptions(Hybur, RootSearch, ExpandGematria, ExpandSpelling, ExpandNumberGender, ExpandAramaic, RasheyTevot, RequireWordOrder, ExpandRashiOcr, Math.Clamp(Fuzziness, 0, 10), Math.Max(1, MaxProximity));
	}

	private static IReadOnlyCollection<string> MapCorporaForService(IEnumerable<string> corpora)
	{
		List<string> list = new List<string>();
		using IEnumerator<string> enumerator = corpora.GetEnumerator();
		while (enumerator.MoveNext())
		{
			switch (enumerator.Current)
			{
			case "PDF":
				list.Add("pdf");
				break;
			case "Text":
				list.Add("otzraya");
				break;
			case "Personal":
				list.Add("personal");
				break;
			}
		}
		return list;
	}

	private bool CanSearch()
	{
		return true;
	}

	private NavigationEntry SnapshotCurrent()
	{
		return new NavigationEntry(SelectedRow?.Book?.FileID, SelectedRow?.Book?.SourceType, SelectedRow?.Book?.RelativePath, PdfPage, QueryText ?? string.Empty, IsContentMode: true, (Results.Count > 0) ? new List<SearchResultRow>(Results) : null, SelectedRow?.Book?.FileID, StatusText);
	}

	private void RecordNavIfNotSuppressed()
	{
		if (_suppressNavigationRecord)
		{
			return;
		}
		_navigation.RecordNavigation(SnapshotCurrent());
		if (Results.Count <= 1000)
		{
			NavigationEntry[]? back = _navigation.Snapshot().Back;
			if (back == null || back.Length <= 100)
			{
				return;
			}
		}
		int count = Results.Count;
		NavigationEntry[]? back2 = _navigation.Snapshot().Back;
		Log.Information("Nav[Search]: pushed entry results={ResultsCount} backDepth={Depth}", count, (back2 != null) ? back2.Length : 0);
	}

	private void ApplyEntry(NavigationEntry? entry)
	{
		if ((object)entry == null)
		{
			return;
		}
		_suppressNavigationRecord = true;
		try
		{
			QueryText = entry.FilterText;
			RangeObservableCollection<SearchResultRow> results = Results;
			IEnumerable<SearchResultRow> contentResults = entry.ContentResults;
			results.ReplaceAll(contentResults ?? Enumerable.Empty<SearchResultRow>());
			_displayedFileId = null;
			_displayedQueryAtOpen = null;
			if (!string.IsNullOrEmpty(entry.SelectedResultFileId))
			{
				SearchResultRow searchResultRow = (SelectedRow = Results.FirstOrDefault((SearchResultRow r) => string.Equals(r.Book?.FileID, entry.SelectedResultFileId, StringComparison.Ordinal)));
				CurrentResultIndex = (((object)searchResultRow == null) ? (-1) : Results.IndexOf(searchResultRow));
			}
			else
			{
				SelectedRow = null;
				CurrentResultIndex = -1;
			}
			UpdateResultPosition();
			StatusText = entry.StatusText ?? string.Empty;
			if (entry.Page > 0)
			{
				PdfPage = entry.Page;
			}
		}
		finally
		{
			_suppressNavigationRecord = false;
		}
	}

	[RelayCommand(CanExecute = "CanGoBack")]
	private void GoBack()
	{
		ApplyEntry(_navigation.GoBack());
	}

	[RelayCommand(CanExecute = "CanGoForward")]
	private void GoForward()
	{
		ApplyEntry(_navigation.GoForward());
	}

	private static bool IsPdfStem(string fileId)
	{
		if (fileId.Length > 0)
		{
			return fileId.All(char.IsAsciiDigit);
		}
		return false;
	}

	[RelayCommand(CanExecute = "CanCancelSearch")]
	private void CancelSearch()
	{
		if (IsBusy)
		{
			ReportAbandonedSearch();
			_wasCancelled = true;
			_searchCts?.Cancel();
			_searchCts = null;
			IsStopping = false;
			IsBusy = false;
			StatusText = SharedStrings.S768;
		}
	}

	private bool CanCancelSearch()
	{
		if (IsBusy)
		{
			return !IsStopping;
		}
		return false;
	}

	private void ReportAbandonedSearch()
	{
		if (IsBusy && _searchStartedTicks != 0L)
		{
			long elapsedMs = Environment.TickCount64 - _searchStartedTicks;
			_searchStartedTicks = 0L;
			_perfAdvisor.ReportOperation(SlowStage.Search, elapsedMs);
		}
	}

	private string LowProximityHint(string? rawQuery, int resultCount)
	{
		if (resultCount > 0 || MaxProximity > 5)
		{
			return string.Empty;
		}
		if ((rawQuery ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length < 2)
		{
			return string.Empty;
		}
		return SharedStrings.S9074;
	}

	private string IndexNotReadyHint()
	{
		List<string> notes = new List<string>();
		Check("Text", SharedStrings.S568, _paths.OtzrayaIndexPath);
		Check("Personal", SharedStrings.S55, _paths.PersonalIndexPath);
		if (notes.Count != 0)
		{
			return "  (" + string.Join("; ", notes) + ")";
		}
		return string.Empty;
		void Check(string corpus, string label, string indexPath)
		{
			if (_selectedCorpora.Contains(corpus))
			{
				int? num = _main.IndexBuildPercentFor(indexPath);
				if (num.HasValue)
				{
					int valueOrDefault = num.GetValueOrDefault();
					notes.Add($"{SharedStrings.S2273}{label}{SharedStrings.S2274}{valueOrDefault}{SharedStrings.S2275}");
				}
				else if (!SearchOrchestrator.IsIndexBuilt(indexPath))
				{
					notes.Add(SharedStrings.S2276 + label + SharedStrings.S2277);
				}
			}
		}
	}

	private static string ComposeFinalStatus(int count, TimeSpan elapsed, bool cancelled)
	{
		double totalSeconds = elapsed.TotalSeconds;
		string text = ((totalSeconds < 1.0) ? $"({elapsed.TotalMilliseconds:F0}{SharedStrings.S2278}" : $"({totalSeconds:F1}{SharedStrings.S2279}");
		if (cancelled)
		{
			if (count != 0)
			{
				return $"{SharedStrings.S2281}{count}{SharedStrings.S2282}{text}";
			}
			return SharedStrings.S2280 + text;
		}
		if (count == 0)
		{
			return SharedStrings.StatusNoResults + " " + text;
		}
		return string.Format(SharedStrings.StatusFoundResults, count) + " " + text;
	}

	private async Task OpenSelectedAsync(CancellationToken ct)
	{
		if ((object)SelectedRow == null)
		{
			PdfPath = null;
			CurrentBookHits = null;
			MarkedPdfPath = null;
			CurrentTextRelativePath = null;
			IsTextMode = false;
			InBookSearchStatus = string.Empty;
			InBookSearchText = string.Empty;
			_main.SetOpenBookTitle(null);
			_displayedFileId = null;
			_displayedQueryAtOpen = null;
			return;
		}
		if (string.IsNullOrEmpty(SelectedRow.Book.FileID))
		{
			StatusText = SharedStrings.ErrorMissingFileId;
			return;
		}
		if (string.Equals(_displayedFileId, SelectedRow.Book.FileID, StringComparison.Ordinal) && string.Equals(_displayedQueryAtOpen, QueryText, StringComparison.Ordinal))
		{
			CurrentResultIndex = Results.IndexOf(SelectedRow);
			UpdateResultPosition();
			return;
		}
		InBookSearchStatus = string.Empty;
		InBookSearchText = string.Empty;
		_main.SetOpenBookTitle(SelectedRow.Book.BookName, QueryText);
		Stopwatch openSw = Stopwatch.StartNew();
		Log.Information("TIMING: OpenSelected start fileId={FileId} src={SourceType}", SelectedRow.Book.FileID, SelectedRow.Book.SourceType);
		if (string.Equals(SelectedRow.Book.SourceType, "Text", StringComparison.Ordinal))
		{
			Book book = SelectedRow.Book;
			if (string.IsNullOrEmpty(book.RelativePath))
			{
				StatusText = SharedStrings.S781;
				return;
			}
			string text = _paths.OtzrayaTextPath(book.RelativePath);
			if (!File.Exists(text))
			{
				StatusText = SharedStrings.S2283 + text;
				return;
			}
			CurrentTextTerms = QueryBuilder.ExtractHighlightTerms(QueryText, addPrefixes: false, expandRoots: false, expandNumberGender: false, expandGematria: false, expandSpelling: false, null, expandRashiOcr: false, dropPhraseConstituents: true);
			PdfPath = null;
			MarkedPdfPath = null;
			CurrentBookHits = null;
			IsBookLoading = false;
			IsTextMode = true;
			CurrentTextRelativePath = book.RelativePath;
			CurrentResultIndex = Results.IndexOf(SelectedRow);
			UpdateResultPosition();
			_displayedFileId = SelectedRow.Book.FileID;
			_displayedQueryAtOpen = QueryText;
			RecordNavIfNotSuppressed();
			return;
		}
		if (string.Equals(SelectedRow.Book.SourceType, "Personal", StringComparison.Ordinal))
		{
			Book book2 = SelectedRow.Book;
			string rel = ((!string.IsNullOrEmpty(book2.RelativePath)) ? book2.RelativePath : book2.FileID);
			string personalPath = _paths.PersonalFilePath(rel);
			if (!File.Exists(personalPath))
			{
				StatusText = SharedStrings.S2284 + personalPath;
				PdfPath = null;
				MarkedPdfPath = null;
				CurrentBookHits = null;
				return;
			}
			IsTextMode = false;
			CurrentTextRelativePath = null;
			MarkedPdfPath = null;
			_hitFetchCts?.Cancel();
			_hitFetchCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
			CancellationToken personalCt = _hitFetchCts.Token;
			IsBookLoading = true;
			string personalQuery = (string.IsNullOrEmpty(ActiveQueryText) ? QueryText.Trim() : ActiveQueryText);
			InBookHitInfo personalHits = null;
			if (!string.IsNullOrWhiteSpace(personalQuery))
			{
				try
				{
					Stopwatch idxSw = Stopwatch.StartNew();
					await _orchestrator.EnsureIndexOpenAsync(personalCt);
					Log.Information("TIMING: [Personal] EnsureIndexOpen done at +{Elapsed}ms (took {Step}ms)", openSw.ElapsedMilliseconds, idxSw.ElapsedMilliseconds);
					Stopwatch hitsSw = Stopwatch.StartNew();
					personalHits = await _orchestrator.GetInBookHitsCachedAsync(rel, personalQuery, QueryText, Hybur, RootSearch, ExpandNumberGender, ExpandGematria, ExpandSpelling, ExpandAramaic ? _hebAramMap : null, Math.Clamp(Fuzziness, 0, 10), ExpandWeakLetters, personalCt);
					Log.Information("TIMING: [Personal] InBookHits done at +{Elapsed}ms (took {Step}ms) hitCount={HitCount} pages={Pages} fileName={File}", openSw.ElapsedMilliseconds, hitsSw.ElapsedMilliseconds, personalHits?.HitCount, personalHits?.Pages?.Count, rel);
				}
				catch (OperationCanceledException)
				{
					IsBookLoading = false;
					return;
				}
				catch (Exception ex2)
				{
					StatusText = SharedStrings.S2285 + ex2.Message;
				}
			}
			if (personalCt.IsCancellationRequested)
			{
				IsBookLoading = false;
				return;
			}
			CurrentBookHits = personalHits;
			CurrentMatchIndex = (((personalHits?.Pages.Count ?? 0) <= 0) ? (-1) : 0);
			UpdateMatchPosition();
			PdfPage = (((personalHits?.Pages.Count ?? 0) <= 0) ? 1 : personalHits.Pages[0]);
			IsBookLoading = false;
			Log.Information("TIMING: [Personal] setting PdfPath at +{Elapsed}ms", openSw.ElapsedMilliseconds);
			PdfPath = personalPath;
			CurrentResultIndex = Results.IndexOf(SelectedRow);
			UpdateResultPosition();
			_displayedFileId = SelectedRow.Book.FileID;
			_displayedQueryAtOpen = QueryText;
			RecordNavIfNotSuppressed();
			return;
		}
		if (!int.TryParse(SelectedRow.Book.FileID, out var fileId))
		{
			StatusText = SharedStrings.ErrorMissingFileId;
			return;
		}
		IsTextMode = false;
		CurrentTextRelativePath = null;
		string path = _paths.PdfPath(fileId, SelectedRow.Book.Folder);
		if (!File.Exists(path))
		{
			Book bookToFetch = SelectedRow.Book;
			OnDemandBookService obj = (OnDemandBookService)App.Services.GetService(typeof(OnDemandBookService));
			Window owner = Application.Current?.MainWindow;
			bool flag = await obj.EnsureLocalAsync(bookToFetch, owner);
			if ((object)SelectedRow?.Book != bookToFetch)
			{
				return;
			}
			if (!flag || !File.Exists(path))
			{
				StatusText = SharedStrings.S2286 + path;
				PdfPath = null;
				CurrentBookHits = null;
				MarkedPdfPath = null;
				return;
			}
		}
		_hitFetchCts?.Cancel();
		_hitFetchCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
		CancellationToken fetchCt = _hitFetchCts.Token;
		string inBookQuery = (string.IsNullOrEmpty(ActiveQueryText) ? QueryText.Trim() : ActiveQueryText);
		string text2 = RemoteSearchUrl();
		string text3 = ((text2 == null) ? OnlineServiceUrl() : null);
		int num;
		if (text2 != null || text3 != null)
		{
			int? pageNumber = SelectedRow.PageNumber;
			if (pageNumber.HasValue)
			{
				int valueOrDefault = pageNumber.GetValueOrDefault();
				if (valueOrDefault > 0)
				{
					num = valueOrDefault;
					goto IL_0917;
				}
			}
			num = 1;
			goto IL_0917;
		}
		IsBookLoading = true;
		InBookHitInfo hits = null;
		if (!string.IsNullOrWhiteSpace(inBookQuery))
		{
			try
			{
				Stopwatch hitsSw = Stopwatch.StartNew();
				Stopwatch idxSw = Stopwatch.StartNew();
				await _orchestrator.EnsureIndexOpenAsync(fetchCt);
				Log.Information("TIMING: EnsureIndexOpen done at +{Elapsed}ms (took {Step}ms)", openSw.ElapsedMilliseconds, idxSw.ElapsedMilliseconds);
				hits = await _orchestrator.GetInBookHitsCachedAsync($"{fileId}.pdf", inBookQuery, QueryText, Hybur, RootSearch, ExpandNumberGender, ExpandGematria, ExpandSpelling, ExpandAramaic ? _hebAramMap : null, Math.Clamp(Fuzziness, 0, 10), ExpandWeakLetters, fetchCt);
				Log.Information("TIMING: InBookHits done at +{Elapsed}ms (took {Step}ms) hitCount={HitCount} pages={Pages}", openSw.ElapsedMilliseconds, hitsSw.ElapsedMilliseconds, hits?.HitCount, hits?.Pages?.Count);
			}
			catch (OperationCanceledException)
			{
				IsBookLoading = false;
				return;
			}
			catch (Exception ex4)
			{
				InBookSearchStatus = SharedStrings.S2287 + ex4.Message;
			}
		}
		if (fetchCt.IsCancellationRequested)
		{
			IsBookLoading = false;
			return;
		}
		string markedPath = null;
		if ((object)hits != null && hits.HitCount > 0)
		{
			try
			{
				markedPath = await _orchestrator.GetMarkedPdfPathCachedAsync($"{fileId}.pdf", inBookQuery, fetchCt);
			}
			catch (OperationCanceledException)
			{
				IsBookLoading = false;
				return;
			}
			catch (Exception ex6)
			{
				InBookSearchStatus = SharedStrings.S2288 + ex6.Message;
			}
		}
		if (fetchCt.IsCancellationRequested)
		{
			IsBookLoading = false;
			return;
		}
		MarkedPdfPath = markedPath;
		CurrentBookHits = hits;
		CurrentMatchIndex = (((hits?.Pages.Count ?? 0) <= 0) ? (-1) : 0);
		UpdateMatchPosition();
		PdfPage = (((hits?.Pages.Count ?? 0) <= 0) ? 1 : hits.Pages[0]);
		IsBookLoading = false;
		Log.Information("TIMING: setting PdfPath at +{Elapsed}ms (this triggers SearchPage.OpenSelectedAsync)", openSw.ElapsedMilliseconds);
		_perfAdvisor.ReportOperation(SlowStage.BookOpen, openSw.ElapsedMilliseconds);
		PdfPath = path;
		CurrentResultIndex = Results.IndexOf(SelectedRow);
		UpdateResultPosition();
		_displayedFileId = SelectedRow.Book.FileID;
		_displayedQueryAtOpen = QueryText;
		RecordNavIfNotSuppressed();
		return;
		IL_0917:
		int num2 = num;
		IReadOnlyList<string> readOnlyList;
		if (!string.IsNullOrWhiteSpace(inBookQuery))
		{
			readOnlyList = QueryBuilder.ExtractHighlightTerms(QueryText, addPrefixes: false, expandRoots: false, expandNumberGender: false, expandGematria: false, expandSpelling: false, null, expandRashiOcr: false, dropPhraseConstituents: true);
		}
		else
		{
			IReadOnlyList<string> readOnlyList2 = Array.Empty<string>();
			readOnlyList = readOnlyList2;
		}
		IReadOnlyList<string> matchedTerms = readOnlyList;
		string highlightXml = $"<loc pg=\"{Math.Max(0, num2 - 1)}\" pos=\"0\" len=\"1\"></loc>";
		MarkedPdfPath = null;
		CurrentBookHits = new InBookHitInfo(SelectedRow.HitCount, new int[1] { num2 }, matchedTerms, highlightXml);
		CurrentMatchIndex = 0;
		UpdateMatchPosition();
		PdfPage = num2;
		IsBookLoading = false;
		Log.Information("TIMING: [online] immediate open page={Page} at +{Elapsed}ms (hits fetched in background)", num2, openSw.ElapsedMilliseconds);
		PdfPath = path;
		CurrentResultIndex = Results.IndexOf(SelectedRow);
		UpdateResultPosition();
		_displayedFileId = SelectedRow.Book.FileID;
		_displayedQueryAtOpen = QueryText;
		RecordNavIfNotSuppressed();
		if (!string.IsNullOrWhiteSpace(inBookQuery))
		{
			FetchOnlineHitsInBackgroundAsync(text2, text3, fileId, inBookQuery, SelectedRow, fetchCt);
		}
	}

	private async Task FetchOnlineHitsInBackgroundAsync(string? remoteUrl, string? onlineUrl, int fileId, string inBookQuery, SearchResultRow rowAtOpen, CancellationToken ct)
	{
		_ = 1;
		try
		{
			InBookHitInfo inBookHitInfo = ((remoteUrl == null) ? (await _webApi.GetInBookHitsAsync(onlineUrl, fileId.ToString(CultureInfo.InvariantCulture), inBookQuery, WebInBookOpts(), ct)) : (await _remote.GetInBookHitsAsync(remoteUrl, $"{fileId}.pdf", inBookQuery, InBookOpts(), QueryText, ct)));
			if (ct.IsCancellationRequested || (object)SelectedRow != rowAtOpen || inBookHitInfo.HitCount == 0 || inBookHitInfo.Pages.Count == 0)
			{
				return;
			}
			int num = -1;
			for (int i = 0; i < inBookHitInfo.Pages.Count; i++)
			{
				if (inBookHitInfo.Pages[i] == PdfPage)
				{
					num = i;
					break;
				}
			}
			CurrentBookHits = inBookHitInfo;
			CurrentMatchIndex = ((num >= 0) ? num : 0);
			UpdateMatchPosition();
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception exception)
		{
			Log.Warning(exception, "Background in-book hit fetch failed for {FileId}", fileId);
		}
	}

	[RelayCommand]
	private void NextResult()
	{
		if (Results.Count != 0)
		{
			CurrentResultIndex = (CurrentResultIndex + 1) % Results.Count;
			SelectedRow = Results[CurrentResultIndex];
		}
	}

	[RelayCommand]
	private void PrevResult()
	{
		if (Results.Count != 0)
		{
			CurrentResultIndex = (CurrentResultIndex - 1 + Results.Count) % Results.Count;
			SelectedRow = Results[CurrentResultIndex];
		}
	}

	private void RequestScrollToPage(int page)
	{
		if (page > 0)
		{
			this.ScrollToPageRequested?.Invoke(page);
		}
	}

	[RelayCommand]
	private void NextMatch()
	{
		IReadOnlyList<int> readOnlyList = CurrentBookHits?.Pages;
		if (readOnlyList != null && readOnlyList.Count != 0)
		{
			CurrentMatchIndex = (CurrentMatchIndex + 1) % readOnlyList.Count;
			PdfPage = readOnlyList[CurrentMatchIndex];
			SelectedHitPage = PdfPage;
			UpdateMatchPosition();
			RequestScrollToPage(PdfPage);
		}
	}

	[RelayCommand]
	private void PrevMatch()
	{
		IReadOnlyList<int> readOnlyList = CurrentBookHits?.Pages;
		if (readOnlyList != null && readOnlyList.Count != 0)
		{
			CurrentMatchIndex = (CurrentMatchIndex - 1 + readOnlyList.Count) % readOnlyList.Count;
			PdfPage = readOnlyList[CurrentMatchIndex];
			SelectedHitPage = PdfPage;
			UpdateMatchPosition();
			RequestScrollToPage(PdfPage);
		}
	}

	private async void DebounceSynonymChips(string? text)
	{
		_chipCts?.Cancel();
		string content = (text ?? string.Empty).Trim();
		if (HebrewKeyboard.LooksMistyped(content))
		{
			content = HebrewKeyboard.ToHebrew(content);
		}
		if (!_chipsEnabled || content.Length == 0)
		{
			if (SynonymGroups.Count > 0)
			{
				ClearSynonymChips();
			}
			return;
		}
		CancellationTokenSource cts = (_chipCts = new CancellationTokenSource());
		try
		{
			await Task.Delay(350, cts.Token);
		}
		catch (OperationCanceledException)
		{
			return;
		}
		if (!cts.IsCancellationRequested)
		{
			RefreshSynonymChips(content);
		}
	}

	[RelayCommand]
	private async Task SearchInBookAsync()
	{
		if (string.IsNullOrWhiteSpace(InBookSearchText))
		{
			return;
		}
		if ((object)SelectedRow == null)
		{
			if (!string.IsNullOrEmpty(PdfPath) || !string.IsNullOrEmpty(_displayedFileId))
			{
				Log.Warning("SearchInBook: no selected row but a book is displayed (pdfPath={Path} displayedFileId={Fid}) — query '{Q}' skipped", PdfPath, _displayedFileId, InBookSearchText);
			}
			return;
		}
		Book book = SelectedRow.Book;
		if (IsTextMode)
		{
			InBookSearchStatus = SharedStrings.S886;
			return;
		}
		if (string.IsNullOrEmpty(book.FileID))
		{
			InBookSearchStatus = SharedStrings.ErrorMissingFileId;
			return;
		}
		string fileName;
		if (string.Equals(book.SourceType, "Personal", StringComparison.Ordinal))
		{
			string text = ((!string.IsNullOrEmpty(book.RelativePath)) ? book.RelativePath : book.FileID);
			fileName = text;
		}
		else
		{
			if (!int.TryParse(book.FileID, out var result))
			{
				InBookSearchStatus = SharedStrings.ErrorMissingFileId;
				return;
			}
			fileName = $"{result}.pdf";
		}
		IsBookLoading = true;
		try
		{
			string remoteUrl = RemoteSearchUrl();
			string inBookQuery = null;
			InBookHitInfo inBookHitInfo;
			if (remoteUrl != null)
			{
				inBookHitInfo = await _remote.GetInBookHitsAsync(remoteUrl, fileName, InBookSearchText.Trim(), InBookOpts());
			}
			else
			{
				string text2 = OnlineServiceUrl();
				if (text2 != null && !string.Equals(book.SourceType, "Personal", StringComparison.Ordinal) && int.TryParse(book.FileID, out var result2))
				{
					inBookHitInfo = await _webApi.GetInBookHitsAsync(text2, result2.ToString(CultureInfo.InvariantCulture), InBookSearchText.Trim(), WebInBookOpts());
				}
				else
				{
					await _orchestrator.EnsureIndexOpenAsync();
					inBookQuery = QueryBuilder.Build(InBookSearchText.Trim(), new QueryBuildOptions(Math.Max(1, MaxProximity), Hybur, FirstWordOnly: false, LastWordOnly: false, RasheyTevot ? _rasheyTevotMap : null, RootSearch, ExpandNumberGender, ExpandGematria, ExpandSpelling, ExpandAramaic ? _hebAramMap : null, _orchestrator.Engine.FilterIndexedWords, 200, RequireWordOrder: RequireWordOrder, ExpandRashiOcr: ExpandRashiOcr, ExpandWeakLetters: ExpandWeakLetters));
					inBookHitInfo = await _orchestrator.GetInBookHitsCachedAsync(fileName, inBookQuery, InBookSearchText, Hybur, RootSearch, ExpandNumberGender, ExpandGematria, ExpandSpelling, ExpandAramaic ? _hebAramMap : null, Math.Clamp(Fuzziness, 0, 10), ExpandWeakLetters);
				}
			}
			if (inBookHitInfo.HitCount == 0)
			{
				CurrentBookHits = null;
				CurrentMatchIndex = -1;
				UpdateMatchPosition();
				InBookSearchStatus = SharedStrings.S785;
				IsBookLoading = false;
				MarkedPdfPath = null;
				return;
			}
			int count = inBookHitInfo.Pages.Count;
			if (count == 0)
			{
				inBookHitInfo = inBookHitInfo with
				{
					Pages = new int[1] { 1 }
				};
			}
			PdfPage = inBookHitInfo.Pages[0];
			CurrentBookHits = inBookHitInfo;
			CurrentMatchIndex = 0;
			UpdateMatchPosition();
			_main.SetOpenBookTitle(book.BookName, InBookSearchText);
			InBookSearchStatus = ((count > 0) ? $"{SharedStrings.S2289}{inBookHitInfo.HitCount}{SharedStrings.S2290}{count}{SharedStrings.S2291}" : $"{SharedStrings.S2292}{inBookHitInfo.HitCount}{SharedStrings.S2293}");
			string markedPath = null;
			if (remoteUrl == null)
			{
				try
				{
					markedPath = await _orchestrator.GetMarkedPdfPathCachedAsync(fileName, inBookQuery);
				}
				catch (Exception ex)
				{
					InBookSearchStatus = SharedStrings.S2294 + ex.Message;
				}
			}
			IsBookLoading = false;
			MarkedPdfPath = markedPath;
		}
		catch (OperationCanceledException)
		{
			IsBookLoading = false;
		}
		catch (Exception ex3)
		{
			IsBookLoading = false;
			InBookSearchStatus = SharedStrings.S2295 + ex3.Message;
		}
	}

	public void ReportViewerPage(int page)
	{
		if (page <= 0)
		{
			return;
		}
		InBookHitInfo currentBookHits = CurrentBookHits;
		int? num = (((object)currentBookHits != null && currentBookHits.Pages.Contains(page)) ? new int?(page) : ((int?)null));
		if (SelectedHitPage == num)
		{
			return;
		}
		_syncingSelectionFromViewer = true;
		try
		{
			SelectedHitPage = num;
		}
		finally
		{
			_syncingSelectionFromViewer = false;
		}
	}

	[RelayCommand]
	private void GoToHitPage(int page)
	{
		if (page <= 0)
		{
			return;
		}
		IReadOnlyList<int> readOnlyList = CurrentBookHits?.Pages;
		if (readOnlyList != null)
		{
			for (int i = 0; i < readOnlyList.Count; i++)
			{
				if (readOnlyList[i] == page)
				{
					CurrentMatchIndex = i;
					break;
				}
			}
		}
		PdfPage = page;
		SelectedHitPage = page;
		UpdateMatchPosition();
		RequestScrollToPage(page);
	}

	public void ApplyHighlightProgress(int drawnSoFar)
	{
		InBookHitInfo currentBookHits = CurrentBookHits;
		int num = currentBookHits?.HitCount ?? 0;
		if (num != 0)
		{
			int count = currentBookHits.Pages.Count;
			if (drawnSoFar == 0 || drawnSoFar >= num)
			{
				InBookSearchStatus = ((count > 0) ? $"{SharedStrings.S2296}{num}{SharedStrings.S2297}{count}{SharedStrings.S2298}" : $"{SharedStrings.S2299}{num}{SharedStrings.S2300}");
			}
			else
			{
				InBookSearchStatus = ((count > 0) ? $"{SharedStrings.S2301}{drawnSoFar}{SharedStrings.S2302}{num}{SharedStrings.S2303}{count}{SharedStrings.S2304}" : $"{SharedStrings.S2305}{drawnSoFar}{SharedStrings.S2306}{num}{SharedStrings.S2307}");
			}
		}
	}

	public void ApplyFuzzyFinalPages(IReadOnlyList<int> pages)
	{
		InBookHitInfo currentBookHits = CurrentBookHits;
		if ((object)currentBookHits != null && !currentBookHits.Pages.SequenceEqual(pages))
		{
			CurrentBookHits = currentBookHits with
			{
				Pages = pages.ToList()
			};
		}
	}

	public void ApplyVerifiedHitPages(IReadOnlyList<int> pages)
	{
		if (pages.Count == 0)
		{
			return;
		}
		InBookHitInfo currentBookHits = CurrentBookHits;
		if ((object)currentBookHits != null && !currentBookHits.Pages.SequenceEqual(pages))
		{
			bool num = CurrentMatchIndex == 0 && currentBookHits.Pages.Count > 0 && PdfPage == currentBookHits.Pages[0];
			CurrentBookHits = currentBookHits with
			{
				Pages = pages.ToList()
			};
			if (num)
			{
				CurrentMatchIndex = 0;
				PdfPage = pages[0];
				SelectedHitPage = pages[0];
			}
			UpdateMatchPosition();
		}
	}

	private void UpdateResultPosition()
	{
		if (Results.Count == 0 || CurrentResultIndex < 0)
		{
			ResultPositionText = string.Empty;
			return;
		}
		ResultPositionText = $"{CurrentResultIndex + 1} / {Results.Count}";
	}

	private void UpdateMatchPosition()
	{
		IReadOnlyList<int> readOnlyList = CurrentBookHits?.Pages;
		if (readOnlyList == null || readOnlyList.Count == 0 || CurrentMatchIndex < 0)
		{
			MatchPositionText = string.Empty;
			return;
		}
		MatchPositionText = $"{CurrentMatchIndex + 1} / {readOnlyList.Count}";
	}
























}
