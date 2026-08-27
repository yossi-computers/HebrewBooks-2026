using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using HebrewBooks.Core.Abstractions;
using HebrewBooks.Core.Models;
using HebrewBooks.Search;
using HebrewBooks.Services.Background;
using HebrewBooks.Services.Downloader;
using HebrewBooks.Services.Otzraya;
using HebrewBooks.UI.Messages;
using HebrewBooks.UI.Resources;
using HebrewBooks.UI.Views;

namespace HebrewBooks.UI.ViewModels;

public partial class DownloaderViewModel : ObservableObject
{
	private readonly BookDownloadService _service;

	private readonly BackgroundProcessorService _bgProcessor;

	private readonly ISearchEngine _engine;

	private readonly IPathResolver _paths;

	private readonly EngineOptions _engineOptions;

	private readonly OtzrayaSyncService _otzrayaSync;

	private readonly OtzrayaCatalogIndexer _otzrayaIndexer;

	private readonly ICatalogRepository _catalog;

	private readonly PublishedSyncService _publishedSync;

	[ObservableProperty]
	[NotifyPropertyChangedFor("PendingCount")]
	private int _maxLocal;

	[ObservableProperty]
	[NotifyPropertyChangedFor("PendingCount")]
	private int _maxOnSite;

	[ObservableProperty]
	[NotifyCanExecuteChangedFor("StartCommand")]
	[NotifyCanExecuteChangedFor("CancelCommand")]
	[NotifyCanExecuteChangedFor("RebuildIndexCommand")]
	[NotifyCanExecuteChangedFor("RefreshCommand")]
	[NotifyCanExecuteChangedFor("LoadCandidatesCommand")]
	[NotifyCanExecuteChangedFor("DownloadSelectedCommand")]
	[NotifyCanExecuteChangedFor("ScanAndCompleteCommand")]
	private bool _isRunning;

	[ObservableProperty]
	private int _processed;

	[ObservableProperty]
	private int _succeeded;

	[ObservableProperty]
	private int _failed;

	[ObservableProperty]
	private int? _currentId;

	[ObservableProperty]
	private string _statusText = string.Empty;

	[ObservableProperty]
	private bool _statusIsError;

	private CancellationTokenSource? _cts;

	private static readonly Random _rng = Random.Shared;

	[ObservableProperty]
	private bool _isIndexing;

	[ObservableProperty]
	private bool _indexJobActive;

	[ObservableProperty]
	private string _ixName = "";

	[ObservableProperty]
	private string _ixLocation = "";

	[ObservableProperty]
	private string _ixWords = "";

	[ObservableProperty]
	private string _ixDocs = "";

	[ObservableProperty]
	private string _ixToIndexFiles = "";

	[ObservableProperty]
	private string _ixToIndexMb = "";

	[ObservableProperty]
	private string _ixIndexedFiles = "";

	[ObservableProperty]
	private string _ixIndexedMb = "";

	[ObservableProperty]
	private string _ixElapsed = "";

	[ObservableProperty]
	private string _ixRemaining = "";

	[ObservableProperty]
	private string _ixDiskFree = "";

	[ObservableProperty]
	private int _ixPercent;

	[ObservableProperty]
	private string _ixStep = "";

	[ObservableProperty]
	private string _ixCurFileName = "";

	[ObservableProperty]
	private string _ixCurFileLocation = "";

	[ObservableProperty]
	private string _ixCurFileType = "";

	[ObservableProperty]
	private string _ixCurFileSize = "";

	[ObservableProperty]
	private string _ixCurFileWords = "";

	[ObservableProperty]
	private int _ixCurFilePercent;

	private DispatcherTimer? _ixHideTimer;

	[ObservableProperty]
	[NotifyCanExecuteChangedFor("DownloadSelectedCommand")]
	private bool _showCandidates;

	[ObservableProperty]
	private bool _isLoadingNames;

	[ObservableProperty]
	private string _candidateFilter = string.Empty;

	[ObservableProperty]
	private string _otzrayaSyncStatus = string.Empty;

	[ObservableProperty]
	[NotifyCanExecuteChangedFor("SyncOtzrayaCommand")]
	[NotifyCanExecuteChangedFor("CancelOtzrayaSyncCommand")]
	private bool _otzrayaSyncing;

	[ObservableProperty]
	private int _otzrayaSyncProgressDone;

	[ObservableProperty]
	private int _otzrayaSyncProgressTotal;

	[ObservableProperty]
	private string _otzrayaSyncCurrentFile = string.Empty;

	private CancellationTokenSource? _otzrayaSyncCts;

	[ObservableProperty]
	[NotifyCanExecuteChangedFor("CheckPublishedCommand")]
	[NotifyCanExecuteChangedFor("SyncPublishedCommand")]
	[NotifyCanExecuteChangedFor("CancelPublishedSyncCommand")]
	private bool _publishedBusy;

	[ObservableProperty]
	private int _publishedProgressDone;

	[ObservableProperty]
	private int _publishedProgressTotal;

	[ObservableProperty]
	private string _publishedStatus = string.Empty;

	[ObservableProperty]
	private int _publishedNewCount = -1;

	private CancellationTokenSource? _publishedCts;

	[ObservableProperty]
	[NotifyCanExecuteChangedFor("ScanOtzrayaCatalogCommand")]
	[NotifyCanExecuteChangedFor("CancelOtzrayaScanCommand")]
	private bool _otzrayaScanning;

