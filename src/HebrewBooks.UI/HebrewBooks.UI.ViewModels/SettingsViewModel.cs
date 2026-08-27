using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;
using CommunityToolkit.Mvvm.Input;
using HebrewBooks.Core.Abstractions;
using HebrewBooks.Core.Models;
using HebrewBooks.Infrastructure.Paths;
using HebrewBooks.Infrastructure.Settings;
using HebrewBooks.Services.Background;
using HebrewBooks.Services.Provisioning;
using HebrewBooks.Services.Search;
using HebrewBooks.Services.Toc;
using HebrewBooks.UI.Controls;
using HebrewBooks.UI.Dialogs;
using HebrewBooks.UI.Resources;
using HebrewBooks.UI.Services;
using HebrewBooks.UI.Services.Theming;
using HebrewBooks.UI.Views;
using Microsoft.Win32;
using Serilog;
using Velopack;

namespace HebrewBooks.UI.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
	private readonly JsonSettingsStore _store;

	private readonly AppUpdateService _updates;

	private readonly ThemeService _theme;

	private readonly RasheyTevotMap _rasheyTevotMap;

	private readonly IPathResolver _paths;

	private readonly TocBundleService _tocBundle;

	private readonly OcrEngineInstaller _ocr;

	private UpdateInfo? _pendingUpdate;

	[ObservableProperty]
	private BookshelfOptions _options = new BookshelfOptions();

	[ObservableProperty]
	private string _updateStatus = string.Empty;

	[ObservableProperty]
	private string _currentVersion = "dev";

	[ObservableProperty]
	private bool _isCheckingUpdate;

	[ObservableProperty]
	private bool _hasPendingUpdate;

	[ObservableProperty]
	private int _updateProgress;

	private ICollectionView? _rasheyTevotView;

	[ObservableProperty]
	private RasheyTevotEntry? _selectedRasheyTevotEntry;

	[ObservableProperty]
	private string _rasheyTevotStatus = string.Empty;

	[ObservableProperty]
	private string _rasheyTevotFilter = string.Empty;

	private readonly ProvisioningService _provisioning;

	private readonly BackgroundProcessorService _background;

	[ObservableProperty]
	private string _libraryDownloadStatus = "";

	[ObservableProperty]
	private bool _libraryDownloadHasWork;

	[ObservableProperty]
	private bool _libraryDownloadRunning;

	[ObservableProperty]
	private int _libraryDownloadProgress;

	private static bool _inSearchReload;

	[ObservableProperty]
	private string _onlineConnectionStatus = string.Empty;

	[ObservableProperty]
	private string _updateReleaseNotes = string.Empty;

	[ObservableProperty]
	private bool _ocrEngineInstalled;

	[ObservableProperty]
	private string _ocrEngineStatus = string.Empty;

	[ObservableProperty]
	private bool _ocrInstallBusy;

	[ObservableProperty]
	private int _ocrInstallProgress;

	[ObservableProperty]
	private string _tocBundleStatus = string.Empty;


























	public IReadOnlyList<string> ThemeOptions { get; } = new string[1] { "System" }.Concat(PaletteRegistry.All.Select((Palette p) => p.Id)).ToArray();

	public ObservableCollection<RasheyTevotEntry> RasheyTevotEntries { get; } = new ObservableCollection<RasheyTevotEntry>();

	public bool CheckUpdatesOnStartup
	{
		get
		{
			return Options.Updates.CheckOnStartup;
		}
		set
		{
			if (Options.Updates.CheckOnStartup != value)
			{
				Options.Updates.CheckOnStartup = value;
				OnPropertyChanged("CheckUpdatesOnStartup");
				_store.Update(delegate(BookshelfOptions o)
				{
					o.Updates.CheckOnStartup = value;
				});
			}
		}
	}

	public bool AutoDownloadUpdates
	{
		get
		{
			return Options.Updates.AutoDownload;
		}
		set
		{
			if (Options.Updates.AutoDownload != value)
			{
				Options.Updates.AutoDownload = value;
				OnPropertyChanged("AutoDownloadUpdates");
				_store.Update(delegate(BookshelfOptions o)
				{
					o.Updates.AutoDownload = value;
				});
			}
		}
	}

	public bool IncludeBetaUpdates
	{
		get
		{
			return Options.Updates.IncludeBeta;
		}
		set
		{
			if (Options.Updates.IncludeBeta != value)
			{
				Options.Updates.IncludeBeta = value;
				OnPropertyChanged("IncludeBetaUpdates");
				_store.Update(delegate(BookshelfOptions o)
				{
					o.Updates.IncludeBeta = value;
				});
			}
		}
	}

	public string SelectedTheme
	{
		get
		{
			return Options.View.Theme;
		}
		set
		{
			if (!(Options.View.Theme == value))
			{
				Options.View.Theme = value;
				OnPropertyChanged("SelectedTheme");
				_theme.ApplyAndPersist(value);
			}
		}
	}

	public IReadOnlyList<MissingBookAction> MissingBookActions { get; } = new MissingBookAction[3]
	{
		MissingBookAction.Ask,
		MissingBookAction.AlwaysDownload,
		MissingBookAction.NeverDownload
	};

	public MissingBookAction SelectedMissingBookAction
	{
		get
		{
			return Options.View.MissingBookAction;
		}
		set
		{
			if (Options.View.MissingBookAction != value)
			{
				Options.View.MissingBookAction = value;
				OnPropertyChanged("SelectedMissingBookAction");
				_store.Update(delegate(BookshelfOptions o)
				{
					o.View.MissingBookAction = value;
				});
			}
		}
	}

	public bool ShowRowDetails
	{
		get
		{
			return Options.View.ShowRowDetails;
		}
		set
		{
			if (Options.View.ShowRowDetails != value)
			{
				Options.View.ShowRowDetails = value;
				OnPropertyChanged("ShowRowDetails");
				_store.Save(Options);
				SettingsViewModel.ShowRowDetailsChanged?.Invoke(value);
			}
		}
	}

	public bool ShowSearchHint
	{
		get
		{
			return Options.View.ShowSearchHint;
		}
		set
		{
			if (Options.View.ShowSearchHint != value)
			{
				Options.View.ShowSearchHint = value;
				OnPropertyChanged("ShowSearchHint");
				_store.Save(Options);
				SettingsViewModel.ShowSearchHintChanged?.Invoke(value);
			}
		}
	}

	public bool EnableSynonymChips
	{
		get
		{
			return Options.View.EnableSynonymChips;
		}
		set
		{
			if (Options.View.EnableSynonymChips != value)
			{
				Options.View.EnableSynonymChips = value;
				OnPropertyChanged("EnableSynonymChips");
				_store.Save(Options);
				SettingsViewModel.SynonymChipsEnabledChanged?.Invoke(value);
			}
		}
	}

	public bool ShowPerformanceHints
	{
		get
		{
			return Options.View.ShowPerformanceHints;
		}
		set
		{
			if (Options.View.ShowPerformanceHints != value)
			{
				Options.View.ShowPerformanceHints = value;
				OnPropertyChanged("ShowPerformanceHints");
				_store.Save(Options);
			}
		}
	}

	public bool ShareUsageData
	{
		get
		{
			return Options.UsageTelemetryConsent == true;
		}
		set
		{
			if (Options.UsageTelemetryConsent == true != value)
			{
				Options.UsageTelemetryConsent = value;
				Options.UsageTelemetryConsentAskedVersion = App.CurrentVersionString();
				OnPropertyChanged("ShareUsageData");
				_store.Save(Options);
			}
		}
	}

	public int MaxProximity
	{
		get
		{
			return Options.Search.MaxProximity;
		}
		set
		{
			int v = Math.Max(1, value);
			if (Options.Search.MaxProximity != v)
			{
				Options.Search.MaxProximity = v;
				OnPropertyChanged("MaxProximity");
				_store.Update(delegate(BookshelfOptions o)
				{
					o.Search.MaxProximity = v;
				});
				RaiseSearchOptionsChanged();
			}
		}
	}

	public bool Hybur
	{
		get
		{
			return Options.Search.Hybur;
		}
		set
		{
			if (Options.Search.Hybur != value)
			{
				Options.Search.Hybur = value;
				OnPropertyChanged("Hybur");
				_store.Update(delegate(BookshelfOptions o)
				{
					o.Search.Hybur = value;
				});
				RaiseSearchOptionsChanged();
			}
		}
	}

	public IReadOnlyList<LanguageChoice> LanguageChoices { get; } = new LanguageChoice[3]
	{
		new LanguageChoice("auto", SharedStrings.S890),
		new LanguageChoice("he", SharedStrings.S891),
		new LanguageChoice("en", "English")
	};

	public string SelectedLanguage
	{
		get
		{
			string text = (Options.Language ?? "auto").Trim().ToLowerInvariant();
			if ((!(text == "he") && !(text == "en")) || 1 == 0)
			{
				return "auto";
			}
			return text;
		}
		set
		{
			string v = (value ?? "auto").Trim().ToLowerInvariant();
			string text = v;
			if ((!(text == "he") && !(text == "en")) || 1 == 0)
			{
				v = "auto";
			}
			if (!(SelectedLanguage == v))
			{
				Options.Language = v;
				_store.Update(delegate(BookshelfOptions o)
				{
					o.Language = v;
				});
				OnPropertyChanged("SelectedLanguage");
				PromptRestart();
			}
		}
	}

	public bool RasheyTevot
	{
		get
		{
			return Options.Search.RasheyTevot;
		}
		set
		{
			if (Options.Search.RasheyTevot != value)
			{
				Options.Search.RasheyTevot = value;
				OnPropertyChanged("RasheyTevot");
				_store.Update(delegate(BookshelfOptions o)
				{
					o.Search.RasheyTevot = value;
				});
				RaiseSearchOptionsChanged();
			}
		}
	}

	public int MaxFilesToRetrieve
	{
		get
		{
			return Options.Search.MaxFilesToRetrieve;
		}
		set
		{
			int v = Math.Max(10, value);
			if (Options.Search.MaxFilesToRetrieve != v)
			{
				Options.Search.MaxFilesToRetrieve = v;
				OnPropertyChanged("MaxFilesToRetrieve");
				_store.Update(delegate(BookshelfOptions o)
				{
					o.Search.MaxFilesToRetrieve = v;
				});
				RaiseSearchOptionsChanged();
			}
		}
	}

	public bool ShowPageRail
	{
		get
		{
			return Options.View.ShowPageRail;
		}
		set
		{
			if (Options.View.ShowPageRail != value)
			{
				Options.View.ShowPageRail = value;
				OnPropertyChanged("ShowPageRail");
				_store.Save(Options);
				PdfJsHost.BroadcastPageRailEnabled(value);
			}
		}
	}

	public bool ShowHitPagesStrip
	{
		get
		{
			return Options.View.ShowHitPagesStrip;
		}
		set
		{
			if (Options.View.ShowHitPagesStrip != value)
			{
				Options.View.ShowHitPagesStrip = value;
				OnPropertyChanged("ShowHitPagesStrip");
				_store.Save(Options);
			}
		}
	}

	public int RegionCopyDpi
	{
		get
		{
			return Options.View.RegionCopyDpi;
		}
		set
		{
			int num = Math.Max(72, Math.Min(600, value));
			if (Options.View.RegionCopyDpi != num)
			{
				Options.View.RegionCopyDpi = num;
				OnPropertyChanged("RegionCopyDpi");
				_store.Save(Options);
				PdfJsHost.BroadcastRegionCopyDpi(num);
			}
		}
	}

	public IReadOnlyList<int> RegionCopyDpiPresets { get; } = new int[7] { 100, 150, 200, 250, 300, 400, 600 };

	public bool ShowBetaFeatures { get; }

	public bool NetworkInstall
	{
		get
		{
			return Options.NetworkInstall;
		}
		set
		{
			if (Options.NetworkInstall != value)
			{
				Options.NetworkInstall = value;
				OnPropertyChanged("NetworkInstall");
				_store.Save(Options);
			}
		}
	}

	public bool UseOnlineService
	{
		get
		{
			return Options.UseOnlineService;
		}
		set
		{
			if (Options.UseOnlineService != value)
			{
				Options.UseOnlineService = value;
				OnPropertyChanged("UseOnlineService");
				_store.Save(Options);
			}
		}
	}

	public string OnlineServiceUrl
	{
		get
		{
			return Options.Paths.OnlineServiceUrl ?? string.Empty;
		}
		set
		{
			string text = (string.IsNullOrWhiteSpace(value) ? null : value.Trim());
			if (!(Options.Paths.OnlineServiceUrl == text))
			{
				Options.Paths.OnlineServiceUrl = text;
				OnPropertyChanged("OnlineServiceUrl");
				_store.Save(Options);
			}
		}
	}

	public string OnlinePdfBaseUrl
	{
		get
		{
			return Options.Paths.OnlinePdfBaseUrl ?? string.Empty;
		}
		set
		{
			string text = (string.IsNullOrWhiteSpace(value) ? null : value.Trim());
			if (!(Options.Paths.OnlinePdfBaseUrl == text))
			{
				Options.Paths.OnlinePdfBaseUrl = text;
				OnPropertyChanged("OnlinePdfBaseUrl");
				_store.Save(Options);
			}
		}
	}

	public string OnlineServiceUrlPlaceholder => "https://hebrewbooks.pages.dev";

	public string OnlinePdfBaseUrlPlaceholder => "https://files.hebrewbooksoffline.dpdns.org/HebrewBooks/books";

	public string BooksDirOverride
	{
		get
		{
			return Options.Paths.BooksDirOverride ?? string.Empty;
		}
		set
		{
			string text = (string.IsNullOrWhiteSpace(value) ? null : value.Trim());
			if (!(Options.Paths.BooksDirOverride == text))
			{
				Options.Paths.BooksDirOverride = text;
				OnPropertyChanged("BooksDirOverride");
				_store.Save(Options);
			}
		}
	}

	public string IndexesDirOverride
	{
		get
		{
			return Options.Paths.IndexesDirOverride ?? string.Empty;
		}
		set
		{
			string text = (string.IsNullOrWhiteSpace(value) ? null : value.Trim());
			if (!(Options.Paths.IndexesDirOverride == text))
			{
				Options.Paths.IndexesDirOverride = text;
				OnPropertyChanged("IndexesDirOverride");
				_store.Save(Options);
			}
		}
	}

	public string FastIndexesDir
	{
		get
		{
			return Options.Paths.FastIndexesDir ?? string.Empty;
		}
		set
		{
			string text = PathInput.Normalize(value);
			if (!(Options.Paths.FastIndexesDir == text))
			{
				Options.Paths.FastIndexesDir = text;
				OnPropertyChanged("FastIndexesDir");
				OnPropertyChanged("IndexLocationStatus");
				_store.Save(Options);
			}
		}
	}

	public string IndexLocationStatus
	{
		get
		{
			var (fastIndexStatus, text) = PathResolver.InspectFastIndexDir(Options.Paths.FastIndexesDir);
			return fastIndexStatus switch
			{
				PathResolver.FastIndexStatus.Usable => SharedStrings.IndexLocationInEffect + text, 
				PathResolver.FastIndexStatus.FolderMissing => SharedStrings.IndexLocationMissing + _paths.IndexesRoot, 
				PathResolver.FastIndexStatus.NoIndexFiles => SharedStrings.IndexLocationNoIx + _paths.IndexesRoot, 
				_ => SharedStrings.IndexLocationDefault + _paths.IndexesRoot, 
			};
		}
	}

	public string CatalogMasterPath
	{
		get
		{
			return Options.Paths.CatalogMasterPath ?? string.Empty;
		}
		set
		{
			string text = (string.IsNullOrWhiteSpace(value) ? null : value.Trim());
			if (!(Options.Paths.CatalogMasterPath == text))
			{
				Options.Paths.CatalogMasterPath = text;
				OnPropertyChanged("CatalogMasterPath");
				_store.Save(Options);
			}
		}
	}

	public string SearchServiceUrl
	{
		get
		{
			return Options.Paths.SearchServiceUrl ?? string.Empty;
		}
		set
		{
			string text = (string.IsNullOrWhiteSpace(value) ? null : value.Trim());
			if (!(Options.Paths.SearchServiceUrl == text))
			{
				Options.Paths.SearchServiceUrl = text;
				OnPropertyChanged("SearchServiceUrl");
				_store.Save(Options);
			}
		}
	}

	public string NetworkBasePath
	{
		get
		{
			return Options.Paths.NetworkBasePath ?? string.Empty;
		}
		set
		{
			string text = (string.IsNullOrWhiteSpace(value) ? null : value.Trim());
			if (!(Options.Paths.NetworkBasePath == text))
			{
				Options.Paths.NetworkBasePath = text;
				OnPropertyChanged("NetworkBasePath");
				_store.Save(Options);
			}
		}
	}

	public string SearchServiceHost
	{
		get
		{
			return Options.Paths.SearchServiceHost ?? string.Empty;
		}
		set
		{
			string text = (string.IsNullOrWhiteSpace(value) ? null : value.Trim());
			if (!(Options.Paths.SearchServiceHost == text))
			{
				Options.Paths.SearchServiceHost = text;
				OnPropertyChanged("SearchServiceHost");
				_store.Save(Options);
			}
		}
	}

	public int SearchServicePort
	{
		get
		{
			return Options.Paths.SearchServicePort;
		}
		set
		{
			int num = ((value <= 0) ? 8080 : value);
			if (Options.Paths.SearchServicePort != num)
			{
				Options.Paths.SearchServicePort = num;
				OnPropertyChanged("SearchServicePort");
				_store.Save(Options);
			}
		}
	}

	public bool ForceProtectMode
	{
		get
		{
			return Options.ForceProtectMode;
		}
		set
		{
			if (Options.ForceProtectMode != value)
			{
				Options.ForceProtectMode = value;
				OnPropertyChanged("ForceProtectMode");
				_store.Save(Options);
			}
		}
	}

	public bool UnifiedSearchLayout
	{
		get
		{
			return Options.View.UnifiedSearchLayout;
		}
		set
		{
			if (Options.View.UnifiedSearchLayout != value)
			{
				Options.View.UnifiedSearchLayout = value;
				OnPropertyChanged("UnifiedSearchLayout");
				_store.Save(Options);
				PromptRestart();
			}
		}
	}

	public bool PersistSessionState
	{
		get
		{
			return Options.View.PersistSessionState;
		}
		set
		{
			if (Options.View.PersistSessionState != value)
			{
				Options.View.PersistSessionState = value;
				OnPropertyChanged("PersistSessionState");
				_store.Save(Options);
			}
		}
	}

	public System.Windows.Media.Brush HighlightColorBrush
	{
		get
		{
			try
			{
				return new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(Options.Search.HighlightColor));
			}
			catch
			{
				return System.Windows.Media.Brushes.Gold;
			}
		}
	}

	public bool HasUpdateReleaseNotes => !string.IsNullOrWhiteSpace(UpdateReleaseNotes);

	public bool CanUninstall => _updates.CanUninstall;

	public bool OcrControlsEnabled => !OcrInstallBusy;

	public bool OcrShowProgress => OcrInstallBusy;

	public string OcrEngineInstallButtonText
	{
		get
		{
			if (!OcrEngineInstalled)
			{
				return SharedStrings.S922;
			}
			return SharedStrings.S921;
		}
	}














































	public static event Action<bool>? ShowRowDetailsChanged;

	public static event Action<bool>? ShowSearchHintChanged;

	public static event Action<bool>? SynonymChipsEnabledChanged;

	public static event Action? SearchOptionsChanged;

	public SettingsViewModel(JsonSettingsStore store, AppUpdateService updates, ThemeService theme, RasheyTevotMap rasheyTevotMap, IPathResolver paths, TocBundleService tocBundle, OcrEngineInstaller ocr, ProvisioningService provisioning, BackgroundProcessorService background)
	{
		_store = store;
		_updates = updates;
		_theme = theme;
		_rasheyTevotMap = rasheyTevotMap;
		_paths = paths;
		_tocBundle = tocBundle;
		_ocr = ocr;
		_provisioning = provisioning;
		_background = background;
		Options = _store.Load();
		CurrentVersion = _updates.CurrentVersion;
		ShowBetaFeatures = _updates.ShowBetaFeatures;
		UpdateStatus = (_updates.IsEnabled ? SharedStrings.S888 : SharedStrings.S889);
		_rasheyTevotView = CollectionViewSource.GetDefaultView(RasheyTevotEntries);
		_rasheyTevotView.Filter = MatchesRasheyTevotFilter;
		LoadRasheyTevotEntries();
		RefreshOcrEngineStatus();
		SearchOptionsChanged += ReloadSearchOptionsFromStore;
		_background.JobStarted += OnLibraryJobStarted;
		_background.JobProgress += OnLibraryJobProgress;
		_background.JobCompleted += OnLibraryJobCompleted;
	}

	private static bool IsProvisionJob(BackgroundProcessorService.Job job)
	{
		return job is ProvisionDownloadJob;
	}

	private void OnLibraryJobStarted(object? _, BackgroundProcessorService.Job job)
	{
		if (IsProvisionJob(job))
		{
			OnUi(delegate
			{
				LibraryDownloadRunning = true;
				LibraryDownloadProgress = 0;
			});
		}
	}

	private void OnLibraryJobProgress(object? _, JobProgress p)
	{
		if (LibraryDownloadRunning)
		{
			OnUi(delegate
			{
				LibraryDownloadProgress = (int)Math.Round(p.Percent * 100.0);
			});
		}
	}

	private void OnLibraryJobCompleted(object? _, JobCompletion c)
	{
		if (IsProvisionJob(c.Job))
		{
			OnUi(delegate
			{
				LibraryDownloadRunning = false;
				LibraryDownloadProgress = ((c.Error == null && !c.Cancelled) ? 100 : LibraryDownloadProgress);
			});
			_ = RefreshLibraryDownloadStatusAsync();
		}
	}

	private static void OnUi(Action a)
	{
		Dispatcher dispatcher = System.Windows.Application.Current?.Dispatcher;
		if (dispatcher == null || dispatcher.CheckAccess())
		{
			a();
		}
		else
		{
			dispatcher.BeginInvoke(a);
		}
	}

	[RelayCommand]
	private async Task RefreshLibraryDownloadStatusAsync()
	{
		try
		{
			BookshelfOptions opts = _store.Load();
			string root = _paths.DataDriveRoot;
			ProvisioningService.LibraryDownloadStatus status = await Task.Run(() => _provisioning.DescribeStatus(root, opts.Paths.InstallType, opts.Paths.BuildIndexLocally));
			List<string> missing = new List<string>();
			if (status.Pending.Index)
			{
				missing.Add(SharedStrings.S2396);
			}
			if (status.Pending.Books)
			{
				missing.Add(SharedStrings.S2397);
			}
			if (status.Pending.BuildIndexLocally)
			{
				missing.Add(SharedStrings.S2398);
			}
			OnUi(delegate
			{
				LibraryDownloadHasWork = status.Pending.HasWork;
				LibraryDownloadStatus = (status.IsComplete ? $"{SharedStrings.S2394}{SharedStrings.S2399}{status.BooksOnDisk:N0}" : $"{SharedStrings.S2395}{string.Join(", ", missing)}{SharedStrings.S2399}{status.BooksOnDisk:N0}");
			});
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			Exception ex3 = ex2;
			OnUi(delegate
			{
				LibraryDownloadHasWork = false;
				LibraryDownloadStatus = ex3.Message;
			});
		}
	}

	[RelayCommand]
	private async Task ResumeLibraryDownloadAsync()
	{
		if (LibraryDownloadRunning)
		{
			return;
		}
		try
		{
			BookshelfOptions bookshelfOptions = _store.Load();
			string dataDriveRoot = _paths.DataDriveRoot;
			ProvisionPlan provisionPlan = _provisioning.ComputePendingPlan(dataDriveRoot, bookshelfOptions.Paths.InstallType, bookshelfOptions.Paths.BuildIndexLocally);
			if (!provisionPlan.HasWork)
			{
				await RefreshLibraryDownloadStatusAsync();
				return;
			}
			_store.Update(delegate(BookshelfOptions o)
			{
				o.Paths.ProvisionPending = true;
			});
			ISearchEngine engine = null;
			IndexSpec localIndexSpec = null;
			if (provisionPlan.BuildIndexLocally)
			{
				engine = App.Services.GetService(typeof(ISearchEngine)) as ISearchEngine;
				localIndexSpec = new IndexSpec(_paths.IndexesRoot, new string[1] { _paths.PdfsRoot }, UseNativeEnumeration: true);
			}
			await _background.EnqueueAsync(new ProvisionDownloadJob(dataDriveRoot, provisionPlan, _provisioning, bookshelfOptions.Paths.InstallType, engine, localIndexSpec));
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			OnUi(delegate
			{
				LibraryDownloadStatus = ex2.Message;
			});
		}
	}

	[RelayCommand]
	private void CancelLibraryDownload()
	{
		_background.CancelCurrentJob();
	}

	private bool MatchesRasheyTevotFilter(object obj)
	{
		if (string.IsNullOrWhiteSpace(RasheyTevotFilter))
		{
			return true;
		}
		if (!(obj is RasheyTevotEntry rasheyTevotEntry))
		{
			return false;
		}
		string value = RasheyTevotFilter.Trim();
		string acronym = rasheyTevotEntry.Acronym;
		if (acronym == null || !acronym.Contains(value, StringComparison.OrdinalIgnoreCase))
		{
			return rasheyTevotEntry.Expansions?.Contains(value, StringComparison.OrdinalIgnoreCase) ?? false;
		}
		return true;
	}

	public static void RaiseSearchOptionsChanged()
	{
		if (!_inSearchReload)
		{
			SettingsViewModel.SearchOptionsChanged?.Invoke();
		}
	}

	public static void ApplySearchOptionsReload(Action reload)
	{
		bool inSearchReload = _inSearchReload;
		_inSearchReload = true;
		try
		{
			reload();
		}
		finally
		{
			_inSearchReload = inSearchReload;
		}
	}

	private void ReloadSearchOptionsFromStore()
	{
		ApplySearchOptionsReload(delegate
		{
			Options = _store.Load();
			OnPropertyChanged("MaxProximity");
			OnPropertyChanged("Hybur");
			OnPropertyChanged("RasheyTevot");
			OnPropertyChanged("MaxFilesToRetrieve");
		});
	}

	public void RefreshFromDisk()
	{
		ApplySearchOptionsReload(delegate
		{
			Options = _store.Load();
			OnPropertyChanged(string.Empty);
		});
	}

	[RelayCommand]
	private async Task CheckOnlineConnection()
	{
		string siteBase = (string.IsNullOrWhiteSpace(OnlineServiceUrl) ? "https://hebrewbooks.pages.dev" : OnlineServiceUrl.Trim());
		OnlineConnectionStatus = SharedStrings.S892;
		try
		{
			OnlineConnectionStatus = ((await new WebApiClient().IsHealthyAsync(siteBase)) ? SharedStrings.S893 : SharedStrings.S894);
		}
		catch (Exception ex)
		{
			OnlineConnectionStatus = SharedStrings.S9080 + ex.Message;
		}
	}

	[RelayCommand]
	private void BrowseFastIndexDir()
	{
		OpenFolderDialog openFolderDialog = new OpenFolderDialog
		{
			Title = SharedStrings.IndexLocationBrowseTitle
		};
		string fastIndexesDir = Options.Paths.FastIndexesDir;
		try
		{
			if (!string.IsNullOrWhiteSpace(fastIndexesDir) && Directory.Exists(fastIndexesDir))
			{
				openFolderDialog.InitialDirectory = fastIndexesDir;
			}
		}
		catch
		{
		}
		if (openFolderDialog.ShowDialog() == true)
		{
			string folderName = openFolderDialog.FolderName;
			var (fastIndexStatus, text) = PathResolver.InspectFastIndexDir(folderName);
			if (fastIndexStatus != PathResolver.FastIndexStatus.Usable)
			{
				ShowFastIndexRejected(fastIndexStatus, folderName);
				return;
			}
			FastIndexesDir = text;
			ShowFastIndexAccepted(text, !string.Equals(text, folderName, StringComparison.OrdinalIgnoreCase));
		}
	}

	private static void ShowFastIndexAccepted(string resolved, bool adjusted)
	{
		int num = PathResolver.CountIndexSegments(resolved);
		List<string> list = new List<string>
		{
			string.Format(SharedStrings.IndexLocationCheckOk, num),
			resolved
		};
		if (adjusted)
		{
			list.Add(SharedStrings.IndexLocationCheckAdjusted);
		}
		list.Add(string.Format(SharedStrings.IndexLocationCheckSecondary, Sibling("Otzraya_IDX"), Sibling("Personal_IDX")));
		list.Add(SharedStrings.IndexLocationCheckApplies);
		HebrewMessageBox.Show(string.Join(Environment.NewLine + Environment.NewLine, list), SharedStrings.IndexLocationCheckTitle, MessageBoxButton.OK, MessageBoxImage.Asterisk);
		string Sibling(string name)
		{
			string directoryName = Path.GetDirectoryName(resolved);
			if (directoryName == null || !PathResolver.IsUsableIndexDir(Path.Combine(directoryName, name)))
			{
				return SharedStrings.IndexLocationCheckSiblingMissing;
			}
			return SharedStrings.IndexLocationCheckSiblingFound;
		}
	}

	private static void ShowFastIndexRejected(PathResolver.FastIndexStatus status, string picked)
	{
		List<string> list = new List<string>
		{
			(status == PathResolver.FastIndexStatus.FolderMissing) ? SharedStrings.IndexLocationCheckGone : SharedStrings.IndexLocationCheckNoIx,
			picked
		};
		IReadOnlyList<string> readOnlyList = PathResolver.FindIndexFoldersInside(picked);
		if (readOnlyList.Count > 0)
		{
			list.Add(SharedStrings.IndexLocationCheckNearby + Environment.NewLine + string.Join(Environment.NewLine, readOnlyList));
		}
		list.Add(SharedStrings.IndexLocationCheckHint);
		HebrewMessageBox.Show(string.Join(Environment.NewLine + Environment.NewLine, list), SharedStrings.IndexLocationCheckTitle, MessageBoxButton.OK, MessageBoxImage.Exclamation);
	}

	[RelayCommand]
	private void ResetDataRoot()
	{
		_store.Update(delegate(BookshelfOptions o)
		{
			o.Paths.DataVolumeSerial = 0u;
			o.Paths.DataSubdir = "HebrewBooks";
			o.Paths.InstallType = "Full";
			o.Paths.BuildIndexLocally = false;
			o.Paths.ProvisionPending = false;
			o.Paths.ForceRescan = true;
		});
		if (HebrewMessageBox.Show(SharedStrings.S896, SharedStrings.S897, MessageBoxButton.YesNo, MessageBoxImage.Asterisk) != MessageBoxResult.Yes)
		{
			return;
		}
		try
		{
			string processPath = Environment.ProcessPath;
			if (!string.IsNullOrEmpty(processPath))
			{
				Process.Start(new ProcessStartInfo(processPath)
				{
					Arguments = "--relaunch",
					UseShellExecute = true
				});
			}
			System.Windows.Application.Current.Shutdown();
		}
		catch
		{
		}
	}

	[RelayCommand]
	private void ResetUserState()
	{
		DoReset(includeBookLastPage: false);
	}

	[RelayCommand]
	private void HardResetUserState()
	{
		DoReset(includeBookLastPage: true);
	}

	private void DoReset(bool includeBookLastPage)
	{
		List<string> list = new List<string>
		{
			SharedStrings.S9081,
			SharedStrings.S9082,
			SharedStrings.S9083
		};
		if (includeBookLastPage)
		{
			list.Add(SharedStrings.S9084);
			list.Add(SharedStrings.S9085);
		}
		string caption = (includeBookLastPage ? SharedStrings.S214 : SharedStrings.S212);
		if (HebrewMessageBox.Show(SharedStrings.S903 + string.Join("\n", list) + "\n\n" + SharedStrings.S904 + SharedStrings.S905, caption, MessageBoxButton.YesNo, MessageBoxImage.Exclamation, MessageBoxResult.No) != MessageBoxResult.Yes)
		{
			return;
		}
		try
		{
			((IFavoritesRepository)App.Services.GetService(typeof(IFavoritesRepository)))?.Clear();
			if (includeBookLastPage)
			{
				((IBookLastPageRepository)App.Services.GetService(typeof(IBookLastPageRepository)))?.Clear();
				((SearchHistoryStore)App.Services.GetService(typeof(SearchHistoryStore)))?.Clear();
			}
			try
			{
				string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HebrewBooks", "session.json");
				if (File.Exists(path))
				{
					File.Delete(path);
				}
			}
			catch (Exception exception)
			{
				Log.Warning(exception, "Reset: deleting session.json failed");
			}
			PathsOptions paths = Options.Paths;
			BookshelfOptions options = new BookshelfOptions
			{
				Paths = paths
			};
			_store.Save(options);
			Options = options;
			string fileName = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "HebrewBooks.exe");
			Process.Start(new ProcessStartInfo
			{
				FileName = fileName,
				UseShellExecute = true
			});
			System.Windows.Application.Current.Shutdown();
		}
		catch (Exception ex)
		{
			Log.Error(ex, "Reset: failed");
			HebrewMessageBox.Show(SharedStrings.S2308 + ex.Message, SharedStrings.S558, MessageBoxButton.OK, MessageBoxImage.Hand);
		}
	}

	private static void PromptRestart()
	{
		if (HebrewMessageBox.Show(SharedStrings.S907, SharedStrings.S908, MessageBoxButton.YesNo, MessageBoxImage.Asterisk) != MessageBoxResult.Yes)
		{
			return;
		}
		try
		{
			string processPath = Environment.ProcessPath;
			if (!string.IsNullOrEmpty(processPath))
			{
				Process.Start(new ProcessStartInfo(processPath)
				{
					Arguments = "--relaunch",
					UseShellExecute = true
				});
			}
			System.Windows.Application.Current.Shutdown();
		}
		catch
		{
		}
	}

	[RelayCommand]
	private void OpenDataFolderWizard()
	{
		try
		{
			if (!(App.Services.GetService(typeof(SetupWizardWindow)) is SetupWizardWindow setupWizardWindow))
			{
				return;
			}
			setupWizardWindow.Owner = System.Windows.Application.Current?.MainWindow;
			setupWizardWindow.ShowDialog();
			string result = setupWizardWindow.Result;
			if (!string.IsNullOrEmpty(result))
			{
				string processPath = Environment.ProcessPath;
				if (!string.IsNullOrEmpty(processPath))
				{
					Process.Start(new ProcessStartInfo(processPath)
					{
						UseShellExecute = true,
						Arguments = "--data-root \"" + result + "\""
					});
				}
				System.Windows.Application.Current.Shutdown();
			}
		}
		catch
		{
		}
	}

	[RelayCommand]
	private void Save()
	{
		_store.Save(Options);
	}

	[RelayCommand]
	private void Reload()
	{
		Options = _store.Load();
	}

	[RelayCommand]
	private void PickHighlightColor()
	{
		System.Drawing.Color color = ParseHexOrDefault(Options.Search.HighlightColor);
		using ColorDialog colorDialog = new ColorDialog
		{
			FullOpen = true,
			AnyColor = true,
			Color = color
		};
		if (colorDialog.ShowDialog() == DialogResult.OK)
		{
			System.Drawing.Color color2 = colorDialog.Color;
			string text = $"#{color2.R:X2}{color2.G:X2}{color2.B:X2}";
			Log.Information("HighlightColor: user picked {Hex} (was {Was})", text, Options.Search.HighlightColor);
			Options.Search.HighlightColor = text;
			OnPropertyChanged("Options");
			OnPropertyChanged("HighlightColorBrush");
			_store.Save(Options);
			PdfJsHost.BroadcastHighlightColor(text);
		}
	}

	[RelayCommand]
	private void ResetHighlightColor()
	{
		string text = "#FFD500";
		if (!string.Equals(Options.Search.HighlightColor, text, StringComparison.OrdinalIgnoreCase))
		{
			Log.Information("HighlightColor: reset to default {Hex} (was {Was})", text, Options.Search.HighlightColor);
			Options.Search.HighlightColor = text;
			OnPropertyChanged("Options");
			OnPropertyChanged("HighlightColorBrush");
			_store.Save(Options);
			PdfJsHost.BroadcastHighlightColor(text);
		}
	}

	[RelayCommand]
	private void ResetViewSettings()
	{
		ViewOptions viewOptions = new ViewOptions();
		Options.View.MainWindowPlacement = new WindowPlacementOptions();
		Options.View.LibraryCatalogRatio = viewOptions.LibraryCatalogRatio;
		Options.View.ManualResize = viewOptions.ManualResize;
		Options.View.ExplorerBarWidth = viewOptions.ExplorerBarWidth;
		Options.View.PercentSplitBarInLeft = viewOptions.PercentSplitBarInLeft;
		Options.View.PinResultList = viewOptions.PinResultList;
		Options.View.CountScroll = viewOptions.CountScroll;
		Options.View.NavPaneOpen = viewOptions.NavPaneOpen;
		Options.View.ChromeAutoHide = viewOptions.ChromeAutoHide;
		SelectedTheme = viewOptions.Theme;
		UnifiedSearchLayout = viewOptions.UnifiedSearchLayout;
		ShowRowDetails = viewOptions.ShowRowDetails;
		ShowSearchHint = viewOptions.ShowSearchHint;
		ShowPerformanceHints = viewOptions.ShowPerformanceHints;
		ShowPageRail = viewOptions.ShowPageRail;
		ShowHitPagesStrip = viewOptions.ShowHitPagesStrip;
		PersistSessionState = viewOptions.PersistSessionState;
		RegionCopyDpi = viewOptions.RegionCopyDpi;
		SelectedMissingBookAction = viewOptions.MissingBookAction;
		_store.Save(Options);
		Log.Information("View (display + layout) settings reset to defaults");
	}

	private static System.Drawing.Color ParseHexOrDefault(string? hex)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(hex))
			{
				return System.Drawing.Color.Gold;
			}
			string text;
			if (!hex.StartsWith('#'))
			{
				text = hex;
			}
			else
			{
				text = hex.Substring(1, hex.Length - 1);
			}
			string text2 = text;
			return System.Drawing.Color.FromArgb(255, Convert.ToInt32(text2.Substring(0, 2), 16), Convert.ToInt32(text2.Substring(2, 2), 16), Convert.ToInt32(text2.Substring(4, 2), 16));
		}
		catch
		{
			return System.Drawing.Color.Gold;
		}
	}

	[RelayCommand]
	private async Task CheckForUpdates()
	{
		if (!_updates.IsEnabled)
		{
			return;
		}
		try
		{
			IsCheckingUpdate = true;
			UpdateStatus = SharedStrings.S909;
			UpdateReleaseNotes = string.Empty;
			_pendingUpdate = await _updates.CheckAsync();
			if (_pendingUpdate == null)
			{
				UpdateStatus = SharedStrings.S2309 + CurrentVersion + ")";
				HasPendingUpdate = false;
				return;
			}
			UpdateStatus = $"{SharedStrings.S2310}{_pendingUpdate.TargetFullRelease.Version}";
			HasPendingUpdate = true;
			string target = _pendingUpdate.TargetFullRelease.Version.ToString();
			Task.Run(async delegate
			{
				try
				{
					string notes = await _updates.GetCumulativeReleaseNotesAsync(_updates.CurrentVersion, target).ConfigureAwait(continueOnCapturedContext: false);
					if (!string.IsNullOrWhiteSpace(notes))
					{
						System.Windows.Application.Current?.Dispatcher.BeginInvoke((Func<string>)(() => UpdateReleaseNotes = notes));
					}
				}
				catch (Exception exception)
				{
					Log.Warning(exception, "Settings: cumulative release-notes fetch failed");
				}
			});
		}
		finally
		{
			IsCheckingUpdate = false;
		}
	}

	[RelayCommand]
	private async Task InstallUpdate()
	{
		if (_pendingUpdate == null)
		{
			return;
		}
		try
		{
			UpdateStatus = SharedStrings.S912;
			UpdateProgress = 0;
			await _updates.DownloadAsync(_pendingUpdate, new Progress<int>(delegate(int p)
			{
				UpdateProgress = p;
			}));
			UpdateStatus = SharedStrings.S913;
			_updates.ApplyAndRestart(_pendingUpdate);
		}
		catch (Exception ex)
		{
			UpdateStatus = SharedStrings.S2311 + ex.Message;
		}
	}

	[RelayCommand]
	private void Uninstall()
	{
		if (System.Windows.MessageBox.Show(SharedStrings.S915, SharedStrings.S916, MessageBoxButton.YesNo, MessageBoxImage.Exclamation) != MessageBoxResult.Yes)
		{
			return;
		}
		try
		{
			File.Delete(UninstallCleanup.DeleteDataMarkerPath);
		}
		catch
		{
		}
		string directoryName = Path.GetDirectoryName(_paths.AppPath);
		if (UninstallCleanup.IsLocalDeletableDataRoot(directoryName))
		{
			switch (System.Windows.MessageBox.Show(SharedStrings.S2312 + directoryName + "\n\n" + SharedStrings.S918, SharedStrings.S919, MessageBoxButton.YesNoCancel, MessageBoxImage.Exclamation))
			{
			case MessageBoxResult.Cancel:
				return;
			case MessageBoxResult.Yes:
				try
				{
					Directory.CreateDirectory(UninstallCleanup.AppDataDir);
					File.WriteAllText(UninstallCleanup.DeleteDataMarkerPath, directoryName);
				}
				catch
				{
				}
				break;
			}
		}
		if (_updates.BeginUninstall())
		{
			System.Windows.Application.Current.Shutdown();
		}
		else
		{
			System.Windows.MessageBox.Show(SharedStrings.S920, SharedStrings.S916, MessageBoxButton.OK, MessageBoxImage.Asterisk);
		}
	}

	private void RefreshOcrEngineStatus()
	{
		OcrEngineInstalled = _ocr.IsInstalled;
		if (!OcrInstallBusy)
		{
			string ocrEngineStatus;
			if (!OcrEngineInstalled)
			{
				ocrEngineStatus = SharedStrings.S925;
			}
			else
			{
				string s = SharedStrings.S2313;
				string installedVersion = _ocr.InstalledVersion;
				ocrEngineStatus = s + ((installedVersion != null && installedVersion.Length > 0) ? (" (" + installedVersion + ")") : "") + SharedStrings.S2314;
			}
			OcrEngineStatus = ocrEngineStatus;
		}
	}

	[RelayCommand]
	private async Task InstallOcrEngine()
	{
		if (OcrInstallBusy)
		{
			return;
		}
		try
		{
			OcrInstallBusy = true;
			OcrInstallProgress = 0;
			Progress<OcrInstallProgress> progress = new Progress<OcrInstallProgress>(delegate(OcrInstallProgress p)
			{
				OcrInstallProgress = p.Percent;
				OcrEngineStatus = p.Message;
			});
			if (_ocr.IsBundleAvailable)
			{
				double value = (double)_ocr.BundledZipSizeBytes / 1024.0 / 1024.0;
				if (HebrewMessageBox.Show($"{SharedStrings.S2315}{value:F0}MB)?\n\n" + SharedStrings.S927, SharedStrings.S928, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes) != MessageBoxResult.Yes)
				{
					RefreshOcrEngineStatus();
					return;
				}
				await _ocr.InstallFromBundleAsync(progress);
				OcrEngineInstalled = _ocr.IsInstalled;
				OcrEngineStatus = (OcrEngineInstalled ? SharedStrings.S929 : SharedStrings.S930);
				return;
			}
			OcrEngineStatus = SharedStrings.S931;
			OcrEngineInstaller.OcrRelease release = await _ocr.CheckLatestAsync();
			if ((object)release == null)
			{
				OcrEngineStatus = SharedStrings.S932;
				return;
			}
			double value2 = (double)release.SizeBytes / 1024.0 / 1024.0;
			if (HebrewMessageBox.Show($"{SharedStrings.S2316}{release.Tag}{SharedStrings.S2317}{value2:F0}MB)?\n\n" + SharedStrings.S934, SharedStrings.S928, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes) != MessageBoxResult.Yes)
			{
				RefreshOcrEngineStatus();
				return;
			}
			await _ocr.InstallAsync(release, progress);
			OcrEngineInstalled = _ocr.IsInstalled;
			OcrEngineStatus = (OcrEngineInstalled ? (SharedStrings.S2318 + (_ocr.InstalledVersion ?? release.Tag) + SharedStrings.S2319) : SharedStrings.S930);
		}
		catch (Exception ex)
		{
			Log.Warning(ex, "OCR engine install failed");
			OcrEngineStatus = SharedStrings.S2320 + ex.Message;
		}
		finally
		{
			OcrInstallBusy = false;
			OcrInstallProgress = 0;
		}
	}

	[RelayCommand]
	private void OpenOcrReleasePage()
	{
		try
		{
			Process.Start(new ProcessStartInfo("https://github.com/HebrewBooks-2026/win-ocr/releases/latest")
			{
				UseShellExecute = true
			});
		}
		catch (Exception exception)
		{
			Log.Warning(exception, "Open OCR releases page failed");
		}
	}

	private void LoadRasheyTevotEntries()
	{
		RasheyTevotEntries.Clear();
		foreach (KeyValuePair<string, IReadOnlyList<string>> item in _rasheyTevotMap.Entries.OrderBy<KeyValuePair<string, IReadOnlyList<string>>, string>((KeyValuePair<string, IReadOnlyList<string>> k) => k.Key, StringComparer.Ordinal))
		{
			RasheyTevotEntries.Add(new RasheyTevotEntry(item.Key, string.Join(" | ", item.Value)));
		}
		RasheyTevotStatus = $"{SharedStrings.S2321}{RasheyTevotEntries.Count}{SharedStrings.S2322}";
	}

	[RelayCommand]
	private void AddRasheyTevotEntry()
	{
		RasheyTevotEntry rasheyTevotEntry = new RasheyTevotEntry(string.Empty, string.Empty);
		RasheyTevotEntries.Add(rasheyTevotEntry);
		SelectedRasheyTevotEntry = rasheyTevotEntry;
	}

	[RelayCommand]
	private void RemoveRasheyTevotEntry()
	{
		if (SelectedRasheyTevotEntry != null)
		{
			RasheyTevotEntries.Remove(SelectedRasheyTevotEntry);
			SelectedRasheyTevotEntry = null;
		}
	}

	[RelayCommand]
	private void ReloadRasheyTevot()
	{
		try
		{
			RasheyTevotMap rasheyTevotMap = RasheyTevotMap.LoadFromFile(_paths.RasheyTevotPath);
			if (rasheyTevotMap != RasheyTevotMap.Empty)
			{
				_rasheyTevotMap.ReplaceWith(rasheyTevotMap.Entries.ToDictionary<KeyValuePair<string, IReadOnlyList<string>>, string, IReadOnlyList<string>>((KeyValuePair<string, IReadOnlyList<string>> k) => k.Key, (KeyValuePair<string, IReadOnlyList<string>> v) => v.Value));
			}
			else
			{
				_rasheyTevotMap.ReplaceWith(new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal));
			}
			LoadRasheyTevotEntries();
		}
		catch (Exception ex)
		{
			RasheyTevotStatus = SharedStrings.S2323 + ex.Message;
		}
	}

	[RelayCommand]
	private void SaveRasheyTevot()
	{
		try
		{
			Dictionary<string, IReadOnlyList<string>> dictionary = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
			foreach (RasheyTevotEntry rasheyTevotEntry in RasheyTevotEntries)
			{
				string text = (rasheyTevotEntry.Acronym ?? string.Empty).Trim();
				if (text.Length != 0)
				{
					string[] array = (from s in (rasheyTevotEntry.Expansions ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries)
						select s.Trim() into s
						where s.Length > 0
						select s).ToArray();
					if (array.Length != 0)
					{
						dictionary[text] = array;
					}
				}
			}
			_rasheyTevotMap.ReplaceWith(dictionary);
			_rasheyTevotMap.Save(_paths.RasheyTevotPath);
			RasheyTevotStatus = $"{SharedStrings.S2324}{dictionary.Count}{SharedStrings.S2325}{_paths.RasheyTevotPath}";
		}
		catch (Exception ex)
		{
			RasheyTevotStatus = SharedStrings.S2326 + ex.Message;
		}
	}

	[RelayCommand]
	private async Task ExportTocsAsync()
	{
		Microsoft.Win32.SaveFileDialog dlg = new Microsoft.Win32.SaveFileDialog
		{
			Title = SharedStrings.S941,
			Filter = "JSON|*.json",
			FileName = "HebrewBooks-TOC-" + DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".json",
			DefaultExt = ".json"
		};
		if (dlg.ShowDialog() != true)
		{
			return;
		}
		try
		{
			int value = await _tocBundle.ExportAsync(dlg.FileName);
			TocBundleStatus = $"{SharedStrings.S2327}{value}{SharedStrings.S2328}{dlg.FileName}";
		}
		catch (Exception ex)
		{
			TocBundleStatus = SharedStrings.S2329 + ex.Message;
		}
	}

	[RelayCommand]
	private async Task ImportTocsAsync()
	{
		Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog
		{
			Title = SharedStrings.S944,
			Filter = "JSON|*.json"
		};
		if (openFileDialog.ShowDialog() != true)
		{
			return;
		}
		int num;
		switch (HebrewMessageBox.Show(SharedStrings.S945, SharedStrings.S944, MessageBoxButton.YesNoCancel, MessageBoxImage.Question))
		{
		case MessageBoxResult.Cancel:
			return;
		default:
			num = 1;
			break;
		case MessageBoxResult.Yes:
			num = 0;
			break;
		}
		ImportMode mode = (ImportMode)num;
		try
		{
			(int, int) obj = await _tocBundle.ImportAsync(openFileDialog.FileName, mode);
			int item = obj.Item1;
			int item2 = obj.Item2;
			TocBundleStatus = $"{SharedStrings.S2330}{item}{SharedStrings.S2331}{item2}{SharedStrings.S2332}";
		}
		catch (Exception ex)
		{
			TocBundleStatus = SharedStrings.S2333 + ex.Message;
		}
	}





}
