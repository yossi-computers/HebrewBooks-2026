using System;
using System.CodeDom.Compiler;
using System.Collections;
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
using CommunityToolkit.Mvvm.Messaging;
using HebrewBooks.Core.Abstractions;
using HebrewBooks.Core.Catalog;
using HebrewBooks.Core.Models;
using HebrewBooks.Data;
using HebrewBooks.Infrastructure.Settings;
using HebrewBooks.Services.Catalog;
using HebrewBooks.Services.Search;
using HebrewBooks.UI.Collections;
using HebrewBooks.UI.Controls;
using HebrewBooks.UI.Messages;
using HebrewBooks.UI.Navigation;
using HebrewBooks.UI.Resources;
using HebrewBooks.UI.Services;
using HebrewBooks.UI.Views;
using Serilog;

namespace HebrewBooks.UI.ViewModels;

public partial class LibraryViewModel : ObservableObject, IRecipient<CatalogChangedMessage>, IRecipient<ShelvesChangedMessage>, IHitStripNavigator
{
	public enum ContentScope
	{
		All,
		Marked,
		CurrentList
	}

	public sealed record ContentScopeOption(ContentScope Scope, string Label);

	private readonly record struct FavoriteLocation(ObservableCollection<FavoriteBookEntry> Container, FavoriteBookEntry Entry);

	private const int PageSize = 200000;

	private const int FirstChunkSize = 150;

	private readonly CatalogService _catalog;

	private readonly IPathResolver _paths;

	private readonly CatalogFuzzyKeyCache _fuzzyCache;

	private readonly JsonSettingsStore _settings;

	private readonly SearchOrchestrator _search;

	private readonly RemoteSearchClient _remote = new RemoteSearchClient();

	private readonly WebApiClient _webApi = new WebApiClient();

	private readonly RasheyTevotMap _rasheyTevotMap;

	private readonly HebAramMap _hebAramMap;

	private readonly IMadafRepository _madafs;

	[ObservableProperty]
	private bool _isBusy;

	[ObservableProperty]
	private string _statusText = SharedStrings.StatusReady;

	[ObservableProperty]
	private Book? _selectedBook;

	[ObservableProperty]
	private int _totalCount;

	[ObservableProperty]
	private string _filterText = string.Empty;

	[ObservableProperty]
	private string? _currentPdfPath;

	[ObservableProperty]
	private string _currentBookTitle = string.Empty;

	[ObservableProperty]
	private int _currentPage = 1;

	[ObservableProperty]
	private string _inBookSearchText = string.Empty;

	[ObservableProperty]
	private string _inBookSearchStatus = string.Empty;

	[ObservableProperty]
	private bool _isInBookSearching;

	[ObservableProperty]
	private bool _hasOpenBook;

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

	[ObservableProperty]
	private int _currentHitIndex = -1;

	[ObservableProperty]
	[NotifyPropertyChangedFor("HasInBookResults")]
	[NotifyPropertyChangedFor("ShowHitPagesStrip")]
	private InBookHitInfo? _currentBookHits;

	[ObservableProperty]
	private string? _markedPdfPath;

	[ObservableProperty]
	private double _catalogRatio;

	private static readonly MadafNode AllBooksNode = new MadafNode(-1, SharedStrings.S743, View: true, Array.Empty<int>());

	[ObservableProperty]
	private MadafNode? _selectedMadaf;

	[ObservableProperty]
	private IReadOnlyList<Book> _filteredBooks = Array.Empty<Book>();

	private IReadOnlyList<CatalogRow> _topLevelRows = Array.Empty<CatalogRow>();

	[ObservableProperty]
	private CatalogRow? _selectedCatalogRow;

	private bool _syncingSelection;

	private bool _restoringTabContent;

	private readonly ICatalogRepository _catalogRepo;

	private readonly MainViewModel _main;

	private readonly ISearchScopeContext _scope;

	private readonly SynonymLookup _synonyms;

	private readonly IBookLastPageRepository _lastPageRepo;

	private readonly IFavoritesRepository _favoritesRepo;

	[ObservableProperty]
	private bool _isCurrentBookFavorited;

	[ObservableProperty]
	private bool _showFavoritesPanel = true;

	private int _favoritesResolvedCount = -1;

	private readonly NavigationHistory _navigation = new NavigationHistory();

	private bool _suppressNavigationRecord;

	private string? _sessionPath;

	private bool _persistEnabled;

	private NavigationEntry? _pendingRestoreEntry;

	private bool _pendingRestoreApplied;

	[ObservableProperty]
	private int _markRevision;

	[ObservableProperty]
	private int _favoritesRevision;

	private readonly UsageTelemetryService _usage;

	private readonly PerformanceAdvisor _perfAdvisor;

	private readonly PopularitySnapshot _popularity;

	private readonly IShelfTreeRepository _shelfTree;

	private bool _catalogStale;

	private bool _catalogFullyLoaded;

	private bool _loadInFlight;

	private string? _openBookFileId;

	private volatile Dictionary<int, string?> _descById = new Dictionary<int, string>();

	private CancellationTokenSource? _openDebounce;

	[ObservableProperty]
	private bool _isContentMode;

	private bool _showSearchHint = true;

	[ObservableProperty]
	private bool _isSearching;

	[ObservableProperty]
	private SearchResultRow? _selectedRow;

	[ObservableProperty]
	private string? _activeQueryText;

	private CancellationTokenSource? _contentCts;

	[ObservableProperty]
	private bool _chipsScrollDismissed;

	[ObservableProperty]
	private string _activeChipsSummary = string.Empty;

	[ObservableProperty]
	private string _spellCorrectedFrom = string.Empty;

	private string? _spellFixedContentQuery;

	private bool _skipSpellCorrection;

	private const int MaxSelectedChips = 6;

	private bool _revertingChip;

	private CancellationTokenSource? _chipCts;

	private bool _chipsEnabled;

	private IReadOnlyDictionary<string, IReadOnlyList<string>> _inBookSelectedSyns = new Dictionary<string, IReadOnlyList<string>>();

	private const int MaxInlineXfilterFiles = 2500;

	private static readonly TimeSpan SpellSuggestBudget = TimeSpan.FromSeconds(8.0);

	private const int MaxXfilterRequestChars = 60000;

	private long _contentSearchStartedTicks;

	[ObservableProperty]
	private bool _isStopping;

	private List<string>? _scopeSnapshot;

	[ObservableProperty]
	private ContentScopeOption? _selectedScopeOption;

	[ObservableProperty]
	private string _resultFilterText = string.Empty;

	private ICollectionView? _resultsView;

	private readonly SearchHistoryStore _history;

	private readonly SearchResultsCacheStore _resultsCache;

	private bool _settingsLoaded;

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
	private int _maxProximity = 30;

	[ObservableProperty]
	private int _fuzziness;

	[ObservableProperty]
	private bool _requireWordOrder;

	private string? _contentOpenFileId;

	private CancellationTokenSource? _filterCts;

	[ObservableProperty]
	private OpenBookTab? _activeTab;

	private bool _tabsRestored;

	private OpenTabsPersistence.SavedSession? _sessionCache;

	private bool _sessionLoaded;

	private OpenTabsPersistence.SavedTabs? _pendingTearOff;

	private bool _tearOffDone;

	private bool _autoSelectFirstDone;

	[ObservableProperty]
	private int? _selectedHitPage;

	private bool _syncingSelectionFromViewer;

	[ObservableProperty]
	private string _matchPositionText = string.Empty;











































	public IReadOnlyList<string> CurrentTextTerms { get; private set; } = Array.Empty<string>();

	public bool ShowDetailsButton => RowDetailsMode == DataGridRowDetailsVisibilityMode.Collapsed;

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

	public bool ShowInBookChrome
	{
		get
		{
			if (HasOpenBook)
			{
				return !IsTextMode;
			}
			return false;
		}
	}

	public ObservableCollection<int> InBookHitPages { get; } = new ObservableCollection<int>();

	public bool HasInBookResults
	{
		get
		{
			InBookHitInfo currentBookHits = CurrentBookHits;
			if ((object)currentBookHits != null)
			{
				return currentBookHits.HitCount > 0;
			}
			return false;
		}
	}

	public bool ShowHitPagesStrip
	{
		get
		{
			if (HasInBookResults)
			{
				return _settings.Load().View.ShowHitPagesStrip;
			}
			return false;
		}
	}

	public List<Book> AllBooks { get; } = new List<Book>();

	public ObservableCollection<MadafNode> MadafNodes { get; } = new ObservableCollection<MadafNode>();

	public TopicFilterViewModel Topics { get; } = new TopicFilterViewModel();

	public YearFilterViewModel Years { get; } = new YearFilterViewModel();

	public SortFilterViewModel Sorting { get; } = new SortFilterViewModel();

	public RangeObservableCollection<CatalogRow> CatalogRows { get; } = new RangeObservableCollection<CatalogRow>();

	public ObservableCollection<FavoriteFolderViewModel> FavoriteFolderTree { get; } = new ObservableCollection<FavoriteFolderViewModel>();

	public ObservableCollection<FavoriteBookEntry> RootFavorites { get; } = new ObservableCollection<FavoriteBookEntry>();

	public ObservableCollection<string> FavoriteFolderNames { get; } = new ObservableCollection<string>();

	public NavigationHistory Navigation => _navigation;

	public bool CanGoBack => _navigation.CanGoBack;

	public bool CanGoForward => _navigation.CanGoForward;

	public MainViewModel Main => _main;

	public ObservableCollection<ShelfTreeNode> ShelfTree { get; } = new ObservableCollection<ShelfTreeNode>();

	public RangeObservableCollection<SearchResultRow> Results { get; } = new RangeObservableCollection<SearchResultRow>();

	public bool IsBrowseMode => !IsContentMode;

	public bool ContentSearchEnabled { get; }

	public string SearchBoxPlaceholder
	{
		get
		{
			if (!ContentSearchEnabled)
			{
				return SharedStrings.S746;
			}
			return SharedStrings.S745;
		}
	}

	public string SearchHint
	{
		get
		{
			if (!ContentSearchEnabled || !_showSearchHint)
			{
				return string.Empty;
			}
			string text = (FilterText ?? string.Empty).Trim();
			if (text.Length == 0)
			{
				return string.Empty;
			}
			int num = text.IndexOf(':');
			if (num < 0)
			{
				return $"{SharedStrings.S2139}{text}{SharedStrings.S2140}{text}{SharedStrings.S2141}";
			}
			string text2 = text.Substring(0, num).Trim();
			string text3 = text;
			int num2 = num + 1;
			string text4 = text3.Substring(num2, text3.Length - num2).Trim();
			if (text2.Length == 0)
			{
				return SharedStrings.S748;
			}
			if (text4.Length == 0)
			{
				return SharedStrings.S2142 + text2 + "\"";
			}
			return $"{SharedStrings.S2143}{text4}{SharedStrings.S2144}{text2}\"";
		}
	}

	public bool HasSearchHint => SearchHint.Length > 0;

	public ObservableCollection<SynonymChipGroup> SynonymGroups { get; } = new ObservableCollection<SynonymChipGroup>();

	private IEnumerable<SynonymChipVm> AllChips => SynonymGroups.SelectMany((SynonymChipGroup g) => g.Chips);

	public bool HasSynonymChips => SynonymGroups.Count > 0;

	public bool HasSelectedChips => AllChips.Any((SynonymChipVm c) => c.IsSelected);

	public string SearchWithSelectedLabel => $"{SharedStrings.S2145}{AllChips.Count((SynonymChipVm c) => c.IsSelected)})";

	public bool ShowChipSelector
	{
		get
		{
			if (HasSynonymChips)
			{
				return !IsContentMode;
			}
			return false;
		}
	}

	public bool HasActiveChips => !string.IsNullOrEmpty(ActiveChipsSummary);

	public bool HasSpellCorrection => !string.IsNullOrEmpty(SpellCorrectedFrom);

	public string SpellCorrectedTo => _spellFixedContentQuery ?? string.Empty;

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

	public IReadOnlyList<ContentScopeOption> ContentScopeOptions { get; } = new ContentScopeOption[3]
	{
		new ContentScopeOption(ContentScope.All, SharedStrings.S744),
		new ContentScopeOption(ContentScope.Marked, SharedStrings.S770),
		new ContentScopeOption(ContentScope.CurrentList, SharedStrings.S771)
	};

	public bool IsScopeActive
	{
		get
		{
			ContentScopeOption selectedScopeOption = SelectedScopeOption;
			if ((object)selectedScopeOption != null)
			{
				return selectedScopeOption.Scope != ContentScope.All;
			}
			return false;
		}
	}

	public string ScopeChipText => SelectedScopeOption?.Scope switch
	{
		ContentScope.Marked => $"{SharedStrings.S2165}{_scope.MarkedCount}{SharedStrings.S2166}", 
		ContentScope.CurrentList => $"{SharedStrings.S2167}{_scopeSnapshot?.Count ?? 0}{SharedStrings.S2168}", 
		_ => string.Empty, 
	};

	public ObservableCollection<string> SearchHistory { get; } = new ObservableCollection<string>();

	public bool SuppressNextOpen { get; set; }

	public bool CanShareLink
	{
		get
		{
			Book selectedBook = SelectedBook;
			int result;
			if ((object)selectedBook != null && string.Equals(selectedBook.SourceType, "PDF", StringComparison.Ordinal))
			{
				return int.TryParse(selectedBook.FileID, out result);
			}
			return false;
		}
	}

	public int MarkedCount => _scope.MarkedCount;

	public bool HasMarkedBooks => _scope.MarkedCount > 0;

	public ObservableCollection<OpenBookTab> OpenTabs { get; } = new ObservableCollection<OpenBookTab>();

	public bool HasTabs => OpenTabs.Count > 0;

	public Action<IReadOnlyList<OpenTabsPersistence.SavedTabs>>? SpawnRestoreWindows { get; set; }

	private bool TabRestorePending
	{
		get
		{
			if (_sessionLoaded && !_tabsRestored)
			{
				OpenTabsPersistence.SavedSession? sessionCache = _sessionCache;
				if ((object)sessionCache == null)
				{
					return false;
				}
				return sessionCache.Windows.FirstOrDefault()?.Tabs.Count > 0;
			}
			return false;
		}
	}

	public bool BatchingInlineOpen { get; private set; }































































































	public event Action<int>? ScrollToPageRequested;

	public event Action? CatalogScrollToTopRequested;

	void IRecipient<CatalogChangedMessage>.Receive(CatalogChangedMessage message)
	{
		_catalogStale = true;
		_fuzzyCache.Invalidate();
	}

	void IRecipient<ShelvesChangedMessage>.Receive(ShelvesChangedMessage message)
	{
		ReloadShelvesAsync();
	}

	public async Task ReloadShelvesAsync()
	{
		try
		{
			int? selectedId = SelectedMadaf?.MadafID;
			IReadOnlyList<MadafNode> obj = await _madafs.GetTreeAsync();
			MadafNodes.Clear();
			MadafNodes.Add(AllBooksNode);
			foreach (MadafNode item in obj)
			{
				MadafNodes.Add(item);
			}
			SelectedMadaf = MadafNodes.FirstOrDefault((MadafNode m) => m.MadafID == selectedId) ?? AllBooksNode;
			ApplyFilter();
		}
		catch
		{
		}
		await LoadShelfTreeAsync();
	}