	[ObservableProperty]
	private int _otzrayaScanProgressDone;

	[ObservableProperty]
	private int _otzrayaScanProgressTotal;

	private CancellationTokenSource? _otzrayaScanCts;





















	public int PendingCount => Math.Max(0, MaxOnSite - MaxLocal);

	public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

	public ObservableCollection<DownloadCandidate> Candidates { get; } = new ObservableCollection<DownloadCandidate>();

	public int SelectedCount => Candidates.Count((DownloadCandidate c) => c.IsSelected);



































































	public DownloaderViewModel(BookDownloadService service, BackgroundProcessorService bgProcessor, ISearchEngine engine, IPathResolver paths, EngineOptions engineOptions, OtzrayaSyncService otzrayaSync, OtzrayaCatalogIndexer otzrayaIndexer, ICatalogRepository catalog, PublishedSyncService publishedSync)
	{
		DownloaderViewModel downloaderViewModel = this;
		_service = service;
		_bgProcessor = bgProcessor;
		_engine = engine;
		_paths = paths;
		_engineOptions = engineOptions;
		_otzrayaSync = otzrayaSync;
		_otzrayaIndexer = otzrayaIndexer;
		_catalog = catalog;
		_publishedSync = publishedSync;
		Dispatcher dispatcher = Application.Current?.Dispatcher;
		_bgProcessor.JobStarted += delegate(object? _, BackgroundProcessorService.Job job)
		{
			RunOnUi(dispatcher, delegate
			{
				downloaderViewModel.AddLog("⚙ " + job.Title + SharedStrings.S2029);
				if ((job is IndexBuildJob || job is TargetedIndexBuildJob) ? true : false)
				{
					downloaderViewModel._ixHideTimer?.Stop();
					downloaderViewModel.ResetIndexPanel();
					downloaderViewModel.IxStep = SharedStrings.S622;
					downloaderViewModel.IsIndexing = true;
					downloaderViewModel.IndexJobActive = true;
				}
			});
		};
		_bgProcessor.JobProgress += delegate(object? _, JobProgress p)
		{
			RunOnUi(dispatcher, delegate
			{
				downloaderViewModel.StatusText = $"{p.Job.Title}: {p.Percent * 100.0:0}%";
			});
		};
		_bgProcessor.IndexProgress += delegate(object? _, IndexProgressReport r)
		{
			RunOnUi(dispatcher, delegate
			{
				downloaderViewModel.ApplyIndexProgress(r);
			});
		};
		_bgProcessor.JobCompleted += delegate(object? _, JobCompletion c)
		{
			RunOnUi(dispatcher, delegate
			{
				BackgroundProcessorService.Job job;
				if (c.Error != null)
				{
					downloaderViewModel.AddLog("✗ " + c.Job.Title + SharedStrings.S2030 + c.Error.Message);
					downloaderViewModel.StatusText = c.Job.Title + SharedStrings.S2031 + c.Error.Message;
					downloaderViewModel.StatusIsError = true;
					job = c.Job;
					if ((job is IndexBuildJob || job is TargetedIndexBuildJob) ? true : false)
					{
						downloaderViewModel.IxStep = SharedStrings.S625;
					}
				}
				else if (c.Cancelled)
				{
					downloaderViewModel.AddLog("⏹ " + c.Job.Title + SharedStrings.S2032);
					downloaderViewModel.StatusText = c.Job.Title + SharedStrings.S2033;
					job = c.Job;
					if ((job is IndexBuildJob || job is TargetedIndexBuildJob) ? true : false)
					{
						downloaderViewModel.IxStep = SharedStrings.S628;
					}
				}
				else
				{
					downloaderViewModel.AddLog("✓ " + c.Job.Title + SharedStrings.S2034);
					downloaderViewModel.StatusText = c.Job.Title + SharedStrings.S2035;
					job = c.Job;
					if ((job is IndexBuildJob || job is TargetedIndexBuildJob) ? true : false)
					{
						downloaderViewModel.IxStep = SharedStrings.S631;
						downloaderViewModel.IxPercent = 100;
						downloaderViewModel.IxCurFilePercent = 100;
					}
				}
				job = c.Job;
				if ((job is IndexBuildJob || job is TargetedIndexBuildJob) ? true : false)
				{
					downloaderViewModel.IndexJobActive = false;
					if (downloaderViewModel._ixHideTimer == null)
					{
						downloaderViewModel._ixHideTimer = new DispatcherTimer
						{
							Interval = TimeSpan.FromSeconds(8.0)
						};
					}
					downloaderViewModel._ixHideTimer.Tick -= downloaderViewModel.OnIxHideTick;
					downloaderViewModel._ixHideTimer.Tick += downloaderViewModel.OnIxHideTick;
					downloaderViewModel._ixHideTimer.Start();
				}
			});
		};
	}

	private void OnIxHideTick(object? sender, EventArgs e)
	{
		_ixHideTimer?.Stop();
		IsIndexing = false;
	}

	[RelayCommand]
	private void CancelIndex()
	{
		_bgProcessor.CancelCurrentJob();
	}

	private void ApplyIndexProgress(IndexProgressReport r)
	{
		IsIndexing = true;
		IxName = r.IndexName;
		IxLocation = r.IndexLocation;
		IxCurFileName = r.CurrentFileName ?? "";
		IxCurFileLocation = r.CurrentFileLocation ?? "";
		if (r.Step == 99)
		{
			IxStep = ((r.FilesRead > 0) ? $"{SharedStrings.S2036}{r.FilesToIndex:N0}{SharedStrings.S2037}{r.FilesRead:N0}" : $"{SharedStrings.S2038}{r.FilesToIndex:N0}");
			IxPercent = Math.Clamp(r.PercentDone, 0, 100);
			string ixWords = (IxDocs = "");
			IxWords = ixWords;
			string text2 = (IxIndexedMb = "");
			string text4 = (IxIndexedFiles = text2);
			ixWords = (IxToIndexMb = text4);
			IxToIndexFiles = ixWords;
			text4 = (IxDiskFree = "");
			ixWords = (IxRemaining = text4);
			IxElapsed = ixWords;
			text4 = (IxCurFileWords = "");
			ixWords = (IxCurFileSize = text4);
			IxCurFileType = ixWords;
			IxCurFilePercent = 0;
		}
		else
		{
			IxWords = r.WordsInIndex.ToString("N0");
			IxDocs = r.DocsInIndex.ToString("N0");
			IxToIndexFiles = $"{r.FilesToIndex:N0} files";
			IxToIndexMb = $"{(double)r.KbToIndex / 1024.0:N0} MB";
			IxIndexedFiles = $"{r.FilesRead:N0} files";
			IxIndexedMb = $"{(double)r.KbRead / 1024.0:N0} MB";
			IxElapsed = FormatDuration(r.ElapsedSeconds);
			IxRemaining = FormatDuration(r.EstRemainingSeconds);
			IxDiskFree = $"{(double)r.DiskFreeBytes / 1073741824.0:N0} GB";
			IxPercent = Math.Clamp(r.PercentDone, 0, 100);
			IxStep = StepName(r.Step);
			IxCurFileName = r.CurrentFileName ?? "";
			IxCurFileLocation = r.CurrentFileLocation ?? "";
			IxCurFileType = r.CurrentFileType ?? "";
			IxCurFileSize = ((r.CurrentFileSizeBytes > 0) ? r.CurrentFileSizeBytes.ToString("N0") : "");
			IxCurFileWords = ((r.CurrentFileWords > 0) ? r.CurrentFileWords.ToString("N0") : "");
			IxCurFilePercent = Math.Clamp(r.CurrentFilePercent, 0, 100);
		}
	}

	private void ResetIndexPanel()
	{
		string text = (IxToIndexMb = "");
		string text3 = (IxToIndexFiles = text);
		string text5 = (IxDocs = text3);
		string text7 = (IxWords = text5);
		string ixName = (IxLocation = text7);
		IxName = ixName;
		text = (IxStep = "");
		text3 = (IxDiskFree = text);
		text5 = (IxRemaining = text3);
		text7 = (IxElapsed = text5);
		ixName = (IxIndexedMb = text7);
		IxIndexedFiles = ixName;
		text3 = (IxCurFileWords = "");
		text5 = (IxCurFileSize = text3);
		text7 = (IxCurFileType = text5);
		ixName = (IxCurFileLocation = text7);
		IxCurFileName = ixName;
		int ixPercent = (IxCurFilePercent = 0);
		IxPercent = ixPercent;
	}

	private static string StepName(int step)
	{
		return step switch
		{
			1 => SharedStrings.S634, 
			2 => SharedStrings.S635, 
			3 => SharedStrings.S636, 
			4 => SharedStrings.S637, 
			5 => SharedStrings.S638, 
			6 => SharedStrings.S639, 
			7 => SharedStrings.S640, 
			8 => SharedStrings.S641, 
			_ => "", 
		};
	}

	private static string FormatDuration(int seconds)
	{
		if (seconds < 0)
		{
			seconds = 0;
		}
		TimeSpan timeSpan = TimeSpan.FromSeconds(seconds);
		if (timeSpan.TotalHours >= 1.0)
		{
			return $"{(int)timeSpan.TotalHours}:{timeSpan.Minutes:00}:{timeSpan.Seconds:00}";
		}
		return $"{timeSpan.Minutes:00}:{timeSpan.Seconds:00}";
	}

	private static void RunOnUi(Dispatcher? d, Action a)
	{
		if (d == null || d.CheckAccess())
		{
			a();
		}
		else
		{
			d.BeginInvoke(a);
		}
	}

	[RelayCommand(CanExecute = "CanInteract")]
	public async Task RefreshAsync()
	{
		StatusText = SharedStrings.S642;
		StatusIsError = false;
		try
		{
			Task<int> localTask = _service.GetMaxLocalAsync();
			Task<int> siteTask = _service.GetMaxOnSiteAsync();
			await Task.WhenAll<int>(localTask, siteTask);
			MaxLocal = localTask.Result;
			MaxOnSite = siteTask.Result;
			StatusText = ((PendingCount == 0) ? SharedStrings.S643 : $"{PendingCount}{SharedStrings.S2039}");
		}
		catch (Exception ex)
		{
			StatusText = SharedStrings.S2117 + ex.Message;
			StatusIsError = true;
		}
	}

	private bool CanInteract()
	{
		return !IsRunning;
	}

	private bool CanCancel()
	{
		return IsRunning;
	}