	public async Task LoadShelfTreeAsync()
	{
		try
		{
			IReadOnlyList<ShelfTreeNode> obj = await _shelfTree.GetTreeAsync();
			ShelfTree.Clear();
			foreach (ShelfTreeNode item in obj)
			{
				ShelfTree.Add(item);
			}
		}
		catch (Exception exception)
		{
			Log.Warning(exception, "LoadShelfTree failed");
		}
	}

	public void FilterByShelfNode(ShelfTreeNode shelf)
	{
		HashSet<string> fileIds = new HashSet<string>(StringComparer.Ordinal);
		Collect(shelf);
		List<int> bookIds = (from b in AllBooks
			where !string.IsNullOrEmpty(b.FileID) && fileIds.Contains(b.FileID)
			select b.ID).ToList();
		SelectedMadaf = new MadafNode(shelf.NodeId, shelf.Title, View: true, bookIds);
		void Collect(ShelfTreeNode n)
		{
			if (n.Kind == ShelfNodeKind.Book && !string.IsNullOrEmpty(n.FileId))
			{
				fileIds.Add(n.FileId);
			}
			foreach (ShelfTreeNode child in n.Children)
			{
				Collect(child);
			}
		}
	}

	public void ClearShelfFilter()
	{
		SelectedMadaf = AllBooksNode;
	}

	private void OnShowRowDetailsChanged(bool value)
	{
		RowDetailsMode = (value ? DataGridRowDetailsVisibilityMode.VisibleWhenSelected : DataGridRowDetailsVisibilityMode.Collapsed);
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
		OnPropertyChanged("CorpusFilterSummary");
		ApplyFilter();
		SaveSession();
	}

	private void OnSortingChanged()
	{
		if (IsContentMode)
		{
			ApplyResultSort();
		}
		else
		{
			ApplyFilter();
		}
	}

	private void ApplyResultSort()
	{
		if (_resultsView is ListCollectionView listCollectionView)
		{
			listCollectionView.CustomSort = ResultSortComparer(Sorting.Layers);
		}
	}

	internal static IComparer? ResultSortComparer(IReadOnlyList<SortLayer> layers)
	{
		if (layers.Count == 0)
		{
			return null;
		}
		IComparer<SearchResultRow> rows = CatalogSorting.RowComparer(layers);
		return Comparer<object>.Create((object a, object b) => (a is SearchResultRow x && b is SearchResultRow y) ? rows.Compare(x, y) : 0);
	}

	public LibraryViewModel(CatalogService catalog, IPathResolver paths, JsonSettingsStore settings, SearchOrchestrator search, RasheyTevotMap rasheyTevotMap, HebAramMap hebAramMap, IMadafRepository madafs, ICatalogRepository catalogRepo, MainViewModel main, ISearchScopeContext scope, SearchHistoryStore history, SearchResultsCacheStore resultsCache, IBookLastPageRepository lastPageRepo, IFavoritesRepository favoritesRepo, SynonymLookup synonyms, UsageTelemetryService usage, PopularitySnapshot popularity, IShelfTreeRepository shelfTree, PerformanceAdvisor perfAdvisor)
	{
		_scope = scope;
		_synonyms = synonyms;
		_usage = usage;
		_perfAdvisor = perfAdvisor;
		_popularity = popularity;
		_shelfTree = shelfTree;
		_catalog = catalog;
		_paths = paths;
		_fuzzyCache = new CatalogFuzzyKeyCache(_paths.UserDataRoot);
		_settings = settings;
		_search = search;
		_rasheyTevotMap = rasheyTevotMap;
		_hebAramMap = hebAramMap;
		_madafs = madafs;
		_catalogRepo = catalogRepo;
		_main = main;
		_history = history;
		_resultsCache = resultsCache;
		_lastPageRepo = lastPageRepo;
		_favoritesRepo = favoritesRepo;
		_catalogRatio = Math.Clamp(_settings.Load().View.LibraryCatalogRatio, 0.15, 0.85);
		_navigation.StateChanged += delegate
		{
			OnPropertyChanged("CanGoBack");
			OnPropertyChanged("CanGoForward");
			GoBackCommand.NotifyCanExecuteChanged();
			GoForwardCommand.NotifyCanExecuteChanged();
		};
		RowDetailsMode = (_settings.Load().View.ShowRowDetails ? DataGridRowDetailsVisibilityMode.VisibleWhenSelected : DataGridRowDetailsVisibilityMode.Collapsed);
		SettingsViewModel.ShowRowDetailsChanged += OnShowRowDetailsChanged;
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
		SelectedScopeOption = ContentScopeOptions[0];
		Sorting.RelevanceAvailable = false;
		Sorting.Changed += OnSortingChanged;
		_resultsView = CollectionViewSource.GetDefaultView(Results);
		_resultsView.Filter = MatchesResultFilter;
		ViewOptions view = _settings.Load().View;
		ContentSearchEnabled = view.UnifiedSearchLayout;
		_showSearchHint = view.ShowSearchHint;
		LoadSearchSettings();
		SettingsViewModel.SearchOptionsChanged += OnSharedSearchOptionsChanged;
		SettingsViewModel.ShowSearchHintChanged += OnShowSearchHintChanged;
		_chipsEnabled = _settings.Load().View.EnableSynonymChips;
		SettingsViewModel.SynonymChipsEnabledChanged += OnSynonymChipsEnabledChanged;
		ReloadHistory();
		Topics.Changed += ApplyFilter;
		Years.Changed += OnYearFilterChanged;
		_scope.Changed += OnScopeChanged;
		WeakReferenceMessenger.Default.RegisterAll(this);
	}

	private void OnShowSearchHintChanged(bool value)
	{
		_showSearchHint = value;
		OnPropertyChanged("SearchHint");
		OnPropertyChanged("HasSearchHint");
	}

	[RelayCommand]
	private void ExitContentMode()
	{
		_contentCts?.Cancel();
		IsContentMode = false;
		IsSearching = false;
		Results.Clear();
		SelectedRow = null;
		_contentOpenFileId = null;
		ResultFilterText = string.Empty;
		ActiveChipsSummary = string.Empty;
		FilterText = string.Empty;
		ApplyFilter();
		RecordNavIfNotSuppressed();
	}