	[RelayCommand(CanExecute = "CanInteract")]
	private async Task StartAsync()
	{
		if (PendingCount <= 0)
		{
			await RefreshAsync();
			if (PendingCount <= 0)
			{
				return;
			}
		}
		IsRunning = true;
		Processed = 0;
		Succeeded = 0;
		Failed = 0;
		StatusIsError = false;
		StatusText = SharedStrings.S646;
		AddLog($"{SharedStrings.S2040}{MaxLocal + 1}–{MaxOnSite} ({PendingCount}{SharedStrings.S2041}{_paths.PdfsRoot}");
		_cts = new CancellationTokenSource();
		try
		{
			AddLog(SharedStrings.S648);
			try
			{
				int num = await _service.SyncDiskFilesToCatalogAsync(null, _cts.Token);
				AddLog((num > 0) ? $"{SharedStrings.S2042}{num}{SharedStrings.S2043}" : SharedStrings.S650);
				MaxLocal = await _service.GetMaxLocalAsync();
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception ex2)
			{
				AddLog(SharedStrings.S2044 + ex2.Message);
			}
			List<int> list = new List<int>();
			for (int i = MaxLocal + 1; i <= MaxOnSite; i++)
			{
				list.Add(i);
			}
			await RunDownloadAsync(list, _cts.Token);
			StatusText = $"{SharedStrings.S2045}{Succeeded}{SharedStrings.S2046}{Failed}{SharedStrings.S2047}";
			MaxLocal = await _service.GetMaxLocalAsync();
		}
		catch (OperationCanceledException)
		{
			StatusText = $"{SharedStrings.S2048}{Processed}{SharedStrings.S2049}{Succeeded}{SharedStrings.S2050}";
		}
		catch (Exception ex4)
		{
			StatusText = SharedStrings.S9066 + ex4.Message;
			StatusIsError = true;
		}
		finally
		{
			CurrentId = null;
			IsRunning = false;
			_cts?.Dispose();
			_cts = null;
			if (Succeeded > 0)
			{
				WeakReferenceMessenger.Default.Send(new CatalogChangedMessage(Succeeded));
			}
		}
	}

	[RelayCommand(CanExecute = "CanInteract")]
	private async Task ScanAndCompleteAsync()
	{
		IsRunning = true;
		Processed = 0;
		Succeeded = 0;
		Failed = 0;
		StatusIsError = false;
		StatusText = SharedStrings.S655;
		AddLog(SharedStrings.S656);
		_cts = new CancellationTokenSource();
		try
		{
			AddLog(SharedStrings.S657);
			try
			{
				int num = await _service.SyncDiskFilesToCatalogAsync(null, _cts.Token);
				AddLog((num > 0) ? $"{SharedStrings.S2051}{num}{SharedStrings.S2052}" : SharedStrings.S650);
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception ex2)
			{
				AddLog(SharedStrings.S2053 + ex2.Message);
			}
			int maxOnSite = 0;
			try
			{
				maxOnSite = (MaxOnSite = await _service.GetMaxOnSiteAsync(_cts.Token));
			}
			catch (Exception ex3)
			{
				AddLog(SharedStrings.S2054 + ex3.Message + SharedStrings.S2055);
			}
			AddLog(SharedStrings.S660);
			Progress<int> listProgress = new Progress<int>(delegate(int n)
			{
				StatusText = $"{SharedStrings.S2056}{n:N0}{SharedStrings.S2057}";
			});
			CompletionScan scan = await _service.ScanForMissingAsync(maxOnSite, listProgress, _cts.Token);
			MaxLocal = await _service.GetMaxLocalAsync();
			AddLog($"{SharedStrings.S2058}{scan.MirrorCount:N0}{SharedStrings.S2059}{scan.DiskCount:N0}{SharedStrings.S2060}{scan.Missing.Count:N0}");
			if (scan.Missing.Count == 0)
			{
				StatusText = SharedStrings.S663;
				return;
			}
			AddLog($"{SharedStrings.S2061}{scan.Missing.Count:N0}{SharedStrings.S2062}");
			await PopulateMissingCandidatesAsync(scan.Missing, _cts.Token);
			ShowCandidates = true;
			StatusText = $"{SharedStrings.S2063}{scan.Missing.Count:N0}{SharedStrings.S2064}";
			AddLog($"{SharedStrings.S2065}{Candidates.Count:N0}{SharedStrings.S2066}");
		}
		catch (OperationCanceledException)
		{
			StatusText = SharedStrings.S2067;
		}
		catch (Exception ex5)
		{
			StatusText = SharedStrings.S9067 + ex5.Message;
			StatusIsError = true;
		}
		finally
		{
			CurrentId = null;
			IsRunning = false;
			_cts?.Dispose();
			_cts = null;
			if (Succeeded > 0)
			{
				WeakReferenceMessenger.Default.Send(new CatalogChangedMessage(Succeeded));
			}
		}
	}

	private async Task RunDownloadAsync(IReadOnlyList<int> ids, CancellationToken ct)
	{
		int total = ids.Count;
		List<string> freshPdfs = new List<string>();
		IReadOnlySet<int> mirrorHits = new HashSet<int>();
		try
		{
			AddLog($"{SharedStrings.S2068}{total}{SharedStrings.S2069}");
			Progress<int> progress = new Progress<int>(delegate(int n)
			{
				StatusText = $"{SharedStrings.S2070}{n}/{total}";
			});
			mirrorHits = await _service.MirrorPrefetchAsync(ids, progress, ct);
			AddLog((mirrorHits.Count > 0) ? $"✓ {mirrorHits.Count}/{total}{SharedStrings.S2071}" : SharedStrings.S672);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex2)
		{
			AddLog(SharedStrings.S2072 + ex2.Message + SharedStrings.S2073);
		}
		foreach (int item in mirrorHits)
		{
			string text = Path.Combine(_paths.PdfsRoot, item + ".pdf");
			if (File.Exists(text))
			{
				freshPdfs.Add(text);
			}
		}
		try
		{
			for (int i = 0; i < ids.Count; i++)
			{
				ct.ThrowIfCancellationRequested();
				int id = ids[i];
				CurrentId = id;
				AddLog($"{SharedStrings.S2074}{id}...");
				StatusText = $"{SharedStrings.S2075}{id} ({Processed + 1}/{total})";
				DownloadOutcome downloadOutcome;
				try
				{
					downloadOutcome = await _service.DownloadBookAsync(id, ct);
				}
				catch (OperationCanceledException)
				{
					throw;
				}
				catch (Exception ex4)
				{
					downloadOutcome = new DownloadOutcome(id, Success: false, 0, null, ex4.Message);
				}
				Processed++;
				if (downloadOutcome.Success)
				{
					Succeeded++;
					string text2 = Path.Combine(_paths.PdfsRoot, id + ".pdf");
					long value = (File.Exists(text2) ? (new FileInfo(text2).Length / 1024) : 0);
					string value2 = (mirrorHits.Contains(id) ? SharedStrings.S676 : (downloadOutcome.WasAlreadyOnDisk ? SharedStrings.S677 : SharedStrings.S678));
					AddLog($"{value2} {id} ({value:N0} KB) — {downloadOutcome.Title ?? SharedStrings.S1081}");
					if (!downloadOutcome.WasAlreadyOnDisk && File.Exists(text2))
					{
						freshPdfs.Add(text2);
					}
				}
				else
				{
					Failed++;
					AddLog($"✗ {id} — {downloadOutcome.Error ?? SharedStrings.S9068}");
				}
				if (i < ids.Count - 1 && !downloadOutcome.WasAlreadyOnDisk)
				{
					int num = _rng.Next(18, 43);
					StatusText = $"{SharedStrings.S2076}{num}{SharedStrings.S2077}";
					try
					{
						await Task.Delay(TimeSpan.FromSeconds(num), ct);
					}
					catch (OperationCanceledException)
					{
						throw;
					}
				}
			}
		}
		finally
		{
			if (freshPdfs.Count > 0)
			{
				AddLog($"{SharedStrings.S2078}{freshPdfs.Count}{SharedStrings.S2079}");
				IndexSpec spec = new IndexSpec(_paths.IndexesRoot, new string[1] { _paths.PdfsRoot }, UseNativeEnumeration: true);
				try
				{
					await _bgProcessor.EnqueueAsync(new TargetedIndexBuildJob(spec, freshPdfs, Array.Empty<string>(), _engine));
				}
				catch
				{
				}
			}
		}
	}

	[RelayCommand(CanExecute = "CanInteract")]
	private async Task LoadCandidatesAsync()
	{
		if (PendingCount <= 0)
		{
			await RefreshAsync();
			if (PendingCount <= 0)
			{
				StatusText = SharedStrings.S681;
				return;
			}
		}
		IsRunning = true;
		IsLoadingNames = true;
		StatusIsError = false;
		Candidates.Clear();
		ShowCandidates = true;
		_cts = new CancellationTokenSource();
		int total = MaxOnSite - MaxLocal;
		AddLog($"{SharedStrings.S2080}{total}{SharedStrings.S2081}");
		try
		{
			int done = 0;
			for (int id = MaxLocal + 1; id <= MaxOnSite; id++)
			{
				_cts.Token.ThrowIfCancellationRequested();
				DownloadCandidateInfo downloadCandidateInfo = await _service.PeekBookInfoAsync(id, _cts.Token);
				DownloadCandidate downloadCandidate = new DownloadCandidate(downloadCandidateInfo.FileId, downloadCandidateInfo.BookName, downloadCandidateInfo.AuthorName)
				{
					IsSelected = false
				};
				downloadCandidate.PropertyChanged += delegate(object? _, PropertyChangedEventArgs e)
				{
					if (e.PropertyName == "IsSelected")
					{
						OnPropertyChanged("SelectedCount");
						DownloadSelectedCommand.NotifyCanExecuteChanged();
					}
				};
				Candidates.Add(downloadCandidate);
				done++;
				StatusText = $"{SharedStrings.S2082}{done}/{total}";
			}
			OnPropertyChanged("SelectedCount");
			StatusText = $"{SharedStrings.S2083}{Candidates.Count}{SharedStrings.S2084}";
			AddLog($"{SharedStrings.S2085}{Candidates.Count}{SharedStrings.S2086}");
		}
		catch (OperationCanceledException)
		{
			StatusText = $"{SharedStrings.S2087}{Candidates.Count}{SharedStrings.S2088}";
		}
		catch (Exception ex2)
		{
			StatusText = SharedStrings.S9069 + ex2.Message;
			StatusIsError = true;
		}
		finally
		{
			IsLoadingNames = false;
			IsRunning = false;
			_cts?.Dispose();
			_cts = null;
		}
	}