	private NavigationEntry SnapshotCurrent()
	{
		return new NavigationEntry(SelectedBook?.FileID, SelectedBook?.SourceType, SelectedBook?.RelativePath, CurrentPage, FilterText ?? string.Empty, IsContentMode, (IsContentMode && Results.Count > 0) ? new List<SearchResultRow>(Results) : null, SelectedRow?.Book?.FileID, StatusText);
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
		Log.Information("Nav: pushed entry results={ResultsCount} backDepth={Depth} contentMode={IsContent}", count, (back2 != null) ? back2.Length : 0, IsContentMode);
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
			FilterText = entry.FilterText;
			if (entry.IsContentMode)
			{
				IsContentMode = true;
				Results.Clear();
				if (entry.ContentResults != null)
				{
					foreach (SearchResultRow contentResult in entry.ContentResults)
					{
						Results.Add(contentResult);
					}
				}
				SelectedRow = ((!string.IsNullOrEmpty(entry.SelectedResultFileId)) ? Results.FirstOrDefault((SearchResultRow r) => string.Equals(r.Book?.FileID, entry.SelectedResultFileId, StringComparison.Ordinal)) : null);
			}
			else
			{
				IsContentMode = false;
				Results.Clear();
				SelectedRow = null;
				ApplyFilter();
			}
			StatusText = entry.StatusText ?? string.Empty;
			if (!string.IsNullOrEmpty(entry.BookFileId))
			{
				Book book = AllBooks.FirstOrDefault((Book b) => string.Equals(b.FileID, entry.BookFileId, StringComparison.Ordinal));
				if ((object)book != null)
				{
					SelectedBook = book;
					OpenSelected();
					CurrentPage = entry.Page;
				}
				else
				{
					Log.Warning("Session restore: recorded book {Fid} not found in AllBooks ({N} loaded) — nothing reopened", entry.BookFileId, AllBooks.Count);
				}
			}
			else
			{
				SelectedBook = null;
				CurrentPdfPath = null;
				CurrentTextRelativePath = null;
				HasOpenBook = false;
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

	public void EnableSessionPersistence(string filePath)
	{
		_sessionPath = filePath;
		if (!_settings.Load().View.PersistSessionState)
		{
			Log.Information("Session restore: skipped — 'remember state across restarts' (PersistSessionState) is off");
			return;
		}
		_persistEnabled = true;
		NavigationHistorySnapshot navigationHistorySnapshot = NavigationSessionStore.Load(filePath);
		if ((object)navigationHistorySnapshot != null)
		{
			_navigation.Restore(navigationHistorySnapshot);
			RestoreCorpusSelection(navigationHistorySnapshot.SelectedCorpora);
			_pendingRestoreEntry = _navigation.Current;
			Log.Information("Session restore: queued from session.json — bookFileId={Fid} contentMode={Cm} catalogAlreadyLoaded={Loaded}", _pendingRestoreEntry?.BookFileId, _pendingRestoreEntry?.IsContentMode, AllBooks.Count > 0);
		}
		else
		{
			Log.Information("Session restore: nothing to restore (no session.json — first run or cleared)");
		}
		_navigation.StateChanged += SaveSession;
		TryApplyPendingRestore();
	}

	private bool TryApplyPendingRestore()
	{
		if (_pendingRestoreApplied || (object)_pendingRestoreEntry == null || AllBooks.Count == 0)
		{
			return false;
		}
		_pendingRestoreApplied = true;
		NavigationEntry pendingRestoreEntry = _pendingRestoreEntry;
		_pendingRestoreEntry = null;
		Log.Information("Session restore: applying — bookFileId={Fid} contentMode={Cm} allBooks={N}", pendingRestoreEntry?.BookFileId, pendingRestoreEntry?.IsContentMode, AllBooks.Count);
		ApplyEntry(pendingRestoreEntry);
		return true;
	}

	private void SaveSession()
	{
		if (_persistEnabled && _sessionPath != null)
		{
			NavigationSessionStore.Save(_navigation.Snapshot()with
			{
				SelectedCorpora = SelectedCorpusValues()
			}, _sessionPath);
		}
	}

	private string[] SelectedCorpusValues()
	{
		return (from o in CorpusFilters
			where o.Value != "All" && o.IsSelected
			select o.Value).ToArray();
	}

	private void RestoreCorpusSelection(IReadOnlyList<string>? selected)
	{
		if (selected == null)
		{
			return;
		}
		HashSet<string> hashSet = new HashSet<string>(selected, StringComparer.Ordinal);
		_suppressCorpusSync = true;
		try
		{
			foreach (CorpusFilterOption item in CorpusFilters.Where((CorpusFilterOption o) => o.Value != "All"))
			{
				item.IsSelected = hashSet.Contains(item.Value);
			}
			CorpusFilters.First((CorpusFilterOption o) => o.Value == "All").IsSelected = CorpusFilters.Where((CorpusFilterOption o) => o.Value != "All").All((CorpusFilterOption o) => o.IsSelected);
		}
		finally
		{
			_suppressCorpusSync = false;
		}
		OnPropertyChanged("CorpusFilterSummary");
		ApplyFilter();
	}

	[RelayCommand]
	private async Task SearchTypedExactAsync()
	{
		if (!HasSpellCorrection)
		{
			return;
		}
		_skipSpellCorrection = true;
		try
		{
			await RunContentSearchAsync();
		}
		finally
		{
			_skipSpellCorrection = false;
		}
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
				StatusText = $"{SharedStrings.S2146}{6}{SharedStrings.S2147}";
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
		OnPropertyChanged("HasSelectedChips");
		OnPropertyChanged("SearchWithSelectedLabel");
	}

	private async void DebounceSynonymChips(string? text)
	{
		_chipCts?.Cancel();
		string content = text ?? string.Empty;
		int num = content.IndexOf(':');
		if (num >= 0)
		{
			string text2 = content;
			int num2 = num + 1;
			content = text2.Substring(num2, text2.Length - num2);
		}
		content = content.Trim();
		if (HebrewKeyboard.LooksMistyped(content))
		{
			content = HebrewKeyboard.ToHebrew(content);
		}
		if (!_chipsEnabled || !ContentSearchEnabled || content.Length == 0)
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
	private void SearchWithSelectedChips()
	{
		if (HasSelectedChips && RunContentSearchCommand.CanExecute(null))
		{
			RunContentSearchCommand.Execute(null);
		}
	}

	public void RunSearchFromExternal(string query)
	{
		if (!string.IsNullOrWhiteSpace(query))
		{
			FilterText = query.Trim();
			if (ContentSearchEnabled && RunContentSearchCommand.CanExecute(null))
			{
				RunContentSearchCommand.Execute(null);
			}
		}
	}

	[RelayCommand]
	private async Task RunContentSearchAsync()
	{
		if (!ContentSearchEnabled)
		{
			return;
		}
		string raw = FilterText?.Trim() ?? string.Empty;
		if (raw.Length == 0)
		{
			ExitContentMode();
			return;
		}
		_filterCts?.Cancel();
		List<string> list = (from c in AllChips
			where c.IsSelected
			select c.Term).ToList();
		ActiveChipsSummary = ((list.Count > 0) ? string.Join("  ·  ", list) : string.Empty);
		SpellCorrectedFrom = string.Empty;
		_spellFixedContentQuery = null;
		string text = raw;
		bool flag = false;
		if (!_skipSpellCorrection && HebrewKeyboard.LooksMistyped(raw))
		{
			text = HebrewKeyboard.ToHebrew(raw);
			flag = true;
		}
		string q = text;
		string catalogPart = null;
		IReadOnlyList<string> catalogScope = null;
		int num = text.IndexOf(':');
		if (num >= 0)
		{
			catalogPart = text.Substring(0, num).Trim();
			string text2 = text;
			int num2 = num + 1;
			q = text2.Substring(num2, text2.Length - num2).Trim();
			if (q.Length == 0)
			{
				_contentCts?.Cancel();
				IsContentMode = false;
				IsSearching = false;
				Results.Clear();
				SelectedRow = null;
				ApplyFilter();
				return;
			}
			if (catalogPart.Length > 0)
			{
				catalogScope = (from b in RankAndOrderBooks(BrowseSource(), catalogPart)
					where !string.IsNullOrEmpty(b.FileID)
					select b.FileID).Distinct<string>(StringComparer.Ordinal).ToList();
				if (catalogScope.Count == 0)
				{
					_contentCts?.Cancel();
					IsContentMode = true;
					IsSearching = false;
					Results.Clear();
					SelectedRow = null;
					StatusText = SharedStrings.S2148 + catalogPart + "\"";
					return;
				}
			}
		}
		if (flag)
		{
			int num3 = raw.IndexOf(':');
			LibraryViewModel libraryViewModel = this;
			string spellCorrectedFrom;
			if (num3 < 0)
			{
				spellCorrectedFrom = raw;
			}
			else
			{
				string text2 = raw;
				int num2 = num3 + 1;
				spellCorrectedFrom = text2.Substring(num2, text2.Length - num2).Trim();
			}
			libraryViewModel.SpellCorrectedFrom = spellCorrectedFrom;
			_spellFixedContentQuery = q;
			OnPropertyChanged("SpellCorrectedTo");
		}
		if (!q.Contains(' ') && CountHebrewLetters(q) < 2)
		{
			_contentCts?.Cancel();
			IsContentMode = true;
			IsSearching = false;
			Results.Clear();
			SelectedRow = null;
			StatusText = SharedStrings.S755;
			return;
		}
		ReportAbandonedContentSearch();
		_contentCts?.Cancel();
		CancellationTokenSource myCts = (_contentCts = new CancellationTokenSource());
		CancellationToken ct = myCts.Token;
		Stopwatch sw = Stopwatch.StartNew();
		_contentSearchStartedTicks = Environment.TickCount64;
		try
		{
			IsSearching = true;
			IsContentMode = true;
			Results.Clear();
			SelectedRow = null;
			_contentOpenFileId = null;
			ResultFilterText = string.Empty;
			StatusText = SharedStrings.StatusSearching;
			SearchOptions sopts = _settings.Load().Search;
			string fuzzyNote = string.Empty;
			IProgress<SearchResultRow> progress = null;
			Progress<int> liveHitCount = new Progress<int>(delegate(int n)
			{
				if (_contentCts == myCts)
				{
					StatusText = string.Format(SharedStrings.StatusFoundResults, n);
				}
			});
			HashSet<string> corpora = (from corpusFilterOption in CorpusFilters
				where corpusFilterOption.Value != "All" && corpusFilterOption.IsSelected
				select corpusFilterOption.Value).ToHashSet<string>(StringComparer.Ordinal);
			int num4 = CorpusFilters.Count((CorpusFilterOption corpusFilterOption) => corpusFilterOption.Value != "All");
			HashSet<string> corporaSelected = ((corpora.Count > 0 && corpora.Count < num4) ? corpora : null);
			IReadOnlyList<string> scope = catalogScope ?? ScopeRestrictFileIds();
			string onlineUrl = OnlineServiceUrl();
			string remoteUrl = ((onlineUrl == null) ? RemoteSearchUrl() : null);
			IReadOnlyList<SearchResultRow> rows;
			int hitDivisor;
			if (onlineUrl != null)
			{
				IReadOnlyDictionary<string, IReadOnlyList<string>> bySource = (_inBookSelectedSyns = SelectedSynonymsBySource());
				ActiveQueryText = q;
				WebApiClient.Options o = new WebApiClient.Options(Math.Max(1, MaxProximity), Hybur, RootSearch, ExpandGematria, ExpandSpelling, ExpandNumberGender, ExpandAramaic, RasheyTevot, RequireWordOrder, ExpandRashiOcr, Math.Clamp(Fuzziness, 0, 10), Math.Max(1, sopts.MaxFilesToRetrieve), MapCorpusForApi(corporaSelected), "hitCount", scope, FlattenSynonyms(bySource));
				WebApiClient.SearchOutcome searchOutcome = await _webApi.SearchAsync(onlineUrl, q, o, progress, ct);
				rows = searchOutcome.Rows;
				if (rows.Count > 0 && !_skipSpellCorrection && !string.IsNullOrWhiteSpace(searchOutcome.CorrectedQuery) && !string.Equals(searchOutcome.CorrectedQuery, searchOutcome.OriginalQuery ?? q, StringComparison.Ordinal))
				{
					_spellFixedContentQuery = searchOutcome.CorrectedQuery;
					OnPropertyChanged("SpellCorrectedTo");
					ActiveQueryText = searchOutcome.CorrectedQuery;
					SpellCorrectedFrom = searchOutcome.OriginalQuery ?? q;
				}
			}
			else if (remoteUrl != null)
			{
				IReadOnlyDictionary<string, IReadOnlyList<string>> readOnlyDictionary = (_inBookSelectedSyns = SelectedSynonymsBySource());
				string text3 = q;
				if (readOnlyDictionary.Count > 0)
				{
					bool flag2 = Math.Clamp(Fuzziness, 0, 10) > 0;
					text3 = BuildWithSynonyms(q, readOnlyDictionary, new QueryBuildOptions(Math.Max(1, MaxProximity), Hybur, FirstWordOnly: false, LastWordOnly: false, (!flag2 && RasheyTevot) ? _rasheyTevotMap : null, !flag2 && RootSearch, !flag2 && ExpandNumberGender, !flag2 && ExpandGematria, !flag2 && ExpandSpelling, (!flag2 && ExpandAramaic) ? _hebAramMap : null, null, 200, RequireWordOrder: RequireWordOrder, ExpandRashiOcr: !flag2 && ExpandRashiOcr));
				}
				ActiveQueryText = text3;
				RemoteSearchClient.Options options = new RemoteSearchClient.Options(Math.Max(1, MaxProximity), Hybur, RootSearch, ExpandGematria, ExpandSpelling, ExpandNumberGender, ExpandAramaic, RasheyTevot, RequireWordOrder, ExpandRashiOcr, Math.Clamp(Fuzziness, 0, 10), Math.Max(1, sopts.MaxFilesToRetrieve), (corporaSelected == null) ? null : MapCorporaForService(corporaSelected), scope);
				string rfp = SearchResultsCacheStore.RemoteFingerprint(remoteUrl, text3, options.Proximity, options.Hybur, options.Roots, options.Gematria, options.Spelling, options.NumberGender, options.Aramaic, options.RasheyTevot, options.RequireWordOrder, options.RashiOcr, options.Fuzziness, options.MaxFiles, options.Corpora, options.RestrictFileIds);
				IReadOnlyList<SearchResultRow> readOnlyList = _resultsCache.TryLoadRows(rfp);
				if (readOnlyList != null)
				{
					rows = readOnlyList;
				}
				else
				{
					rows = await _remote.SearchAsync(remoteUrl, text3, options, progress, ct);
					if (_contentCts == myCts)
					{
						_resultsCache.SaveRows(rfp, rows);
					}
				}
			}
			else
			{
				await _search.EnsureIndexOpenAsync(ct);
				bool fuzzyOn = Math.Clamp(Fuzziness, 0, 10) > 0;
				bool flag3 = RootSearch || ExpandSpelling || ExpandGematria || ExpandNumberGender || ExpandAramaic || RasheyTevot || ExpandRashiOcr || ExpandWeakLetters;
				if (fuzzyOn && flag3)
				{
					fuzzyNote = SharedStrings.S9073;
				}
				QueryBuildOptions qbOpts = new QueryBuildOptions(Math.Max(1, MaxProximity), Hybur, FirstWordOnly: false, LastWordOnly: false, (!fuzzyOn && RasheyTevot) ? _rasheyTevotMap : null, !fuzzyOn && RootSearch, !fuzzyOn && ExpandNumberGender, !fuzzyOn && ExpandGematria, !fuzzyOn && ExpandSpelling, (!fuzzyOn && ExpandAramaic) ? _hebAramMap : null, _search.Engine.FilterIndexedWords, 200, RequireWordOrder: RequireWordOrder, ExpandRashiOcr: !fuzzyOn && ExpandRashiOcr, ExpandWeakLetters: !fuzzyOn && ExpandWeakLetters);
				IReadOnlyDictionary<string, IReadOnlyList<string>> selBySource = SelectedSynonymsBySource();
				_inBookSelectedSyns = selBySource;
				string text4 = (ActiveQueryText = await Task.Run(() => BuildWithSynonyms(q, selBySource, qbOpts), ct));
				hitDivisor = QueryBuilder.CountMatchWords(q);
				IReadOnlyList<string> idxPaths = null;
				if (corporaSelected != null)
				{
					List<string> list2 = new List<string>();
					if (corporaSelected.Contains("PDF"))
					{
						list2.Add(_paths.IndexesRoot);
					}
					if (corporaSelected.Contains("Text"))
					{
						list2.Add(_paths.OtzrayaIndexPath);
					}
					if (corporaSelected.Contains("Personal"))
					{
						list2.Add(_paths.PersonalIndexPath);
					}
					if (list2.Count > 0)
					{
						idxPaths = list2;
					}
				}
				string rfp = SearchResultsCacheStore.Fingerprint(MakeQuery(text4, scope, idxPaths));
				long idxStamp = CurrentIndexStamp();
				IReadOnlyList<SearchHit> cachedHits = _resultsCache.TryLoad(rfp, idxStamp);
				IReadOnlyList<SearchResultRow> readOnlyList2 = ((cachedHits == null) ? (await ExecuteAsync(text4)) : (await _search.RehydrateAsync(cachedHits, ct)));
				rows = readOnlyList2;
				if (rows.Count == 0 && !fuzzyOn && !_skipSpellCorrection && !ct.IsCancellationRequested)
				{
					Stopwatch spellSw = Stopwatch.StartNew();
					SpellSuggestionService.Correction correction = await SpellSuggestionService.SuggestAsync(_search.Engine, q, SpellSuggestBudget, ct, delegate(string step)
					{
						Log.Information("TIMING: SpellSuggest step {Step}", step);
					});
					Log.Information("TIMING: SpellSuggest {Ms}ms → {Correction}", spellSw.ElapsedMilliseconds, correction?.Corrected ?? "(none)");
					if ((object)correction != null)
					{
						string corrected = correction.Corrected;
						string fixedText = await Task.Run(() => BuildWithSynonyms(corrected, selBySource, qbOpts), ct);
						if (!string.IsNullOrEmpty(fixedText))
						{
							hitDivisor = QueryBuilder.CountMatchWords(correction.Corrected);
							IReadOnlyList<SearchResultRow> readOnlyList3 = await ExecuteAsync(fixedText);
							if (readOnlyList3.Count > 0)
							{
								rows = readOnlyList3;
								_spellFixedContentQuery = correction.Corrected;
								OnPropertyChanged("SpellCorrectedTo");
								SpellCorrectedFrom = q;
								ActiveQueryText = fixedText;
							}
							else
							{
								hitDivisor = QueryBuilder.CountMatchWords(q);
							}
						}
					}
				}
				if (cachedHits == null && _contentCts == myCts)
				{
					_resultsCache.Save(rfp, idxStamp, rows.Select((SearchResultRow r) => new SearchHit(r.Book.FileID ?? string.Empty, r.HitCount, r.Location, r.PageNumber)).ToList());
				}
			}
			long elapsedMilliseconds = sw.ElapsedMilliseconds;
			if (_contentCts == myCts)
			{
				Results.ReplaceAll(rows);
				_syncingSelection = true;
				try
				{
					SelectedRow = ((Results.Count > 0) ? Results[0] : null);
				}
				finally
				{
					_syncingSelection = false;
				}
				Log.Information("TIMING: Search[Combined] engine={EngineMs}ms populate={PopulateMs}ms total={TotalMs}ms rows={Rows}", elapsedMilliseconds, sw.ElapsedMilliseconds - elapsedMilliseconds, sw.ElapsedMilliseconds, rows.Count);
				_perfAdvisor.ReportOperation(SlowStage.Search, elapsedMilliseconds);
				string text6 = ((catalogScope != null) ? $"{SharedStrings.S2149}{catalogScope.Count}{SharedStrings.S2150}{catalogPart}\"" : string.Empty);
				StatusText = ComposeContentStatus(Results.Count, sw.Elapsed, cancelled: false) + text6 + fuzzyNote + LowProximityHint(q, Results.Count) + ((remoteUrl == null && onlineUrl == null) ? IndexNotReadyHint(corpora) : string.Empty) + CapHint(Results.Count, sopts.MaxFilesToRetrieve);
				_history.Add(raw);
				ReloadHistory();
				RecordNavIfNotSuppressed();
			}
			async Task<IReadOnlyList<SearchResultRow>> ExecuteAsync(string text7)
			{
				if (scope == null || scope.Count <= 0)
				{
					IReadOnlyList<string> idxPaths2 = null;
					if (corporaSelected != null)
					{
						List<string> list3 = new List<string>();
						if (corporaSelected.Contains("PDF"))
						{
							list3.Add(_paths.IndexesRoot);
						}
						if (corporaSelected.Contains("Text"))
						{
							list3.Add(_paths.OtzrayaIndexPath);
						}
						if (corporaSelected.Contains("Personal"))
						{
							list3.Add(_paths.PersonalIndexPath);
						}
						if (list3.Count > 0)
						{
							idxPaths2 = list3;
						}
					}
					return await _search.RunAsync(MakeQuery(text7, null, idxPaths2), SortMode.HitCount, progress, ct, liveHitCount);
				}
				List<SearchResultRow> merged = new List<SearchResultRow>();
				int liveBase = 0;
				int liveLast = 0;
				Progress<int> cumLive = new Progress<int>(delegate(int n)
				{
					liveLast = n;
					if (_contentCts == myCts)
					{
						StatusText = string.Format(SharedStrings.StatusFoundResults, liveBase + n);
					}
				});
				List<string> list4 = scope.Where(IsPdfStem).ToList();
				if (list4.Count > 0)
				{
					string passText = text7;
					if (list4.Count <= 2500)
					{
						string value = string.Join(" or ", list4.Select((string id) => "xfilter(name \"" + id + ".pdf\")"));
						string text8 = $"({text7}) and ({value})";
						if (text8.Length <= 60000)
						{
							passText = text8;
						}
					}
					await RunPassAsync(passText, list4, new string[1] { _paths.IndexesRoot });
				}
				List<string> list5 = scope.Where((string id) => !IsPdfStem(id)).ToList();
				if (list5.Count > 0)
				{
					List<string> list6 = new List<string>();
					if (!string.IsNullOrEmpty(_paths.OtzrayaIndexPath))
					{
						list6.Add(_paths.OtzrayaIndexPath);
					}
					if (!string.IsNullOrEmpty(_paths.PersonalIndexPath))
					{
						list6.Add(_paths.PersonalIndexPath);
					}
					await RunPassAsync(text7, list5, (list6.Count > 0) ? list6 : null);
				}
				return merged.OrderByDescending((SearchResultRow r) => r.HitCount).ToList();
				async Task RunPassAsync(string text9, IReadOnlyList<string> restrictIds, IReadOnlyList<string>? idxPaths3)
				{
					IReadOnlyList<SearchResultRow> collection = await _search.RunAsync(MakeQuery(text9, restrictIds, idxPaths3), SortMode.HitCount, progress, ct, cumLive);
					merged.AddRange(collection);
					liveBase += liveLast;
					liveLast = 0;
				}
			}
			SearchQuery MakeQuery(string text7, IReadOnlyList<string>? restrictIds, IReadOnlyList<string>? restrictToIndexPaths)
			{
				return new SearchQuery(text7, Math.Max(1, MaxProximity), Hybur, IncludeNumbers: true, MaxFiles(), Math.Clamp(Fuzziness, 0, 10), restrictIds, restrictToIndexPaths, hitDivisor);
			}
			int MaxFiles()
			{
				return Math.Max(1, sopts.MaxFilesToRetrieve);
			}
		}
		catch (OperationCanceledException)
		{
			if (_contentCts == myCts)
			{
				StatusText = ComposeContentStatus(Results.Count, sw.Elapsed, cancelled: true);
				RecordNavIfNotSuppressed();
			}
		}
		catch (Exception ex2)
		{
			if (_contentCts == myCts)
			{
				StatusText = ex2.Message;
			}
		}
		finally
		{
			if (_contentCts == myCts)
			{
				IsSearching = false;
				IsStopping = false;
				_contentSearchStartedTicks = 0L;
			}
		}
	}

	private static bool IsPdfStem(string fileId)
	{
		if (fileId.Length > 0)
		{
			return fileId.All(char.IsAsciiDigit);
		}
		return false;
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

	private string IndexNotReadyHint(IReadOnlySet<string> searchedCorpora)
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
			if (searchedCorpora.Contains(corpus))
			{
				int? num = _main.IndexBuildPercentFor(indexPath);
				if (num.HasValue)
				{
					int valueOrDefault = num.GetValueOrDefault();
					notes.Add($"{SharedStrings.S2151}{label}{SharedStrings.S2152}{valueOrDefault}{SharedStrings.S2153}");
				}
				else if (!SearchOrchestrator.IsIndexBuilt(indexPath))
				{
					notes.Add(SharedStrings.S2154 + label + SharedStrings.S2155);
				}
			}
		}
	}

	private static int CountHebrewLetters(string s)
	{
		int num = 0;
		foreach (char c in s)
		{
			if (c >= 'א' && c <= 'ת')
			{
				num++;
			}
		}
		return num;
	}

	private static string CapHint(int count, int max)
	{
		if (max <= 0 || count < max)
		{
			return string.Empty;
		}
		return $"{SharedStrings.S2156}{max}{SharedStrings.S2157}";
	}

	private static string ComposeContentStatus(int count, TimeSpan elapsed, bool cancelled)
	{
		double totalSeconds = elapsed.TotalSeconds;
		string text = ((totalSeconds < 1.0) ? $"({elapsed.TotalMilliseconds:F0}{SharedStrings.S2158}" : $"({totalSeconds:F1}{SharedStrings.S2159}");
		if (cancelled)
		{
			if (count != 0)
			{
				return $"{SharedStrings.S2161}{count}{SharedStrings.S2162}{text}";
			}
			return SharedStrings.S2160 + text;
		}
		if (count == 0)
		{
			return SharedStrings.StatusNoResults + " " + text;
		}
		return $"{SharedStrings.S2163}{count}{SharedStrings.S2164}{text}";
	}

	private void ReportAbandonedContentSearch()
	{
		if (IsSearching && _contentSearchStartedTicks != 0L)
		{
			long elapsedMs = Environment.TickCount64 - _contentSearchStartedTicks;
			_contentSearchStartedTicks = 0L;
			_perfAdvisor.ReportOperation(SlowStage.Search, elapsedMs);
		}
	}

	[RelayCommand]
	private void CancelContentSearch()
	{
		if (IsSearching)
		{
			ReportAbandonedContentSearch();
			_contentCts?.Cancel();
			_contentCts = null;
			IsStopping = false;
			IsSearching = false;
			StatusText = SharedStrings.S768;
		}
	}

	private IReadOnlyList<string>? ScopeRestrictFileIds()
	{
		switch (SelectedScopeOption?.Scope)
		{
		case ContentScope.Marked:
		{
			IReadOnlyCollection<string> markedFileIds = _scope.MarkedFileIds;
			return (markedFileIds != null && markedFileIds.Count > 0) ? markedFileIds.ToList() : null;
		}
		case ContentScope.CurrentList:
		{
			List<string> scopeSnapshot = _scopeSnapshot;
			return (scopeSnapshot != null && scopeSnapshot.Count > 0) ? _scopeSnapshot : null;
		}
		default:
			return null;
		}
	}

	[RelayCommand]
	private void ClearScope()
	{
		SelectedScopeOption = ContentScopeOptions[0];
	}

	private void OnYearFilterChanged()
	{
		if (IsContentMode)
		{
			_resultsView?.Refresh();
		}
		else
		{
			ApplyFilter();
		}
	}

	private bool MatchesResultFilter(object? o)
	{
		if (!(o is SearchResultRow searchResultRow))
		{
			return false;
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

	private void LoadSearchSettings()
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

	private void SaveSearchSettings()
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
		LoadSearchSettings();
	}

	public void RefreshSearchOptionsFromDisk()
	{
		LoadSearchSettings();
	}

	[RelayCommand]
	private void ToggleFirstWord()
	{
		string text = FilterText ?? string.Empty;
		string filterText;
		if (!text.StartsWith("▶", StringComparison.Ordinal))
		{
			filterText = "▶ " + text.TrimStart();
		}
		else
		{
			string text2 = text;
			int length = "▶".Length;
			filterText = text2.Substring(length, text2.Length - length).TrimStart();
		}
		FilterText = filterText;
	}

	[RelayCommand]
	private void ToggleLastWord()
	{
		string text = FilterText ?? string.Empty;
		string filterText;
		if (!text.EndsWith("◀", StringComparison.Ordinal))
		{
			filterText = text.TrimEnd() + " ◀";
		}
		else
		{
			string text2 = text;
			int length = "◀".Length;
			filterText = text2.Substring(0, text2.Length - length).TrimEnd();
		}
		FilterText = filterText;
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

	[RelayCommand]
	private async Task UseHistoryEntry(string? entry)
	{
		if (!string.IsNullOrWhiteSpace(entry))
		{
			FilterText = entry;
			await RunContentSearchAsync();
		}
	}

	[RelayCommand]
	private void ToggleFavorite(Book? book)
	{
		Book book2 = book ?? SelectedBook;
		if ((object)book2 == null || string.IsNullOrEmpty(book2.FileID))
		{
			return;
		}
		string fileID = book2.FileID;
		if (AllBooks.Count > 0)
		{
			LoadFavorites();
		}
		FavoriteLocation? favoriteLocation = FindFavoriteInTree(fileID);
		if (favoriteLocation.HasValue)
		{
			_favoritesRepo.RemoveAll(fileID);
			if (string.Equals(SelectedBook?.FileID, fileID, StringComparison.Ordinal))
			{
				IsCurrentBookFavorited = false;
			}
			favoriteLocation.Value.Container.Remove(favoriteLocation.Value.Entry);
		}
		else
		{
			_favoritesRepo.Add(fileID);
			if (string.Equals(SelectedBook?.FileID, fileID, StringComparison.Ordinal))
			{
				IsCurrentBookFavorited = true;
			}
			RootFavorites.Add(new FavoriteBookEntry(book2, string.Empty));
		}
		FavoritesRevision++;
	}

	public bool IsFavorited(string? fileId)
	{
		if (string.IsNullOrEmpty(fileId))
		{
			return false;
		}
		try
		{
			return _favoritesRepo.IsFavorited(fileId);
		}
		catch
		{
			return false;
		}
	}

	[RelayCommand]
	private void OpenFavorite(FavoriteBookEntry? entry)
	{
		if ((object)entry?.Book != null)
		{
			if (IsContentMode)
			{
				ExitContentMode();
			}
			SelectedBook = entry.Book;
			OpenSelected();
		}
	}

	[RelayCommand]
	private void RemoveFavorite(FavoriteBookEntry? entry)
	{
		if (entry != null && !string.IsNullOrEmpty(entry.FileID))
		{
			_favoritesRepo.RemoveAll(entry.FileID);
			FavoriteLocation? favoriteLocation = FindFavoriteInTree(entry.FileID);
			if (favoriteLocation.HasValue)
			{
				favoriteLocation.Value.Container.Remove(favoriteLocation.Value.Entry);
			}
			if (string.Equals(SelectedBook?.FileID, entry.FileID, StringComparison.Ordinal))
			{
				IsCurrentBookFavorited = false;
			}
			FavoritesRevision++;
		}
	}

	[RelayCommand]
	private void CreateFolder(string? name)
	{
		string trimmed = (name ?? string.Empty).Trim();
		if (!string.IsNullOrEmpty(trimmed))
		{
			_favoritesRepo.CreateFolder(trimmed);
			if (!FavoriteFolderNames.Contains(trimmed))
			{
				FavoriteFolderNames.Add(trimmed);
			}
			if (!FavoriteFolderTree.Any((FavoriteFolderViewModel f) => string.Equals(f.Name, trimmed, StringComparison.Ordinal)))
			{
				FavoriteFolderTree.Add(new FavoriteFolderViewModel
				{
					Name = trimmed
				});
			}
		}
	}

	[RelayCommand]
	private void DeleteFolder(string? name)
	{
		if (string.IsNullOrEmpty(name))
		{
			return;
		}
		_favoritesRepo.DeleteFolder(name);
		FavoriteFolderNames.Remove(name);
		FavoriteFolderViewModel favoriteFolderViewModel = FavoriteFolderTree.FirstOrDefault((FavoriteFolderViewModel f) => string.Equals(f.Name, name, StringComparison.Ordinal));
		if (favoriteFolderViewModel == null)
		{
			return;
		}
		foreach (FavoriteBookEntry item in favoriteFolderViewModel.Books.ToList())
		{
			favoriteFolderViewModel.Books.Remove(item);
			item.FolderName = string.Empty;
			RootFavorites.Add(item);
		}
		FavoriteFolderTree.Remove(favoriteFolderViewModel);
	}

	[RelayCommand]
	private void MoveBookToFolder(MoveFavoriteRequest? request)
	{
		if (request?.Entry == null || string.IsNullOrEmpty(request.Entry.FileID))
		{
			return;
		}
		string text = request.NewFolder ?? string.Empty;
		if (!string.Equals(request.Entry.FolderName, text, StringComparison.Ordinal))
		{
			_favoritesRepo.MoveBookToFolder(request.Entry.FileID, text);
			FavoriteLocation? favoriteLocation = FindFavoriteInTree(request.Entry.FileID);
			if (favoriteLocation.HasValue)
			{
				favoriteLocation.Value.Container.Remove(favoriteLocation.Value.Entry);
			}
			request.Entry.FolderName = text;
			if (string.IsNullOrEmpty(text))
			{
				RootFavorites.Add(request.Entry);
			}
			else
			{
				GetOrCreateFolderVM(text).Books.Add(request.Entry);
			}
		}
	}

	[RelayCommand]
	private void ToggleFavoritesPanel()
	{
		ShowFavoritesPanel = !ShowFavoritesPanel;
	}

	[RelayCommand]
	private void ClearNavigationHistory()
	{
		_navigation.Clear();
	}

	private FavoriteFolderViewModel GetOrCreateFolderVM(string folderName)
	{
		FavoriteFolderViewModel favoriteFolderViewModel = FavoriteFolderTree.FirstOrDefault((FavoriteFolderViewModel f) => string.Equals(f.Name, folderName, StringComparison.Ordinal));
		if (favoriteFolderViewModel != null)
		{
			return favoriteFolderViewModel;
		}
		FavoriteFolderViewModel favoriteFolderViewModel2 = new FavoriteFolderViewModel
		{
			Name = (folderName ?? string.Empty)
		};
		FavoriteFolderTree.Add(favoriteFolderViewModel2);
		return favoriteFolderViewModel2;
	}

	private FavoriteLocation? FindFavoriteInTree(string fileId)
	{
		if (string.IsNullOrEmpty(fileId))
		{
			return null;
		}
		foreach (FavoriteBookEntry rootFavorite in RootFavorites)
		{
			if (string.Equals(rootFavorite.FileID, fileId, StringComparison.Ordinal))
			{
				return new FavoriteLocation(RootFavorites, rootFavorite);
			}
		}
		foreach (FavoriteFolderViewModel item in FavoriteFolderTree)
		{
			foreach (FavoriteBookEntry book in item.Books)
			{
				if (string.Equals(book.FileID, fileId, StringComparison.Ordinal))
				{
					return new FavoriteLocation(item.Books, book);
				}
			}
		}
		return null;
	}

	private void LoadFavorites()
	{
		if (_favoritesResolvedCount == AllBooks.Count)
		{
			return;
		}
		_favoritesResolvedCount = AllBooks.Count;
		Stopwatch stopwatch = Stopwatch.StartNew();
		try
		{
			FavoriteFolderTree.Clear();
			FavoriteFolderNames.Clear();
			RootFavorites.Clear();
			foreach (string folder in _favoritesRepo.GetFolders())
			{
				FavoriteFolderNames.Add(folder);
				FavoriteFolderTree.Add(new FavoriteFolderViewModel
				{
					Name = folder
				});
			}
			IReadOnlyList<FavoriteEntry> all = _favoritesRepo.GetAll();
			int num = 0;
			foreach (FavoriteEntry entry in all)
			{
				Book book = AllBooks.FirstOrDefault((Book b) => string.Equals(b.FileID, entry.FileID, StringComparison.Ordinal));
				if ((object)book == null)
				{
					continue;
				}
				num++;
				string text = entry.FolderName ?? string.Empty;
				if (string.IsNullOrEmpty(text))
				{
					if (!RootFavorites.Any((FavoriteBookEntry e) => string.Equals(e.FileID, book.FileID, StringComparison.Ordinal)))
					{
						RootFavorites.Add(new FavoriteBookEntry(book, string.Empty));
					}
					continue;
				}
				FavoriteFolderViewModel orCreateFolderVM = GetOrCreateFolderVM(text);
				if (!orCreateFolderVM.Books.Any((FavoriteBookEntry e) => string.Equals(e.FileID, book.FileID, StringComparison.Ordinal)))
				{
					orCreateFolderVM.Books.Add(new FavoriteBookEntry(book, text));
				}
			}
			Log.Information("Favorites: resolved {Resolved}/{Count} entries against {Books} books, {Folders} folders ({Ms}ms)", num, all.Count, AllBooks.Count, FavoriteFolderTree.Count, stopwatch.ElapsedMilliseconds);
		}
		catch (Exception exception)
		{
			Log.Warning(exception, "Favorites: load failed — leaving FavoriteFolderTree empty");
		}
	}

	private async void DebouncedOpenRow()
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
				await OpenSelectedRowAsync();
			}
		}
		catch (Exception ex2)
		{
			StatusText = SharedStrings.S2169 + ex2.Message;
		}
	}

	private async Task OpenSelectedRowAsync()
	{
		SearchResultRow row = SelectedRow;
		if ((object)row == null || !IsContentMode)
		{
			return;
		}
		if (string.IsNullOrEmpty(row.Book.FileID))
		{
			StatusText = SharedStrings.ErrorMissingFileId;
		}
		else
		{
			if (string.Equals(_contentOpenFileId, row.Book.FileID, StringComparison.Ordinal))
			{
				return;
			}
			_contentOpenFileId = row.Book.FileID;
			string q = EffectiveContentQuery();
			SelectedBook = row.Book;
			bool hasInBookQuery = !string.Equals(row.Book.SourceType, "Text", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(q);
			if (hasInBookQuery)
			{
				InBookSearchText = q;
				string? text = RemoteSearchUrl();
				string text2 = ((text == null) ? OnlineServiceUrl() : null);
				if (text != null || (text2 != null && string.IsNullOrEmpty(row.Book.RelativePath) && int.TryParse(row.Book.FileID, out var _)))
				{
					SeedProvisionalInBookHits(row, q);
					OpenSelected(preserveInBookState: true);
					if (ActiveTab != null)
					{
						ActiveTab.InBookQuery = q;
						ActiveTab.ContentSearch = CaptureContentSearch();
						ActiveTab.InBookHits = CaptureInBookHits();
					}
					FinishInBookInBackgroundAsync(row, ActiveTab);
					return;
				}
				await SearchInBookAsync();
				if (!string.Equals(SelectedRow?.Book.FileID, row.Book.FileID, StringComparison.Ordinal))
				{
					return;
				}
			}
			OpenSelected(hasInBookQuery);
			if (ActiveTab != null)
			{
				ActiveTab.InBookQuery = (hasInBookQuery ? q : "");
				ActiveTab.ContentSearch = CaptureContentSearch();
				ActiveTab.InBookHits = CaptureInBookHits();
			}
		}
	}

	private void SeedProvisionalInBookHits(SearchResultRow row, string q)
	{
		int? pageNumber = row.PageNumber;
		int num;
		if (pageNumber.HasValue)
		{
			int valueOrDefault = pageNumber.GetValueOrDefault();
			if (valueOrDefault > 0)
			{
				num = valueOrDefault;
				goto IL_0021;
			}
		}
		num = 1;
		goto IL_0021;
		IL_0021:
		int num2 = num;
		IReadOnlyList<string> matchedTerms = QueryBuilder.ExtractHighlightTerms(q, addPrefixes: false, expandRoots: false, expandNumberGender: false, expandGematria: false, expandSpelling: false, null, expandRashiOcr: false, dropPhraseConstituents: true);
		string highlightXml = $"<loc pg=\"{Math.Max(0, num2 - 1)}\" pos=\"0\" len=\"1\"></loc>";
		InBookHitPages.Clear();
		InBookHitPages.Add(num2);
		OnPropertyChanged("InBookHitPages");
		MarkedPdfPath = null;
		CurrentBookHits = new InBookHitInfo(row.HitCount, new int[1] { num2 }, matchedTerms, highlightXml);
		CurrentHitIndex = 0;
		CurrentPage = num2;
		SelectedHitPage = num2;
		UpdateMatchPosition();
		_main.SetOpenBookTitle(row.Book.BookName, InBookSearchText);
		InBookSearchStatus = SharedStrings.S775;
	}

	private async Task FinishInBookInBackgroundAsync(SearchResultRow row, OpenBookTab? tab)
	{
		await SearchInBookAsync();
		if (tab != null && string.Equals(SelectedRow?.Book.FileID, row.Book.FileID, StringComparison.Ordinal))
		{
			tab.InBookHits = CaptureInBookHits();
		}
	}

	public async Task OpenAndPinSelectedRowAsync()
	{
		_openDebounce?.Cancel();
		await OpenSelectedRowAsync();
		if (ActiveTab != null)
		{
			ActiveTab.IsPreview = false;
		}
	}

	private void RebuildCatalogRows(bool autoExpand)
	{
		IReadOnlyList<CatalogRow> readOnlyList = BookGrouping.BuildTopLevel(FilteredBooks ?? Array.Empty<Book>(), HebrewCollation.Compare);
		foreach (CatalogRow item in readOnlyList)
		{
			if (item is GroupHeaderRow groupHeaderRow)
			{
				groupHeaderRow.IsExpanded = autoExpand;
			}
		}
		_topLevelRows = readOnlyList;
		CatalogRows.ReplaceAll(BookGrouping.Flatten(readOnlyList));
	}

	private void SyncRowToSelectedBook(Book? book)
	{
		if (_syncingSelection)
		{
			return;
		}
		CatalogRow selectedCatalogRow = (((object)book == null) ? null : FindRowForBook(book));
		_syncingSelection = true;
		try
		{
			SelectedCatalogRow = selectedCatalogRow;
		}
		finally
		{
			_syncingSelection = false;
		}
	}

	private CatalogRow? FindRowForBook(Book book)
	{
		foreach (CatalogRow catalogRow in CatalogRows)
		{
			if (catalogRow is BookRow bookRow && (object)bookRow.Book == book)
			{
				return bookRow;
			}
		}
		foreach (CatalogRow topLevelRow in _topLevelRows)
		{
			if (!(topLevelRow is GroupHeaderRow groupHeaderRow))
			{
				continue;
			}
			foreach (BookRow child in groupHeaderRow.Children)
			{
				if ((object)child.Book == book)
				{
					EnsureExpanded(groupHeaderRow);
					return child;
				}
			}
		}
		return null;
	}

	[RelayCommand]
	private void ToggleGroup(GroupHeaderRow? header)
	{
		if (header == null)
		{
			return;
		}
		int num = CatalogRows.IndexOf(header);
		if (num >= 0)
		{
			if (header.IsExpanded)
			{
				header.IsExpanded = false;
				CatalogRows.RemoveRange(num + 1, header.Children.Count);
			}
			else
			{
				header.IsExpanded = true;
				CatalogRows.InsertRange(num + 1, header.Children);
			}
		}
	}

	private void EnsureExpanded(GroupHeaderRow header)
	{
		if (!header.IsExpanded)
		{
			int num = CatalogRows.IndexOf(header);
			if (num >= 0)
			{
				header.IsExpanded = true;
				CatalogRows.InsertRange(num + 1, header.Children);
			}
		}
	}

	public void OnHeaderActivated(GroupHeaderRow header)
	{
		bool num = !header.IsExpanded;
		ToggleGroup(header);
		if (num)
		{
			if (header.Children.Count > 0)
			{
				SelectedCatalogRow = header.Children[0];
			}
			return;
		}
		_syncingSelection = true;
		try
		{
			SelectedCatalogRow = header;
		}
		finally
		{
			_syncingSelection = false;
		}
	}

	private async void DebouncedOpen()
	{
		try
		{
			_openDebounce?.Cancel();
			_openDebounce = new CancellationTokenSource();
			CancellationToken ct = _openDebounce.Token;
			try
			{
				await Task.Delay(250, ct);
			}
			catch (OperationCanceledException)
			{
				return;
			}
			if (!ct.IsCancellationRequested)
			{
				OpenSelected();
			}
		}
		catch (Exception ex2)
		{
			StatusText = SharedStrings.S2170 + ex2.Message;
		}
	}

	private void OnScopeChanged(object? sender, EventArgs e)
	{
		MarkRevision++;
		OnPropertyChanged("MarkedCount");
		OnPropertyChanged("HasMarkedBooks");
	}

	public bool IsMarked(string? fileId)
	{
		return _scope.IsMarked(fileId ?? string.Empty);
	}

	[RelayCommand]
	private void ToggleMark(Book? book)
	{
		if (!string.IsNullOrEmpty(book?.FileID))
		{
			_scope.SetMarked(book.FileID, !_scope.IsMarked(book.FileID));
		}
	}

	[RelayCommand]
	private void ToggleGroupMark(GroupHeaderRow? header)
	{
		if (header == null)
		{
			return;
		}
		List<string> list = (from c in header.Children
			select c.Book.FileID into f
			where !string.IsNullOrEmpty(f)
			select (f)).ToList();
		if (list.Count == 0)
		{
			return;
		}
		if (list.All(_scope.IsMarked))
		{
			foreach (string item in list)
			{
				_scope.SetMarked(item, marked: false);
			}
			return;
		}
		_scope.MarkAll(list);
	}

	[RelayCommand]
	private void MarkAllDisplayed()
	{
		IReadOnlyList<Book> filteredBooks = FilteredBooks;
		if (filteredBooks != null && filteredBooks.Count > 0)
		{
			_scope.MarkAll(from b in filteredBooks
				where !string.IsNullOrEmpty(b.FileID)
				select b.FileID);
		}
	}

	[RelayCommand]
	private void ClearMarks()
	{
		_scope.ClearMarks();
	}

	private void ApplyFilter()
	{
		string f = EffectiveCatalogFilter();
		IEnumerable<Book> enumerable = BrowseSource();
		bool shelfActive = (object)SelectedMadaf != null && SelectedMadaf.MadafID >= 0;
		string shelfName = SelectedMadaf?.Name;
		IReadOnlyList<SortLayer> sortLayers = Sorting.Layers;
		if (string.IsNullOrEmpty(f))
		{
			_filterCts?.Cancel();
			FilteredBooks = CatalogSorting.Apply(enumerable, sortLayers).ToList();
			StatusText = (shelfActive ? $"{SharedStrings.S2171}{FilteredBooks.Count}{SharedStrings.S2172}{shelfName}'" : string.Format(SharedStrings.StatusFoundResults, FilteredBooks.Count));
			return;
		}
		_filterCts?.Cancel();
		CancellationToken ct = (_filterCts = new CancellationTokenSource()).Token;
		List<Book> snapshot = enumerable.ToList();
		int total = TotalCount;
		Task.Run(delegate
		{
			string item = string.Empty;
			List<Book> list2;
			if (HebrewKeyboard.LooksMistyped(f))
			{
				string text = HebrewKeyboard.ToHebrew(f);
				List<Book> list = RankAndOrderBooks(snapshot, f);
				List<Book> source = RankAndOrderBooks(snapshot, text);
				HashSet<int> seen = new HashSet<int>(list.Select((Book b) => b.ID));
				list2 = list.Concat(source.Where((Book b) => seen.Add(b.ID))).ToList();
				item = ((list.Count > 0) ? $"  ·  {list.Count}{SharedStrings.S2173}{text}\"" : (SharedStrings.S2174 + text + "\""));
			}
			else
			{
				list2 = RankAndOrderBooks(snapshot, f);
			}
			if (sortLayers.Count > 0)
			{
				list2 = CatalogSorting.Apply(list2, sortLayers).ToList();
			}
			return (ranked: list2, kbNote: item);
		}, ct).ContinueWith(delegate(Task<(List<Book> ranked, string kbNote)> t)
		{
			if (!ct.IsCancellationRequested && t.IsCompletedSuccessfully)
			{
				var (list, text) = t.Result;
				FilteredBooks = list;
				StatusText = (shelfActive ? $"{SharedStrings.S2175}{list.Count}{SharedStrings.S2176}{shelfName}'" : $"{SharedStrings.S2177}{list.Count}{SharedStrings.S2178}{total}") + text;
			}
		}, ct, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.FromCurrentSynchronizationContext());
	}

	private string EffectiveCatalogFilter()
	{
		string text = (FilterText ?? "").Trim();
		int num = text.IndexOf(':');
		if (num < 0)
		{
			return text;
		}
		return text.Substring(0, num).Trim();
	}

	private string EffectiveContentQuery()
	{
		if (_spellFixedContentQuery != null)
		{
			return _spellFixedContentQuery;
		}
		string text = (FilterText ?? "").Trim();
		int num = text.IndexOf(':');
		string text2;
		if (num < 0)
		{
			text2 = text;
		}
		else
		{
			string text3 = text;
			int num2 = num + 1;
			text2 = text3.Substring(num2, text3.Length - num2).Trim();
		}
		string text4 = text2;
		if (!HebrewKeyboard.LooksMistyped(text4))
		{
			return text4;
		}
		return HebrewKeyboard.ToHebrew(text4);
	}

	public string? ContentQueryForNewWindow()
	{
		if (!IsContentMode)
		{
			return null;
		}
		return EffectiveContentQuery();
	}

	public (string? Query, int Page) OpenTabStateFor(string? fileId)
	{
		if (string.IsNullOrEmpty(fileId))
		{
			return (Query: null, Page: 0);
		}
		OpenBookTab openBookTab = OpenTabs.FirstOrDefault((OpenBookTab t) => string.Equals(t.FileId, fileId, StringComparison.Ordinal));
		if (openBookTab == null)
		{
			return (Query: null, Page: 0);
		}
		int num = ((openBookTab == ActiveTab && CurrentPage > 0) ? CurrentPage : openBookTab.LastPage);
		return (Query: string.IsNullOrWhiteSpace(openBookTab.InBookQuery) ? null : openBookTab.InBookQuery, Page: (num > 0) ? num : 0);
	}

	private IEnumerable<Book> BrowseSource()
	{
		IEnumerable<Book> enumerable = AllBooks;
		if ((object)SelectedMadaf != null && SelectedMadaf.MadafID >= 0)
		{
			HashSet<int> allowed = new HashSet<int>(SelectedMadaf.BookIds);
			enumerable = AllBooks.Where((Book b) => allowed.Contains(b.ID));
		}
		if (Topics.SelectedSet.Count > 0)
		{
			enumerable = enumerable.Where((Book b) => CategoryFilter.MatchesAny(b, Topics.SelectedSet));
		}
		HashSet<string> selectedCorpora = (from o in CorpusFilters
			where o.Value != "All" && o.IsSelected
			select o.Value).ToHashSet<string>(StringComparer.Ordinal);
		if (selectedCorpora.Count < 3)
		{
			enumerable = enumerable.Where((Book b) => selectedCorpora.Contains(b.SourceType ?? "PDF"));
		}
		PrintYearRange years = Years.Range;
		if (years.IsActive)
		{
			enumerable = enumerable.Where((Book b) => years.Matches(b.PrintYear));
		}
		return enumerable;
	}

	private List<Book> RankAndOrderBooks(IEnumerable<Book> source, string f)
	{
		string qNorm = HebrewFuzzyMatch.Normalize(f);
		string qSkel = HebrewFuzzyMatch.Skeleton(qNorm);
		string[] qWords = ((qNorm.Length == 0) ? Array.Empty<string>() : qNorm.Split(' ', StringSplitOptions.RemoveEmptyEntries));
		bool fuzzy = qNorm.Length >= 2;
		Comparer<string> comparer = Comparer<string>.Create(HebrewCollation.Compare);
		return (from t in (from b in source
				select (Book: b, Rank: RankBook(b, f, qNorm, qSkel, qWords, fuzzy)) into t
				where t.Rank < int.MaxValue
				orderby t.Rank, _popularity.GetScore(t.Book.FileID) descending
				select t).ThenBy<(Book Book, int Rank), string>(((Book Book, int Rank) t) => t.Book.BookName, comparer)
			select t.Book).ToList();
	}

	private int RankBook(Book b, string f, string qNorm, string qSkel, string[] qWords, bool fuzzy)
	{
		string text = b.BookName ?? "";
		string text2 = b.AuthorName ?? "";
		if (qWords.Length >= 2)
		{
			CatalogFuzzyKeyCache.Fields fields = _fuzzyCache.For(b);
			if (!AllWordsPresent(qWords, fields.NameNorm, fields.AuthNorm))
			{
				return int.MaxValue;
			}
		}
		if (text.StartsWith(f, StringComparison.OrdinalIgnoreCase))
		{
			return 0;
		}
		if (text.Contains(f, StringComparison.OrdinalIgnoreCase))
		{
			return 1;
		}
		if (text2.StartsWith(f, StringComparison.OrdinalIgnoreCase))
		{
			return 2;
		}
		if (text2.Contains(f, StringComparison.OrdinalIgnoreCase))
		{
			return 3;
		}
		string value;
		string obj = (_descById.TryGetValue(b.ID, out value) ? value : b.Description);
		if (obj == null || !obj.Contains(f, StringComparison.OrdinalIgnoreCase))
		{
			string? printPlace = b.PrintPlace;
			if (printPlace == null || !printPlace.Contains(f, StringComparison.OrdinalIgnoreCase))
			{
				string? printYear = b.PrintYear;
				if (printYear == null || !printYear.Contains(f, StringComparison.OrdinalIgnoreCase))
				{
					string? fileID = b.FileID;
					if (fileID == null || !fileID.Contains(f, StringComparison.OrdinalIgnoreCase))
					{
						if (fuzzy)
						{
							CatalogFuzzyKeyCache.Fields fields2 = _fuzzyCache.For(b);
							if (fields2.NameNorm.Length > 0)
							{
								if (fields2.NameNorm.Contains(qNorm, StringComparison.Ordinal))
								{
									return 5;
								}
								if (fields2.NameSkel.Contains(qSkel, StringComparison.Ordinal))
								{
									return 6;
								}
								if (HebrewFuzzyMatch.AnyWordWithinDistance(qWords, fields2.NameNorm))
								{
									return 7;
								}
							}
							if (fields2.AuthNorm.Length > 0)
							{
								if (fields2.AuthNorm.Contains(qNorm, StringComparison.Ordinal))
								{
									return 8;
								}
								if (fields2.AuthSkel.Contains(qSkel, StringComparison.Ordinal))
								{
									return 9;
								}
								if (HebrewFuzzyMatch.AnyWordWithinDistance(qWords, fields2.AuthNorm))
								{
									return 10;
								}
							}
						}
						return int.MaxValue;
					}
				}
			}
		}
		return 4;
	}

	private static bool AllWordsPresent(string[] qWords, string nameNorm, string authNorm)
	{
		foreach (string text in qWords)
		{
			if (text.Length >= 2 && !nameNorm.Contains(text, StringComparison.Ordinal) && !authNorm.Contains(text, StringComparison.Ordinal) && (text.Length < 4 || (!HebrewFuzzyMatch.AnyWordWithinDistance(new string[1] { text }, nameNorm) && !HebrewFuzzyMatch.AnyWordWithinDistance(new string[1] { text }, authNorm))))
			{
				return false;
			}
		}
		return true;
	}

	[RelayCommand]
	private Task LoadAsync()
	{
		if (_catalogFullyLoaded && !_catalogStale)
		{
			return Task.CompletedTask;
		}
		_catalogStale = false;
		return ReloadAsync();
	}

	[RelayCommand]
	private Task RefreshAsync()
	{
		return ReloadAsync();
	}

	private async Task ReloadAsync()
	{
		if (_loadInFlight)
		{
			return;
		}
		_loadInFlight = true;
		try
		{
			IsBusy = true;
			_catalogFullyLoaded = false;
			Stopwatch reloadSw = Stopwatch.StartNew();
			TotalCount = await _catalog.CountAsync();
			Log.Information("TIMING: reload CountAsync done +{Ms}ms (total={Total})", reloadSw.ElapsedMilliseconds, TotalCount);
			IReadOnlyList<Book> readOnlyList = await _catalog.ListAsync(0, 150, "BookName", default(CancellationToken), includeDescription: false);
			AllBooks.Clear();
			AllBooks.AddRange(readOnlyList);
			RecomputeHasMultipleSources();
			ApplyFilter();
			Log.Information("TIMING: reload phase1 grid populated +{Ms}ms (rows={Rows})", reloadSw.ElapsedMilliseconds, readOnlyList.Count);
			IsBusy = false;
			IReadOnlyList<Book> readOnlyList2 = await _catalog.ListAsync(0, 200000, "ID", default(CancellationToken), includeDescription: false);
			Log.Information("TIMING: reload phase2 full list fetched +{Ms}ms (rows={Rows})", reloadSw.ElapsedMilliseconds, readOnlyList2.Count);
			Func<string?, string?, int> hebCmp = HebrewCollation.Compare;
			List<Book> list = readOnlyList2.ToList();
			list.Sort((Book x, Book y) => hebCmp(x.BookName, y.BookName));
			Log.Information("TIMING: reload phase2 in-memory HEB sort done +{Ms}ms", reloadSw.ElapsedMilliseconds);
			AllBooks.Clear();
			AllBooks.AddRange(list);
			RecomputeHasMultipleSources();
			_catalogFullyLoaded = true;
			_fuzzyCache.WarmAsync(list);
			int? num = SelectedBook?.ID;
			if (num.HasValue)
			{
				int kid = num.GetValueOrDefault();
				Book book = AllBooks.FirstOrDefault((Book b) => b.ID == kid);
				if ((object)book != null && (object)book != SelectedBook)
				{
					SelectedBook = book;
				}
			}
			ApplyFilter();
			Log.Information("TIMING: reload phase2 applied +{Ms}ms", reloadSw.ElapsedMilliseconds);
			try
			{
				IReadOnlyList<MadafNode> obj = await _madafs.GetTreeAsync();
				MadafNodes.Clear();
				MadafNodes.Add(AllBooksNode);
				foreach (MadafNode item in obj)
				{
					MadafNodes.Add(item);
				}
				SelectedMadaf = AllBooksNode;
			}
			catch
			{
			}
			await LoadShelfTreeAsync();
			try
			{
				IReadOnlyList<string> all = await _catalogRepo.GetDistinctCategoriesAsync();
				Topics.Initialize(all);
			}
			catch
			{
			}
			Log.Information("TIMING: reload topics done (full load complete) +{Ms}ms", reloadSw.ElapsedMilliseconds);
			Task.Run(async delegate
			{
				try
				{
					IReadOnlyList<Book> readOnlyList3 = await _catalog.ListAsync(0, 200000, "ID");
					Dictionary<int, string?> map = new Dictionary<int, string>(readOnlyList3.Count);
					foreach (Book item2 in readOnlyList3)
					{
						map[item2.ID] = item2.Description;
					}
					Application.Current?.Dispatcher.Invoke(delegate
					{
						_descById = map;
						if (!string.IsNullOrEmpty(FilterText))
						{
							ApplyFilter();
						}
					});
					Log.Information("TIMING: reload phase3 descriptions loaded ({Count})", map.Count);
				}
				catch (Exception exception)
				{
					Log.Warning(exception, "Phase 3 description load failed (rank-4 description filter stays disabled)");
				}
			});
		}
		catch (Exception ex)
		{
			StatusText = ex.Message;
		}
		finally
		{
			IsBusy = false;
			_loadInFlight = false;
		}
	}

	private void RecomputeHasMultipleSources()
	{
		HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
		foreach (Book allBook in AllBooks)
		{
			hashSet.Add(string.IsNullOrEmpty(allBook.SourceType) ? "PDF" : allBook.SourceType);
		}
		HasMultipleSources = hashSet.Count > 1;
	}

	[RelayCommand]
	private async Task AddBookAsync()
	{
		if (((AddBookWindow)App.Services.GetService(typeof(AddBookWindow))).ShowDialog() == true)
		{
			SelectedBook = null;
			await ReloadAsync();
		}
	}

	[RelayCommand]
	private async Task EditSelectedAsync()
	{
		if ((object)SelectedBook == null)
		{
			return;
		}
		int editedId = SelectedBook.ID;
		EditBookWindow obj = (EditBookWindow)App.Services.GetService(typeof(EditBookWindow));
		obj.LoadBook(SelectedBook);
		if (obj.ShowDialog() == true)
		{
			SelectedBook = null;
			await ReloadAsync();
			SelectedBook = AllBooks.FirstOrDefault((Book b) => b.ID == editedId);
		}
	}

	[RelayCommand]
	private async Task DeleteSelectedAsync()
	{
		if ((object)SelectedBook != null)
		{
			await _catalog.DeleteAsync(SelectedBook.ID);
			SelectedBook = null;
			await ReloadAsync();
		}
	}

	[RelayCommand]
	private async Task EditTocAsync()
	{
		if ((object)SelectedBook != null)
		{
			await ((TocEditorWindow)App.Services.GetService(typeof(TocEditorWindow))).EditAsync(SelectedBook.ID, SelectedBook.BookName ?? "", SelectedBook.FileID, SelectedBook.SourceType);
		}
	}

	private void TrackOpenBook(Book book, bool pin)
	{
		_autoSelectFirstDone = true;
		OpenBookTab openBookTab = OpenTabs.FirstOrDefault((OpenBookTab t) => string.Equals(t.FileId, book.FileID, StringComparison.Ordinal));
		if (openBookTab != null)
		{
			openBookTab.SetBook(book);
			if (pin)
			{
				openBookTab.IsPreview = false;
			}
			SetActiveTab(openBookTab);
			return;
		}
		if (!pin)
		{
			OpenBookTab openBookTab2 = OpenTabs.FirstOrDefault((OpenBookTab t) => t.IsPreview);
			if (openBookTab2 != null)
			{
				openBookTab2.SetBook(book);
				SetActiveTab(openBookTab2);
				return;
			}
		}
		OpenBookTab openBookTab3 = new OpenBookTab(book)
		{
			IsPreview = !pin
		};
		OpenTabs.Add(openBookTab3);
		OnPropertyChanged("HasTabs");
		SetActiveTab(openBookTab3);
	}

	public void PinBook(Book book)
	{
		TrackOpenBook(book, pin: true);
		OpenBookInline(book);
	}

	private void SetActiveTab(OpenBookTab? tab)
	{
		foreach (OpenBookTab openTab in OpenTabs)
		{
			openTab.IsActive = openTab == tab;
		}
		ActiveTab = tab;
	}

	private TabContentSearch? CaptureContentSearch()
	{
		if (!IsContentMode)
		{
			return null;
		}
		return new TabContentSearch
		{
			Results = new List<SearchResultRow>(Results),
			SelectedResultFileId = SelectedRow?.Book?.FileID,
			QueryText = (FilterText ?? string.Empty),
			ResultFilterText = (ResultFilterText ?? string.Empty),
			ActiveChipsSummary = (ActiveChipsSummary ?? string.Empty),
			ContentOpenFileId = _contentOpenFileId,
			StatusText = (StatusText ?? string.Empty)
		};
	}

	private void RestoreContentSearch(TabContentSearch? snap)
	{
		bool suppressNavigationRecord = _suppressNavigationRecord;
		_restoringTabContent = true;
		_syncingSelection = true;
		_suppressNavigationRecord = true;
		try
		{
			if (snap == null)
			{
				if (IsContentMode)
				{
					_contentCts?.Cancel();
					FilterText = string.Empty;
					IsContentMode = false;
					IsSearching = false;
					Results.Clear();
					SelectedRow = null;
					_contentOpenFileId = null;
					ResultFilterText = string.Empty;
					ActiveChipsSummary = string.Empty;
				}
			}
			else
			{
				IsContentMode = true;
				IsSearching = false;
				FilterText = snap.QueryText;
				Results.ReplaceAll(snap.Results);
				_contentOpenFileId = snap.ContentOpenFileId;
				ResultFilterText = snap.ResultFilterText;
				ActiveChipsSummary = snap.ActiveChipsSummary;
				StatusText = snap.StatusText;
				SelectedRow = ((!string.IsNullOrEmpty(snap.SelectedResultFileId)) ? Results.FirstOrDefault((SearchResultRow r) => string.Equals(r.Book?.FileID, snap.SelectedResultFileId, StringComparison.Ordinal)) : null);
			}
		}
		finally
		{
			_restoringTabContent = false;
			_syncingSelection = false;
			_suppressNavigationRecord = suppressNavigationRecord;
		}
	}

	private TabInBookHits? CaptureInBookHits()
	{
		if ((object)CurrentBookHits == null)
		{
			return null;
		}
		return new TabInBookHits
		{
			Hits = CurrentBookHits,
			Pages = new List<int>(InBookHitPages),
			HitIndex = CurrentHitIndex,
			SelectedHitPage = SelectedHitPage
		};
	}

	private void RestoreInBookHits(TabInBookHits? snap)
	{
		_syncingSelectionFromViewer = true;
		try
		{
			InBookHitPages.Clear();
			if (snap == null)
			{
				CurrentBookHits = null;
				CurrentHitIndex = -1;
				SelectedHitPage = null;
			}
			else
			{
				foreach (int page in snap.Pages)
				{
					InBookHitPages.Add(page);
				}
				CurrentBookHits = snap.Hits;
				CurrentHitIndex = snap.HitIndex;
				SelectedHitPage = snap.SelectedHitPage;
			}
			UpdateMatchPosition();
		}
		finally
		{
			_syncingSelectionFromViewer = false;
		}
	}

	[RelayCommand]
	private void ActivateTab(OpenBookTab? tab)
	{
		if (tab != null)
		{
			bool num = tab != ActiveTab;
			if (num && ActiveTab != null)
			{
				ActiveTab.ContentSearch = CaptureContentSearch();
				ActiveTab.InBookHits = CaptureInBookHits();
			}
			SetActiveTab(tab);
			if (num)
			{
				RestoreContentSearch(tab.ContentSearch);
				RestoreInBookHits(tab.InBookHits);
			}
			_openBookFileId = tab.Book.FileID;
			CurrentBookTitle = tab.Book.BookName ?? "";
			IsTextMode = string.Equals(tab.Book.SourceType, "Text", StringComparison.Ordinal);
			HasOpenBook = true;
			InBookSearchText = tab.InBookQuery;
			_main.SetOpenBookTitle(tab.Book.BookName);
		}
	}

	[RelayCommand]
	private void CloseTab(OpenBookTab? tab)
	{
		if (tab == null)
		{
			return;
		}
		int num = OpenTabs.IndexOf(tab);
		if (num < 0)
		{
			return;
		}
		bool num2 = tab == ActiveTab;
		OpenTabs.Remove(tab);
		OnPropertyChanged("HasTabs");
		if (num2)
		{
			if (OpenTabs.Count == 0)
			{
				SetActiveTab(null);
				OpenBookInline(null);
			}
			else
			{
				ActivateTab(OpenTabs[Math.Min(num, OpenTabs.Count - 1)]);
			}
		}
	}

	[RelayCommand]
	private void CloseAllTabs()
	{
		if (OpenTabs.Count != 0)
		{
			for (int num = OpenTabs.Count - 1; num >= 0; num--)
			{
				OpenTabs.RemoveAt(num);
			}
			OnPropertyChanged("HasTabs");
			SetActiveTab(null);
			OpenBookInline(null);
		}
	}

	[RelayCommand]
	private void CloseOtherTabs(OpenBookTab? keep)
	{
		if (keep == null || !OpenTabs.Contains(keep))
		{
			return;
		}
		for (int num = OpenTabs.Count - 1; num >= 0; num--)
		{
			if (OpenTabs[num] != keep)
			{
				OpenTabs.RemoveAt(num);
			}
		}
		OnPropertyChanged("HasTabs");
		if (ActiveTab != keep)
		{
			ActivateTab(keep);
		}
	}

	[RelayCommand]
	private void CloseTabsToRight(OpenBookTab? tab)
	{
		if (tab == null)
		{
			return;
		}
		int num = OpenTabs.IndexOf(tab);
		if (num > 0)
		{
			int num2 = ((ActiveTab == null) ? (-1) : OpenTabs.IndexOf(ActiveTab));
			bool flag = num2 >= 0 && num2 < num;
			for (int num3 = num - 1; num3 >= 0; num3--)
			{
				OpenTabs.RemoveAt(num3);
			}
			OnPropertyChanged("HasTabs");
			if (flag)
			{
				ActivateTab(tab);
			}
		}
	}

	public void MoveTab(int from, int to)
	{
		if (from != to && from >= 0 && to >= 0 && from < OpenTabs.Count && to < OpenTabs.Count)
		{
			OpenTabs.Move(from, to);
		}
	}

	public void ActivateAdjacentTab(int dir)
	{
		int count = OpenTabs.Count;
		if (count != 0)
		{
			int num = ((ActiveTab == null) ? (-1) : OpenTabs.IndexOf(ActiveTab));
			ActivateTab(OpenTabs[((num + dir) % count + count) % count]);
		}
	}

	public void CloseActiveTab()
	{
		if (ActiveTab != null)
		{
			CloseTab(ActiveTab);
		}
	}

	public OpenTabsPersistence.SavedTabs SnapshotOpenTabs()
	{
		if (ActiveTab != null)
		{
			if (CurrentPage > 0)
			{
				ActiveTab.LastPage = CurrentPage;
			}
			if (!IsTextMode && (object)CurrentBookHits != null && !string.IsNullOrWhiteSpace(InBookSearchText))
			{
				ActiveTab.InBookQuery = InBookSearchText;
			}
		}
		return new OpenTabsPersistence.SavedTabs((from t in OpenTabs
			where !string.IsNullOrEmpty(t.FileId)
			select new OpenTabsPersistence.SavedTab(t.FileId, !t.IsPreview, t.LastPage, string.IsNullOrWhiteSpace(t.InBookQuery) ? null : t.InBookQuery)).ToList(), ActiveTab?.FileId);
	}

	public bool RestoreOpenTabs()
	{
		if (App.IsProtectMode)
		{
			_tabsRestored = true;
			return false;
		}
		if (!_sessionLoaded)
		{
			_sessionCache = OpenTabsPersistence.LoadSession();
			_sessionLoaded = true;
		}
		if (_tabsRestored || !_catalogFullyLoaded)
		{
			return false;
		}
		_tabsRestored = true;
		if ((object)_sessionCache == null || _sessionCache.Windows.Count == 0)
		{
			return false;
		}
		RestoreTabsAsync(_sessionCache.Windows[0]);
		if (_sessionCache.Windows.Count > 1)
		{
			SpawnRestoreWindows?.Invoke(_sessionCache.Windows.Skip(1).ToList());
		}
		return true;
	}

	public void SetPendingTearOff(OpenTabsPersistence.SavedTabs saved)
	{
		_pendingTearOff = saved;
		_tabsRestored = true;
	}

	public bool RestoreTornOffTab()
	{
		if (_tearOffDone || (object)_pendingTearOff == null || !_catalogFullyLoaded)
		{
			return false;
		}
		OpenTabsPersistence.SavedTabs pendingTearOff = _pendingTearOff;
		_pendingTearOff = null;
		_tearOffDone = true;
		RestoreTabsAsync(pendingTearOff);
		return true;
	}

	public OpenTabsPersistence.SavedTabs BuildTearOffPayload(OpenBookTab tab, int? livePage = null)
	{
		int page = ((livePage.HasValue && livePage.GetValueOrDefault() > 0) ? livePage.Value : ((tab == ActiveTab && CurrentPage > 0) ? CurrentPage : tab.LastPage));
		string text = ((tab == ActiveTab && !IsTextMode && (object)CurrentBookHits != null && !string.IsNullOrWhiteSpace(InBookSearchText)) ? InBookSearchText : tab.InBookQuery);
		OpenTabsPersistence.SavedTab savedTab = new OpenTabsPersistence.SavedTab(tab.FileId, !tab.IsPreview, page, string.IsNullOrWhiteSpace(text) ? null : text);
		return new OpenTabsPersistence.SavedTabs(new OpenTabsPersistence.SavedTab[1] { savedTab }, tab.FileId);
	}

	public bool CanImportBook(string fileId)
	{
		if (!OpenTabs.Any((OpenBookTab t) => string.Equals(t.FileId, fileId, StringComparison.Ordinal)))
		{
			return AllBooks.Any((Book b) => string.Equals(b.FileID, fileId, StringComparison.Ordinal));
		}
		return true;
	}

	public async Task ImportTabAsync(string fileId, int page, string query, bool pinned, int insertIndex)
	{
		OpenBookTab openBookTab = OpenTabs.FirstOrDefault((OpenBookTab t) => string.Equals(t.FileId, fileId, StringComparison.Ordinal));
		if (openBookTab == null)
		{
			Book book = AllBooks.FirstOrDefault((Book b) => string.Equals(b.FileID, fileId, StringComparison.Ordinal));
			if ((object)book == null)
			{
				return;
			}
			await OpenBookForRestoreAsync(book, page, query);
		}
		else
		{
			ActivateTab(openBookTab);
		}
		OpenBookTab openBookTab2 = OpenTabs.FirstOrDefault((OpenBookTab t) => string.Equals(t.FileId, fileId, StringComparison.Ordinal));
		if (openBookTab2 != null)
		{
			if (pinned)
			{
				openBookTab2.IsPreview = false;
			}
			int num = OpenTabs.IndexOf(openBookTab2);
			MoveTab(num, Math.Clamp(insertIndex, 0, OpenTabs.Count - 1));
		}
	}

	private async Task RestoreTabsAsync(OpenTabsPersistence.SavedTabs saved)
	{
		foreach (OpenTabsPersistence.SavedTab st in saved.Tabs)
		{
			Book book = AllBooks.FirstOrDefault((Book b) => string.Equals(b.FileID, st.FileId, StringComparison.Ordinal));
			if ((object)book != null)
			{
				await OpenBookForRestoreAsync(book, st.Page, st.Query ?? "");
			}
		}
		OpenBookTab openBookTab = OpenTabs.FirstOrDefault((OpenBookTab t) => string.Equals(t.FileId, saved.ActiveFileId, StringComparison.Ordinal)) ?? OpenTabs.FirstOrDefault();
		if (openBookTab != null)
		{
			ActivateTab(openBookTab);
		}
	}

	private async Task OpenBookForRestoreAsync(Book book, int page, string query)
	{
		TrackOpenBook(book, pin: true);
		if (!string.IsNullOrWhiteSpace(query))
		{
			SuppressNextOpen = true;
			SelectedBook = book;
			InBookSearchText = query;
			await SearchInBookAsync();
			if (page > 0)
			{
				CurrentPage = page;
			}
			OpenSelected(preserveInBookState: true);
		}
		else
		{
			OpenBookInline(book, preserveInBookState: false, (page > 0) ? new int?(page) : ((int?)null));
		}
		OpenBookTab openBookTab = OpenTabs.FirstOrDefault((OpenBookTab t) => string.Equals(t.FileId, book.FileID, StringComparison.Ordinal));
		if (openBookTab != null)
		{
			openBookTab.LastPage = page;
			openBookTab.InBookQuery = query;
			openBookTab.InBookHits = CaptureInBookHits();
		}
		await Task.Yield();
	}

	private void OpenSelected(bool preserveInBookState = false)
	{
		if ((object)SelectedBook != null)
		{
			TrackOpenBook(SelectedBook, pin: false);
		}
		OpenBookInline(SelectedBook, preserveInBookState);
	}

	private void OpenBookInline(Book? book, bool preserveInBookState = false, int? forcePage = null)
	{
		if (HasOpenBook && (object)book != null && string.Equals(_openBookFileId, book.FileID, StringComparison.Ordinal))
		{
			return;
		}
		if (HasOpenBook && !IsTextMode && !string.IsNullOrEmpty(_openBookFileId) && CurrentPage > 0 && _settings.Load().View.PersistSessionState)
		{
			try
			{
				_lastPageRepo.Save(_openBookFileId, CurrentPage);
			}
			catch (Exception exception)
			{
				Log.Warning(exception, "BookLastPage save failed for {FileId}", _openBookFileId);
			}
		}
		BatchingInlineOpen = true;
		try
		{
			if (preserveInBookState)
			{
				MarkedPdfPath = null;
			}
			else
			{
				ResetInBookSearch();
			}
			if ((object)book == null)
			{
				HasOpenBook = false;
				CurrentPdfPath = null;
				CurrentTextRelativePath = null;
				IsTextMode = false;
				_openBookFileId = null;
				CurrentBookTitle = string.Empty;
				_main.SetOpenBookTitle(null);
				return;
			}
			if (string.IsNullOrEmpty(book.FileID))
			{
				StatusText = SharedStrings.ErrorMissingFileId;
				return;
			}
			_openBookFileId = book.FileID;
			_main.SetOpenBookTitle(book.BookName);
			CurrentBookTitle = book.BookName ?? "";
			int result;
			if (string.Equals(book.SourceType, "Text", StringComparison.Ordinal))
			{
				if (string.IsNullOrEmpty(book.RelativePath))
				{
					StatusText = SharedStrings.S781;
					HasOpenBook = false;
					CurrentTextRelativePath = null;
					IsTextMode = false;
					return;
				}
				string text = _paths.OtzrayaTextPath(book.RelativePath);
				if (!File.Exists(text))
				{
					StatusText = SharedStrings.S2179 + text;
					HasOpenBook = false;
					CurrentTextRelativePath = null;
					IsTextMode = false;
					return;
				}
				CurrentPdfPath = null;
				MarkedPdfPath = null;
				CurrentBookHits = null;
				string text2 = EffectiveContentQuery();
				IReadOnlyList<string> currentTextTerms;
				if (!IsContentMode || string.IsNullOrWhiteSpace(text2))
				{
					IReadOnlyList<string> readOnlyList = Array.Empty<string>();
					currentTextTerms = readOnlyList;
				}
				else
				{
					currentTextTerms = QueryBuilder.ExtractHighlightTerms(text2, addPrefixes: false, expandRoots: false, expandNumberGender: false, expandGematria: false, expandSpelling: false, null, expandRashiOcr: false, dropPhraseConstituents: true);
				}
				CurrentTextTerms = currentTextTerms;
				CurrentBookTitle = book.BookName ?? "";
				IsTextMode = true;
				CurrentTextRelativePath = book.RelativePath;
				HasOpenBook = true;
				RecordNavIfNotSuppressed();
			}
			else if (string.Equals(book.SourceType, "Personal", StringComparison.Ordinal))
			{
				string relativePath = ((!string.IsNullOrEmpty(book.RelativePath)) ? book.RelativePath : book.FileID);
				string text3 = _paths.PersonalFilePath(relativePath);
				if (!File.Exists(text3))
				{
					StatusText = SharedStrings.S2180 + text3;
					HasOpenBook = false;
					CurrentPdfPath = null;
					return;
				}
				IsTextMode = false;
				CurrentTextRelativePath = null;
				if (!preserveInBookState)
				{
					MarkedPdfPath = null;
					CurrentBookHits = null;
				}
				CurrentPdfPath = text3;
				CurrentBookTitle = book.BookName ?? "";
				CurrentPage = (preserveInBookState ? CurrentPage : (forcePage ?? ((!_settings.Load().View.PersistSessionState) ? 1 : (_lastPageRepo.GetLastPage(book.FileID ?? string.Empty) ?? 1))));
				HasOpenBook = true;
				RecordNavIfNotSuppressed();
			}
			else if (!int.TryParse(book.FileID, out result))
			{
				StatusText = SharedStrings.ErrorMissingFileId;
			}
			else
			{
				string text4 = _paths.PdfPath(result, book.Folder);
				if (!File.Exists(text4))
				{
					_openBookFileId = null;
					HasOpenBook = false;
					CurrentPdfPath = null;
					TryDownloadThenOpenInlineAsync(book, text4, preserveInBookState);
				}
				else
				{
					IsTextMode = false;
					CurrentTextRelativePath = null;
					CurrentPdfPath = text4;
					CurrentBookTitle = book.BookName ?? "";
					CurrentPage = (preserveInBookState ? CurrentPage : (forcePage ?? ((!_settings.Load().View.PersistSessionState) ? 1 : (_lastPageRepo.GetLastPage(book.FileID ?? string.Empty) ?? 1))));
					HasOpenBook = true;
					RecordNavIfNotSuppressed();
				}
			}
		}
		finally
		{
			BatchingInlineOpen = false;
			OnPropertyChanged("CurrentPdfPath");
		}
	}

	private async Task TryDownloadThenOpenInlineAsync(Book book, string path, bool preserveInBookState)
	{
		OnDemandBookService obj = (OnDemandBookService)App.Services.GetService(typeof(OnDemandBookService));
		Window owner = Application.Current?.MainWindow;
		if (!(await obj.EnsureLocalAsync(book, owner)))
		{
			if ((object)SelectedBook == book)
			{
				StatusText = SharedStrings.S2181 + path;
				HasOpenBook = false;
				CurrentPdfPath = null;
			}
		}
		else if ((object)SelectedBook == book)
		{
			OpenSelected(preserveInBookState);
		}
	}

	private Book? ResolveOpenBook()
	{
		if (!HasOpenBook || string.IsNullOrEmpty(_openBookFileId))
		{
			return null;
		}
		return AllBooks.FirstOrDefault((Book b) => string.Equals(b.FileID, _openBookFileId, StringComparison.Ordinal));
	}

	[RelayCommand]
	private async Task SearchInBookAsync()
	{
		if (string.IsNullOrWhiteSpace(InBookSearchText))
		{
			Log.Information("SearchInBook: invoked with an empty query box — reset, nothing searched");
			ResetInBookSearch();
			return;
		}
		Book target = SelectedBook ?? ResolveOpenBook();
		if ((object)target == null)
		{
			InBookSearchStatus = SharedStrings.S288;
			Log.Warning("SearchInBook: no book to search (HasOpenBook={HasOpen} openFileId={Fid} contentMode={Mode}) — query '{Q}' kept, search skipped", HasOpenBook, _openBookFileId, IsContentMode, InBookSearchText);
			return;
		}
		if (ActiveTab != null)
		{
			ActiveTab.InBookQuery = InBookSearchText;
		}
		if (string.IsNullOrEmpty(target.FileID))
		{
			InBookSearchStatus = SharedStrings.ErrorMissingFileId;
			return;
		}
		string fileIdAtStart = target.FileID;
		IsInBookSearching = true;
		InBookSearchStatus = SharedStrings.S775;
		try
		{
			string text = ((!string.IsNullOrEmpty(target.RelativePath)) ? target.RelativePath : target.FileID);
			string fileName = (text.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? text : (text + ".pdf"));
			SearchOptions sopts = _settings.Load().Search;
			string remoteUrl = RemoteSearchUrl();
			string onlineUrl = ((remoteUrl == null) ? OnlineServiceUrl() : null);
			string inBookQuery = null;
			InBookHitInfo inBookHitInfo;
			int onlineFid;
			if (remoteUrl != null)
			{
				string rawQuery = InBookSearchText.Trim();
				if (_inBookSelectedSyns.Count > 0)
				{
					rawQuery = BuildWithSynonyms(InBookSearchText.Trim(), _inBookSelectedSyns, new QueryBuildOptions(Math.Max(1, MaxProximity), sopts.Hybur, FirstWordOnly: false, LastWordOnly: false, sopts.RasheyTevot ? _rasheyTevotMap : null, sopts.RootSearch, sopts.ExpandNumberGender, sopts.ExpandGematria, sopts.ExpandSpelling, sopts.ExpandAramaic ? _hebAramMap : null, null, 200, RequireWordOrder: sopts.RequireWordOrder, ExpandRashiOcr: sopts.ExpandRashiOcr));
				}
				inBookHitInfo = await _remote.GetInBookHitsAsync(remoteUrl, fileName, rawQuery, new RemoteSearchClient.InBookOptions(sopts.Hybur, sopts.RootSearch, sopts.ExpandGematria, sopts.ExpandSpelling, sopts.ExpandNumberGender, sopts.ExpandAramaic, sopts.RasheyTevot, sopts.RequireWordOrder, sopts.ExpandRashiOcr, Math.Clamp(sopts.Fuzziness, 0, 10)));
			}
			else if (onlineUrl != null && string.IsNullOrEmpty(target.RelativePath) && int.TryParse(target.FileID, out onlineFid))
			{
				string ibText = InBookSearchText.Trim();
				if (_inBookSelectedSyns.Count > 0)
				{
					ibText = BuildWithSynonyms(InBookSearchText.Trim(), _inBookSelectedSyns, new QueryBuildOptions(Math.Max(1, MaxProximity), sopts.Hybur, FirstWordOnly: false, LastWordOnly: false, sopts.RasheyTevot ? _rasheyTevotMap : null, sopts.RootSearch, sopts.ExpandNumberGender, sopts.ExpandGematria, sopts.ExpandSpelling, sopts.ExpandAramaic ? _hebAramMap : null, null, 200, RequireWordOrder: sopts.RequireWordOrder, ExpandRashiOcr: sopts.ExpandRashiOcr));
				}
				inBookHitInfo = await _webApi.GetInBookHitsAsync(onlineUrl, onlineFid.ToString(CultureInfo.InvariantCulture), ibText, new WebApiClient.InBookOptions(sopts.Hybur, sopts.RootSearch, sopts.ExpandGematria, sopts.ExpandSpelling, sopts.ExpandNumberGender, sopts.ExpandAramaic, sopts.RasheyTevot, sopts.RequireWordOrder, sopts.ExpandRashiOcr, Math.Clamp(sopts.Fuzziness, 0, 10), Math.Max(1, MaxProximity)));
				IReadOnlyList<string> readOnlyList = QueryBuilder.ExtractHighlightTerms((_inBookSelectedSyns.Count > 0) ? (InBookSearchText + " " + string.Join(" ", from s in _inBookSelectedSyns.Values.SelectMany((IReadOnlyList<string> v) => v)
					select (!s.Contains(' ')) ? s : ("\"" + s + "\""))) : InBookSearchText, addPrefixes: false, expandRoots: false, expandNumberGender: false, expandGematria: false, expandSpelling: false, null, expandRashiOcr: false, dropPhraseConstituents: true);
				if (readOnlyList.Count > 0)
				{
					inBookHitInfo = inBookHitInfo with
					{
						MatchedTerms = readOnlyList
					};
				}
				Log.Information("InBook[online]: fileId={FileId} q='{Q}' → hitCount={Hit} pages={Pages} terms={Terms}", onlineFid, ibText, inBookHitInfo.HitCount, inBookHitInfo.Pages.Count, inBookHitInfo.MatchedTerms.Count);
			}
			else
			{
				await _search.EnsureIndexOpenAsync();
				QueryBuildOptions opts = new QueryBuildOptions(Math.Max(1, MaxProximity), sopts.Hybur, FirstWordOnly: false, LastWordOnly: false, sopts.RasheyTevot ? _rasheyTevotMap : null, sopts.RootSearch, sopts.ExpandNumberGender, sopts.ExpandGematria, sopts.ExpandSpelling, sopts.ExpandAramaic ? _hebAramMap : null, _search.Engine.FilterIndexedWords, 200, RequireWordOrder: sopts.RequireWordOrder, ExpandRashiOcr: sopts.ExpandRashiOcr, ExpandWeakLetters: sopts.ExpandWeakLetters);
				inBookQuery = BuildWithSynonyms(InBookSearchText.Trim(), _inBookSelectedSyns, opts);
				string displayQuery = ((_inBookSelectedSyns.Count > 0) ? (InBookSearchText + " " + string.Join(" ", from s in _inBookSelectedSyns.Values.SelectMany((IReadOnlyList<string> v) => v)
					select (!s.Contains(' ')) ? s : ("\"" + s + "\""))) : InBookSearchText);
				inBookHitInfo = await _search.GetInBookHitsCachedAsync(fileName, inBookQuery, displayQuery, sopts.Hybur, sopts.RootSearch, sopts.ExpandNumberGender, sopts.ExpandGematria, sopts.ExpandSpelling, sopts.ExpandAramaic ? _hebAramMap : null, Math.Clamp(sopts.Fuzziness, 0, 10), sopts.ExpandWeakLetters);
			}
			if (!StillSelected())
			{
				return;
			}
			InBookHitPages.Clear();
			if (inBookHitInfo.HitCount == 0)
			{
				CurrentBookHits = null;
				MarkedPdfPath = null;
				InBookSearchStatus = SharedStrings.S785;
				CurrentHitIndex = -1;
				CurrentPage = ((!_settings.Load().View.PersistSessionState) ? 1 : (_lastPageRepo.GetLastPage(target.FileID ?? string.Empty) ?? 1));
				return;
			}
			CurrentBookHits = inBookHitInfo;
			if (inBookHitInfo.Pages.Count > 0)
			{
				foreach (int page in inBookHitInfo.Pages)
				{
					InBookHitPages.Add(page);
				}
			}
			else
			{
				InBookHitPages.Add(1);
			}
			OnPropertyChanged("InBookHitPages");
			CurrentHitIndex = 0;
			CurrentPage = InBookHitPages[0];
			SelectedHitPage = CurrentPage;
			UpdateMatchPosition();
			_main.SetOpenBookTitle(target.BookName, InBookSearchText);
			InBookSearchStatus = ((inBookHitInfo.Pages.Count > 0) ? $"{SharedStrings.S2182}{inBookHitInfo.HitCount}{SharedStrings.S2183}{inBookHitInfo.Pages.Count}{SharedStrings.S2184}" : $"{SharedStrings.S2185}{inBookHitInfo.HitCount}{SharedStrings.S2186}");
			if (remoteUrl == null && onlineUrl == null)
			{
				try
				{
					string markedPdfPath = await _search.GetMarkedPdfPathCachedAsync(fileName, inBookQuery);
					if (!StillSelected())
					{
						return;
					}
					MarkedPdfPath = markedPdfPath;
				}
				catch (Exception ex)
				{
					MarkedPdfPath = null;
					InBookSearchStatus = SharedStrings.S2187 + ex.Message;
				}
			}
			else
			{
				MarkedPdfPath = null;
			}
		}
		catch (Exception ex2)
		{
			Log.Warning(ex2, "InBook: search failed (online/network unreachable?)");
			InBookSearchStatus = SharedStrings.S2188 + ex2.Message;
		}
		finally
		{
			IsInBookSearching = false;
		}
		bool StillSelected()
		{
			if (!string.Equals(SelectedBook?.FileID, fileIdAtStart, StringComparison.Ordinal))
			{
				return string.Equals(_openBookFileId, fileIdAtStart, StringComparison.Ordinal);
			}
			return true;
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
	private void NextHit()
	{
		if (InBookHitPages.Count != 0)
		{
			CurrentHitIndex = (CurrentHitIndex + 1) % InBookHitPages.Count;
			CurrentPage = InBookHitPages[CurrentHitIndex];
			SelectedHitPage = CurrentPage;
			UpdateMatchPosition();
			RequestScrollToPage(CurrentPage);
		}
	}

	[RelayCommand]
	private void PrevHit()
	{
		if (InBookHitPages.Count != 0)
		{
			CurrentHitIndex = (CurrentHitIndex - 1 + InBookHitPages.Count) % InBookHitPages.Count;
			CurrentPage = InBookHitPages[CurrentHitIndex];
			SelectedHitPage = CurrentPage;
			UpdateMatchPosition();
			RequestScrollToPage(CurrentPage);
		}
	}

	[RelayCommand]
	private void NextBook()
	{
		if (IsContentMode)
		{
			StepResult(1);
		}
		else
		{
			StepBook(1);
		}
	}

	[RelayCommand]
	private void PrevBook()
	{
		if (IsContentMode)
		{
			StepResult(-1);
		}
		else
		{
			StepBook(-1);
		}
	}

	private void StepResult(int dir)
	{
		if (Results.Count != 0)
		{
			int num = (((object)SelectedRow == null) ? (-1) : Results.IndexOf(SelectedRow));
			int index = ((num >= 0) ? ((num + dir + Results.Count) % Results.Count) : ((dir <= 0) ? (Results.Count - 1) : 0));
			SelectedRow = Results[index];
		}
	}

	private void StepBook(int dir)
	{
		if (CatalogRows.Count == 0)
		{
			return;
		}
		CatalogRow catalogRow = SelectedCatalogRow ?? (((object)SelectedBook == null) ? null : FindRowForBook(SelectedBook));
		for (int i = ((catalogRow == null) ? (-1) : CatalogRows.IndexOf(catalogRow)) + dir; i >= 0 && i < CatalogRows.Count; i += dir)
		{
			CatalogRow catalogRow2 = CatalogRows[i];
			if (catalogRow2 is BookRow || catalogRow2 is GroupHeaderRow { IsExpanded: false })
			{
				SelectedCatalogRow = catalogRow2;
				break;
			}
		}
	}

	public void StepCatalogRow(int dir)
	{
		if (CatalogRows.Count != 0)
		{
			int num = ((SelectedCatalogRow == null) ? (-1) : CatalogRows.IndexOf(SelectedCatalogRow));
			int num2 = Math.Clamp(num + dir, 0, CatalogRows.Count - 1);
			if (num2 != num)
			{
				SelectedCatalogRow = CatalogRows[num2];
			}
		}
	}

	public void SetSelectedGroupExpanded(bool expanded)
	{
		if (SelectedCatalogRow is GroupHeaderRow groupHeaderRow && groupHeaderRow.IsExpanded != expanded)
		{
			ToggleGroup(groupHeaderRow);
		}
	}

	public void ReportViewerPage(int page)
	{
		if (page <= 0)
		{
			return;
		}
		if (ActiveTab != null)
		{
			ActiveTab.LastPage = page;
		}
		int? num = (InBookHitPages.Contains(page) ? new int?(page) : ((int?)null));
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

	private void UpdateMatchPosition()
	{
		if (InBookHitPages.Count == 0 || CurrentHitIndex < 0)
		{
			MatchPositionText = string.Empty;
			return;
		}
		MatchPositionText = $"{CurrentHitIndex + 1} / {InBookHitPages.Count}";
	}

	[RelayCommand]
	private void GoToHitPage(int page)
	{
		if (page > 0)
		{
			int num = InBookHitPages.IndexOf(page);
			if (num >= 0)
			{
				CurrentHitIndex = num;
			}
			CurrentPage = page;
			SelectedHitPage = page;
			UpdateMatchPosition();
			RequestScrollToPage(page);
		}
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
				InBookSearchStatus = ((count > 0) ? $"{SharedStrings.S2189}{num}{SharedStrings.S2190}{count}{SharedStrings.S2191}" : $"{SharedStrings.S2192}{num}{SharedStrings.S2193}");
			}
			else
			{
				InBookSearchStatus = ((count > 0) ? $"{SharedStrings.S2194}{drawnSoFar}{SharedStrings.S2195}{num}{SharedStrings.S2196}{count}{SharedStrings.S2197}" : $"{SharedStrings.S2198}{drawnSoFar}{SharedStrings.S2199}{num}{SharedStrings.S2200}");
			}
		}
	}

	public void ApplyFuzzyFinalPages(IReadOnlyList<int> pages)
	{
		if (InBookHitPages.SequenceEqual(pages))
		{
			return;
		}
		InBookHitPages.Clear();
		foreach (int page in pages)
		{
			InBookHitPages.Add(page);
		}
		OnPropertyChanged("InBookHitPages");
		UpdateMatchPosition();
	}

	public void ApplyVerifiedHitPages(IReadOnlyList<int> pages)
	{
		if (pages.Count == 0 || InBookHitPages.SequenceEqual(pages))
		{
			return;
		}
		bool flag = CurrentHitIndex == 0 && InBookHitPages.Count > 0;
		InBookHitPages.Clear();
		foreach (int page in pages)
		{
			InBookHitPages.Add(page);
		}
		OnPropertyChanged("InBookHitPages");
		if (flag)
		{
			CurrentHitIndex = 0;
			CurrentPage = pages[0];
			SelectedHitPage = pages[0];
		}
		UpdateMatchPosition();
	}

	private void ResetInBookSearch()
	{
		if (!string.IsNullOrWhiteSpace(InBookSearchText))
		{
			Log.Information("ResetInBookSearch: clearing a non-empty in-book query '{Q}' (hasOpenBook={HasOpen} activeTab={Tab})", InBookSearchText, HasOpenBook, ActiveTab?.FileId ?? "(none)");
		}
		InBookHitPages.Clear();
		OnPropertyChanged("InBookHitPages");
		CurrentHitIndex = -1;
		CurrentBookHits = null;
		SelectedHitPage = null;
		InBookSearchStatus = string.Empty;
		InBookSearchText = string.Empty;
		if (ActiveTab != null)
		{
			ActiveTab.InBookQuery = "";
		}
		MatchPositionText = string.Empty;
		MarkedPdfPath = null;
	}

	public void PersistRatio(double ratio)
	{
		double clamped = Math.Clamp(ratio, 0.15, 0.85);
		if (Math.Abs(clamped - CatalogRatio) < 0.005)
		{
			return;
		}
		CatalogRatio = clamped;
		try
		{
			_settings.Update(delegate(BookshelfOptions o)
			{
				o.View.LibraryCatalogRatio = clamped;
			});
		}
		catch
		{
		}
	}





























}