	private async Task PopulateMissingCandidatesAsync(IReadOnlyList<int> missing, CancellationToken ct)
	{
		Candidates.Clear();
		CandidateFilter = string.Empty;
		Dictionary<string, Book> byFileId = new Dictionary<string, Book>(StringComparer.Ordinal);
		for (int i = 0; i < missing.Count; i += 500)
		{
			ct.ThrowIfCancellationRequested();
			List<string> fileIds = (from id in missing.Skip(i).Take(500)
				select id.ToString()).ToList();
			foreach (Book item in await _catalog.FindByFileIdsAsync(fileIds, ct))
			{
				if (!string.IsNullOrEmpty(item.FileID))
				{
					byFileId[item.FileID] = item;
				}
			}
		}
		foreach (int item2 in missing)
		{
			byFileId.TryGetValue(item2.ToString(), out Book value);
			DownloadCandidate downloadCandidate = new DownloadCandidate(item2, value?.BookName ?? string.Empty, value?.AuthorName)
			{
				IsSelected = false
			};
			downloadCandidate.PropertyChanged += delegate(object? _, PropertyChangedEventArgs e)
			{
				if (e.PropertyName == "IsSelected")
				{
					OnPropertyChanged("SelectedCount");
					DownloadSelectedCommand.NotifyCanExecuteChanged();
				}
			};
			Candidates.Add(downloadCandidate);
		}
		OnPropertyChanged("SelectedCount");
	}

	[RelayCommand]
	private void SelectAllCandidates()
	{
		foreach (DownloadCandidate candidate in Candidates)
		{
			if (candidate.IsVisible)
			{
				candidate.IsSelected = true;
			}
		}
		OnPropertyChanged("SelectedCount");
		DownloadSelectedCommand.NotifyCanExecuteChanged();
	}

	[RelayCommand]
	private void SelectNoCandidates()
	{
		foreach (DownloadCandidate candidate in Candidates)
		{
			if (candidate.IsVisible)
			{
				candidate.IsSelected = false;
			}
		}
		OnPropertyChanged("SelectedCount");
		DownloadSelectedCommand.NotifyCanExecuteChanged();
	}

	private bool CanDownloadSelected()
	{
		if (!IsRunning && ShowCandidates)
		{
			return Candidates.Any((DownloadCandidate c) => c.IsSelected);
		}
		return false;
	}

	[RelayCommand(CanExecute = "CanDownloadSelected")]
	private async Task DownloadSelectedAsync()
	{
		List<int> ids = (from c in Candidates
			where c.IsSelected
			select c.FileId into x
			orderby x
			select x).ToList();
		if (ids.Count == 0)
		{
			return;
		}
		IsRunning = true;
		Processed = 0;
		Succeeded = 0;
		Failed = 0;
		StatusIsError = false;
		StatusText = $"{SharedStrings.S2089}{ids.Count}{SharedStrings.S2090}";
		AddLog($"{SharedStrings.S2091}{ids.Count}{SharedStrings.S2092}{_paths.PdfsRoot}");
		_cts = new CancellationTokenSource();
		try
		{
			await RunDownloadAsync(ids, _cts.Token);
			StatusText = $"{SharedStrings.S2093}{Succeeded}{SharedStrings.S2094}{Failed}{SharedStrings.S2095}";
			MaxLocal = await _service.GetMaxLocalAsync();
			HashSet<int> hashSet = new HashSet<int>(ids);
			for (int num = Candidates.Count - 1; num >= 0; num--)
			{
				if (hashSet.Contains(Candidates[num].FileId))
				{
					Candidates.RemoveAt(num);
				}
			}
			if (Candidates.Count == 0)
			{
				ShowCandidates = false;
			}
			OnPropertyChanged("SelectedCount");
		}
		catch (OperationCanceledException)
		{
			StatusText = $"{SharedStrings.S2096}{Processed}{SharedStrings.S2097}{Succeeded}{SharedStrings.S2098}";
		}
		catch (Exception ex2)
		{
			StatusText = SharedStrings.S9066 + ex2.Message;
			StatusIsError = true;
		}
		finally
		{
			CurrentId = null;
			IsRunning = false;
			_cts?.Dispose();
			_cts = null;
			if (Succeeded > 0)
			{
				WeakReferenceMessenger.Default.Send(new CatalogChangedMessage(Succeeded));
			}
		}
	}

	[RelayCommand(CanExecute = "CanCancel")]
	private void Cancel()
	{
		_cts?.Cancel();
		AddLog(SharedStrings.S690);
	}

	[RelayCommand(CanExecute = "CanInteract")]
	private async Task RebuildIndexAsync()
	{
		IndexBuildJob job = new IndexBuildJob(new IndexSpec(_paths.IndexesRoot, new string[1] { _paths.PdfsRoot }, UseNativeEnumeration: true), _engine);
		await _bgProcessor.EnqueueAsync(job);
		AddLog(SharedStrings.S691);
		StatusText = SharedStrings.S692;
	}

	private void AddLog(string line)
	{
		string formatted = $"[{DateTime.Now:HH:mm:ss}] {line}";
		Dispatcher dispatcher = Application.Current?.Dispatcher;
		if (dispatcher != null && !dispatcher.CheckAccess())
		{
			dispatcher.Invoke(delegate
			{
				AddLogCore(formatted);
			});
		}
		else
		{
			AddLogCore(formatted);
		}
	}

	private void AddLogCore(string formatted)
	{
		Logs.Add(formatted);
		if (Logs.Count > 500)
		{
			Logs.RemoveAt(0);
		}
	}

	[RelayCommand(CanExecute = "CanStartOtzrayaSync")]
	private async Task SyncOtzrayaAsync()
	{
		if (OtzrayaSyncing)
		{
			return;
		}
		_otzrayaSyncCts?.Dispose();
		_otzrayaSyncCts = new CancellationTokenSource();
		CancellationToken ct = _otzrayaSyncCts.Token;
		try
		{
			OtzrayaSyncing = true;
			OtzrayaSyncProgressDone = 0;
			OtzrayaSyncProgressTotal = 0;
			OtzrayaSyncStatus = SharedStrings.S693;
			AddLog(SharedStrings.S694);
			Progress<OtzrayaSyncService.SyncProgress> progress = new Progress<OtzrayaSyncService.SyncProgress>(delegate(OtzrayaSyncService.SyncProgress p)
			{
				OtzrayaSyncProgressDone = p.Done;
				OtzrayaSyncProgressTotal = p.Total;
				OtzrayaSyncCurrentFile = p.CurrentFile;
				if (p.Total > 0 && p.Done < p.Total)
				{
					OtzrayaSyncStatus = $"{SharedStrings.S2099}{p.Done}/{p.Total}";
				}
			});
			Progress<string> status = new Progress<string>(delegate(string msg)
			{
				OtzrayaSyncStatus = msg;
				AddLog("ℹ " + msg);
			});
			OtzrayaSyncService.SyncResult syncResult = await _otzrayaSync.SyncAsync(progress, 8, ct, status);
			OtzrayaSyncStatus = $"{SharedStrings.S2100}{syncResult.Added}{SharedStrings.S2101}{syncResult.Updated}, {SharedStrings.S2102}{syncResult.Removed}{SharedStrings.S2103}{syncResult.Errors}.";
			AddLog($"{SharedStrings.S2104}{syncResult.Added} ~{syncResult.Updated} -{syncResult.Removed} ✗{syncResult.Errors}");
			if (syncResult.Added > 0 || syncResult.Updated > 0 || syncResult.Removed > 0)
			{
				AddLog($"{SharedStrings.S2105}{syncResult.ChangedPaths.Count}{SharedStrings.S2106}{syncResult.DeletedPaths.Count}{SharedStrings.S2107}");
				IndexSpec spec = new IndexSpec(_paths.OtzrayaIndexPath, new string[1] { _paths.OtzrayaRoot }, UseNativeEnumeration: false, _paths.OtzrayaRoot);
				await _bgProcessor.EnqueueAsync(new TargetedIndexBuildJob(spec, syncResult.ChangedPaths, syncResult.DeletedPaths, _engine), ct);
				AddLog(SharedStrings.S700);
			}
		}
		catch (OperationCanceledException)
		{
			OtzrayaSyncStatus = $"{SharedStrings.S2108}{OtzrayaSyncProgressDone}/{OtzrayaSyncProgressTotal}{SharedStrings.S2109}";
			AddLog(SharedStrings.S702);
		}
		catch (Exception ex2)
		{
			OtzrayaSyncStatus = SharedStrings.S2110 + ex2.Message;
			AddLog(SharedStrings.S2111 + ex2.Message);
		}
		finally
		{
			OtzrayaSyncing = false;
		}
	}

	[RelayCommand(CanExecute = "CanCancelOtzrayaSync")]
	private void CancelOtzrayaSync()
	{
		_otzrayaSyncCts?.Cancel();
	}

	private bool CanStartOtzrayaSync()
	{
		return !OtzrayaSyncing;
	}

	private bool CanCancelOtzrayaSync()
	{
		return OtzrayaSyncing;
	}

	[RelayCommand(CanExecute = "CanRunPublished")]
	private async Task CheckPublishedAsync()
	{
		if (PublishedBusy)
		{
			return;
		}
		_publishedCts?.Dispose();
		_publishedCts = new CancellationTokenSource();
		try
		{
			PublishedBusy = true;
			PublishedStatus = SharedStrings.S705;
			IReadOnlyList<PublishedSyncService.PublishedItem> readOnlyList = await _publishedSync.PeekAsync(_publishedCts.Token);
			PublishedNewCount = readOnlyList.Count;
			PublishedStatus = ((readOnlyList.Count == 0) ? SharedStrings.S706 : $"{SharedStrings.S2112}{readOnlyList.Count}{SharedStrings.S2113}");
			AddLog($"{SharedStrings.S2114}{readOnlyList.Count}{SharedStrings.S2115}");
			foreach (PublishedSyncService.PublishedItem item in readOnlyList.Take(50))
			{
				AddLog($"   • {item.BookName} — {item.AuthorName} ({item.FileId})");
			}
			if (readOnlyList.Count > 50)
			{
				AddLog($"{SharedStrings.S2116}{readOnlyList.Count - 50}");
			}
		}
		catch (OperationCanceledException)
		{
			PublishedStatus = SharedStrings.S710;
		}
		catch (Exception ex2)
		{
			PublishedStatus = SharedStrings.S2117 + ex2.Message;
			AddLog(SharedStrings.S2118 + ex2.Message);
		}
		finally
		{
			PublishedBusy = false;
		}
	}

	[RelayCommand(CanExecute = "CanRunPublished")]
	private async Task SyncPublishedAsync()
	{
		if (PublishedBusy)
		{
			return;
		}
		_publishedCts?.Dispose();
		_publishedCts = new CancellationTokenSource();
		CancellationToken ct = _publishedCts.Token;
		try
		{
			PublishedBusy = true;
			PublishedProgressDone = 0;
			PublishedProgressTotal = 0;
			PublishedStatus = SharedStrings.S713;
			AddLog(SharedStrings.S714);
			Progress<PublishedSyncService.SyncProgress> progress = new Progress<PublishedSyncService.SyncProgress>(delegate(PublishedSyncService.SyncProgress p)
			{
				PublishedProgressDone = p.Done;
				PublishedProgressTotal = p.Total;
				if (p.Total > 0)
				{
					PublishedStatus = $"{SharedStrings.S2119}{p.Done}/{p.Total}: {p.CurrentFile}";
				}
			});
			PublishedSyncService.SyncResult syncResult = await _publishedSync.SyncAsync(progress, ct);
			PublishedNewCount = -1;
			PublishedStatus = $"{SharedStrings.S2120}{syncResult.Added}{SharedStrings.S2121}{syncResult.Skipped}{SharedStrings.S2122}{syncResult.Errors}.";
			AddLog($"{SharedStrings.S2123}{syncResult.Added} ⏭{syncResult.Skipped} ✗{syncResult.Errors}");
			if (syncResult.Added > 0)
			{
				AddLog($"{SharedStrings.S2124}{syncResult.ChangedPaths.Count}{SharedStrings.S2125}");
				IndexSpec spec = new IndexSpec(_paths.IndexesRoot, new string[1] { _paths.PdfsRoot }, UseNativeEnumeration: true);
				await _bgProcessor.EnqueueAsync(new TargetedIndexBuildJob(spec, syncResult.ChangedPaths, Array.Empty<string>(), _engine), ct);
			}
		}
		catch (OperationCanceledException)
		{
			PublishedStatus = SharedStrings.S719;
			AddLog(SharedStrings.S720);
		}
		catch (Exception ex2)
		{
			PublishedStatus = SharedStrings.S2126 + ex2.Message;
			AddLog(SharedStrings.S2127 + ex2.Message);
		}
		finally
		{
			PublishedBusy = false;
		}
	}

	[RelayCommand(CanExecute = "CanCancelPublished")]
	private void CancelPublishedSync()
	{
		_publishedCts?.Cancel();
	}

	private bool CanRunPublished()
	{
		return !PublishedBusy;
	}

	private bool CanCancelPublished()
	{
		return PublishedBusy;
	}

	[RelayCommand(CanExecute = "CanScanOtzraya")]
	private async Task ScanOtzrayaCatalogAsync()
	{
		if (OtzrayaScanning)
		{
			return;
		}
		_otzrayaScanCts?.Dispose();
		_otzrayaScanCts = new CancellationTokenSource();
		CancellationToken token = _otzrayaScanCts.Token;
		try
		{
			OtzrayaScanning = true;
			OtzrayaScanProgressDone = 0;
			OtzrayaScanProgressTotal = 0;
			AddLog(SharedStrings.S722);
			Progress<(int, int)> progress = new Progress<(int, int)>(delegate((int Done, int Total) p)
			{
				(OtzrayaScanProgressDone, OtzrayaScanProgressTotal) = p;
			});
			OtzrayaCatalogIndexer.ScanResult scanResult = await _otzrayaIndexer.ScanAsync(progress, token);
			AddLog($"{SharedStrings.S2128}{scanResult.FilesSeen}{SharedStrings.S2129}{scanResult.Inserted}{SharedStrings.S2130}{scanResult.Updated}{SharedStrings.S2131}{scanResult.Removed}{SharedStrings.S2132}{scanResult.Skipped}");
		}
		catch (OperationCanceledException)
		{
			AddLog($"{SharedStrings.S2133}{OtzrayaScanProgressDone}/{OtzrayaScanProgressTotal}");
		}
		catch (Exception ex2)
		{
			AddLog(SharedStrings.S2134 + ex2.Message);
		}
		finally
		{
			OtzrayaScanning = false;
		}
	}

	[RelayCommand(CanExecute = "CanCancelOtzrayaScan")]
	private void CancelOtzrayaScan()
	{
		_otzrayaScanCts?.Cancel();
	}

	[RelayCommand]
	private void OpenPersonalCorpus()
	{
		try
		{
			if (App.Services.GetService(typeof(PersonalCorpusWindow)) is PersonalCorpusWindow personalCorpusWindow)
			{
				personalCorpusWindow.Owner = Application.Current?.MainWindow;
				personalCorpusWindow.Show();
			}
		}
		catch (Exception ex)
		{
			AddLog(SharedStrings.S2135 + ex.Message);
		}
	}

	[RelayCommand]
	private void OpenUploadToServer()
	{
		try
		{
			if (App.Services.GetService(typeof(UploadToServerWindow)) is UploadToServerWindow uploadToServerWindow)
			{
				uploadToServerWindow.Owner = Application.Current?.MainWindow;
				uploadToServerWindow.Show();
			}
		}
		catch (Exception ex)
		{
			AddLog(SharedStrings.S2136 + ex.Message);
		}
	}

	[RelayCommand]
	private async Task BuildOtzrayaIndexAsync()
	{
		try
		{
			AddLog(SharedStrings.S728);
			IndexSpec spec = new IndexSpec(_paths.OtzrayaIndexPath, new string[1] { _paths.OtzrayaRoot }, UseNativeEnumeration: false, _paths.OtzrayaRoot);
			await _bgProcessor.EnqueueAsync(new IndexBuildJob(spec, _engine));
		}
		catch (Exception ex)
		{
			AddLog(SharedStrings.S2137 + ex.Message);
		}
	}

	private bool CanScanOtzraya()
	{
		return !OtzrayaScanning;
	}

	private bool CanCancelOtzrayaScan()
	{
		return OtzrayaScanning;
	}

}
