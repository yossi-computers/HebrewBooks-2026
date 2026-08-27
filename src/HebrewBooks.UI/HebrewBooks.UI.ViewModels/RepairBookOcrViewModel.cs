using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;
using CommunityToolkit.Mvvm.Input;
using HebrewBooks.Core.Abstractions;
using HebrewBooks.Core.Models;
using HebrewBooks.Services.Background;
using HebrewBooks.Services.TextLayer;
using HebrewBooks.UI.Dialogs;
using HebrewBooks.UI.Resources;
using HebrewBooks.UI.Services;
using Microsoft.Extensions.Logging;

namespace HebrewBooks.UI.ViewModels;

public partial class RepairBookOcrViewModel : ObservableObject
{
	private readonly IPathResolver _paths;

	private readonly BackgroundProcessorService _bg;

	private readonly TextLayerService _textLayer;

	private readonly WinOcrPageRenderer _pageRenderer;

	private readonly TextLayerContributor _contributor;

	private readonly OcrEngineInstaller _installer;

	private readonly ILogger<RepairBookOcrViewModel>? _log;

	private bool _engineUpdating;

	private CancellationTokenSource? _installCts;

	private static OcrEngineInstaller.OcrRelease? s_latestRelease;

	private static bool s_latestChecked;

	private static bool s_userDeclinedUpdate;

	private int _fileId;

	private string? _folder;

	private string? _personalRel;

	private bool _isPersonal;

	private string _sidecarPath = "";

	private DateTime _startedAtUtc;

	private RepairBookOcrJob? _job;

	private EventHandler<JobCompletion>? _completedHandler;

	private EventHandler<JobProgress>? _progressHandler;

	private bool _inApplyPhase;

	[ObservableProperty]
	private string _bookName = "";

	[ObservableProperty]
	private string _fileIdLabel = "";

	[ObservableProperty]
	private RepairOcrState _state;

	[ObservableProperty]
	private BitmapImage? _previewImage;

	[ObservableProperty]
	private string _stageLabel = SharedStrings.S842;

	[ObservableProperty]
	private int _currentPage;

	[ObservableProperty]
	private int _totalPages;

	[ObservableProperty]
	private double _percent;

	[ObservableProperty]
	private string _elapsedText = "00:00";

	[ObservableProperty]
	private string _remainingText = "—";

	[ObservableProperty]
	private string _lastEngineLine = "";

	[ObservableProperty]
	private string _summaryText = "";

	[ObservableProperty]
	private string _sidecarPathDisplay = "";

	[ObservableProperty]
	private string _uploadStatus = "";

	[ObservableProperty]
	private bool _hasPr;

	private Timer? _tick;




	public string PageCounterText
	{
		get
		{
			if (!_engineUpdating)
			{
				return $"{SharedStrings.S2252}{CurrentPage} / {TotalPages}";
			}
			return "";
		}
	}

	public string ElapsedDisplay => SharedStrings.S2253 + ElapsedText;

	public string RemainingDisplay => SharedStrings.S2254 + RemainingText;

	public ObservableCollection<string> LogLines { get; } = new ObservableCollection<string>();

	public bool IsIdle => State == RepairOcrState.Idle;

	public bool IsRunning => State == RepairOcrState.Running;

	public bool IsDone => State == RepairOcrState.Done;

	public bool IsErrorOrCancelled
	{
		get
		{
			if (State != RepairOcrState.Failed)
			{
				return State == RepairOcrState.Cancelled;
			}
			return true;
		}
	}



















	public RepairBookOcrViewModel(IPathResolver paths, BackgroundProcessorService bg, TextLayerService textLayer, WinOcrPageRenderer pageRenderer, TextLayerContributor contributor, OcrEngineInstaller installer, ILogger<RepairBookOcrViewModel>? log = null)
	{
		_paths = paths;
		_bg = bg;
		_textLayer = textLayer;
		_pageRenderer = pageRenderer;
		_contributor = contributor;
		_installer = installer;
		_log = log;
	}

	public async Task InitializeAsync(Book book)
	{
		_isPersonal = string.Equals(book.SourceType, "Personal", StringComparison.Ordinal);
		_personalRel = (_isPersonal ? (book.RelativePath ?? book.FileID) : null);
		_fileId = ((!_isPersonal && int.TryParse(book.FileID, out var result)) ? result : 0);
		_folder = book.Folder;
		BookName = (string.IsNullOrEmpty(book.BookName) ? (book.FileID ?? "") : book.BookName);
		FileIdLabel = (_isPersonal ? (SharedStrings.S2255 + _personalRel) : $"FileID {_fileId}");
		string text = (_isPersonal ? string.Concat((_personalRel ?? "").Select((char c) => (!char.IsLetterOrDigit(c)) ? '_' : c)) : _fileId.ToString());
		int length = text.Length;
		if ((length > 100 || length == 0) ? true : false)
		{
			text = Math.Abs((_personalRel ?? "").GetHashCode()).ToString();
		}
		_sidecarPath = Path.Combine(_paths.UserDataRoot, "PendingContributions", text + ".pdf");
		SidecarPathDisplay = _sidecarPath;
		Task.Run(async delegate
		{
			try
			{
				string text2 = (_isPersonal ? _paths.PersonalFilePath(_personalRel) : _paths.PdfPath(_fileId, _folder));
				if (File.Exists(text2))
				{
					byte[] png = await _pageRenderer.RenderPagePngAsync(text2, 0, 72.0).ConfigureAwait(continueOnCapturedContext: false);
					if (png != null)
					{
						Application.Current?.Dispatcher.BeginInvoke((Action)delegate
						{
							BitmapImage bitmapImage = new BitmapImage();
							using MemoryStream streamSource = new MemoryStream(png);
							bitmapImage.BeginInit();
							bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
							bitmapImage.StreamSource = streamSource;
							bitmapImage.EndInit();
							bitmapImage.Freeze();
							PreviewImage = bitmapImage;
						});
					}
				}
			}
			catch (Exception exception)
			{
				_log?.LogDebug(exception, "RepairBookOcrViewModel: thumbnail load failed");
			}
		});
		await Task.CompletedTask;
	}

	[RelayCommand]
	private async Task StartAsync()
	{
		if (_fileId <= 0 || State != RepairOcrState.Idle || !(await EnsureEngineFreshOrConfirmedAsync()))
		{
			return;
		}
		Progress<RepairProgress> richProgress = new Progress<RepairProgress>(OnRichProgress);
		_job = new RepairBookOcrJob(_fileId, _folder, _sidecarPath, _textLayer, null, richProgress, _personalRel);
		State = RepairOcrState.Running;
		StageLabel = SharedStrings.S622;
		_startedAtUtc = DateTime.UtcNow;
		StartTickTimer();
		_progressHandler = delegate(object? _, JobProgress p)
		{
			if ((object)_job != null && !(p.Job.Id != _job.Id) && p.Percent >= 0.85 && !_inApplyPhase)
			{
				Application.Current?.Dispatcher.BeginInvoke((Action)delegate
				{
					_inApplyPhase = true;
				});
			}
		};
		_completedHandler = delegate(object? _, JobCompletion c)
		{
			if ((object)_job != null && !(c.Job.Id != _job.Id))
			{
				Application.Current?.Dispatcher.BeginInvoke((Action)delegate
				{
					OnCompleted(c);
				});
			}
		};
		_bg.JobProgress += _progressHandler;
		_bg.JobCompleted += _completedHandler;
		await _bg.EnqueueAsync(_job, JobLane.Interactive).AsTask().ConfigureAwait(continueOnCapturedContext: false);
	}

	[RelayCommand]
	private void Cancel()
	{
		if (_engineUpdating)
		{
			_installCts?.Cancel();
			StageLabel = SharedStrings.S847;
		}
		else if (State == RepairOcrState.Running)
		{
			StageLabel = SharedStrings.S848;
			if ((object)_job != null)
			{
				_bg.CancelJob(_job.Id);
			}
		}
	}

	private async Task<bool> EnsureEngineFreshOrConfirmedAsync()
	{
		if (s_userDeclinedUpdate)
		{
			return true;
		}
		string installed = _installer.InstalledVersion;
		if (string.IsNullOrWhiteSpace(installed))
		{
			return true;
		}
		if (!s_latestChecked)
		{
			try
			{
				using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(6.0));
				s_latestRelease = await _installer.CheckLatestAsync(cts.Token);
			}
			catch (Exception exception)
			{
				_log?.LogDebug(exception, "RepairBookOcr: engine version check skipped");
			}
			s_latestChecked = true;
		}
		OcrEngineInstaller.OcrRelease ocrRelease = s_latestRelease;
		if ((object)ocrRelease == null || !IsOlder(installed, ocrRelease.Tag))
		{
			return true;
		}
		switch (HebrewMessageBox.Show($"{SharedStrings.S2256}{installed}).\n{SharedStrings.S2257}{ocrRelease.Tag}.\n\n" + SharedStrings.S851 + SharedStrings.S852, SharedStrings.S853, MessageBoxButton.YesNoCancel, MessageBoxImage.Asterisk))
		{
		case MessageBoxResult.Cancel:
			return false;
		case MessageBoxResult.No:
			s_userDeclinedUpdate = true;
			return true;
		default:
			await UpdateEngineAsync(ocrRelease);
			s_latestChecked = false;
			s_userDeclinedUpdate = false;
			return false;
		}
	}

	private async Task UpdateEngineAsync(OcrEngineInstaller.OcrRelease release)
	{
		_engineUpdating = true;
		OnPropertyChanged("PageCounterText");
		State = RepairOcrState.Running;
		StageLabel = SharedStrings.S854;
		Percent = 0.0;
		_installCts = new CancellationTokenSource();
		try
		{
			Progress<OcrInstallProgress> progress = new Progress<OcrInstallProgress>(delegate(OcrInstallProgress p)
			{
				StageLabel = p.Message;
				if (p.Percent > 0)
				{
					Percent = p.Percent;
				}
			});
			await _installer.InstallAsync(release, progress, _installCts.Token);
			State = RepairOcrState.Idle;
			Percent = 100.0;
			StageLabel = SharedStrings.S2258 + release.Tag + SharedStrings.S2259;
		}
		catch (OperationCanceledException)
		{
			State = RepairOcrState.Idle;
			StageLabel = SharedStrings.S856;
		}
		catch (Exception ex2)
		{
			State = RepairOcrState.Idle;
			StageLabel = SharedStrings.S9075 + ex2.Message;
			_log?.LogWarning(ex2, "RepairBookOcr: engine update failed");
		}
		finally
		{
			_engineUpdating = false;
			OnPropertyChanged("PageCounterText");
			_installCts?.Dispose();
			_installCts = null;
		}
	}

	private static bool IsOlder(string installed, string latest)
	{
		int[] array = ParseVersion(installed);
		int[] array2 = ParseVersion(latest);
		if (array == null || array2 == null)
		{
			return false;
		}
		int num = Math.Max(array.Length, array2.Length);
		for (int i = 0; i < num; i++)
		{
			int num2 = ((i < array.Length) ? array[i] : 0);
			int num3 = ((i < array2.Length) ? array2[i] : 0);
			if (num2 != num3)
			{
				return num2 < num3;
			}
		}
		return false;
	}

	private static int[]? ParseVersion(string tag)
	{
		if (string.IsNullOrWhiteSpace(tag))
		{
			return null;
		}
		string[] array = tag.Trim().TrimStart('v', 'V').Split('.', StringSplitOptions.RemoveEmptyEntries);
		if (array.Length == 0)
		{
			return null;
		}
		int[] array2 = new int[array.Length];
		for (int i = 0; i < array.Length; i++)
		{
			string text = new string(array[i].TakeWhile(char.IsDigit).ToArray());
			if (text.Length == 0)
			{
				return null;
			}
			array2[i] = int.Parse(text, CultureInfo.InvariantCulture);
		}
		return array2;
	}

	[RelayCommand]
	private void OpenFolder()
	{
		if (!File.Exists(_sidecarPath))
		{
			return;
		}
		try
		{
			Process.Start(new ProcessStartInfo
			{
				FileName = "explorer.exe",
				Arguments = "/select,\"" + _sidecarPath + "\"",
				UseShellExecute = true
			});
		}
		catch (Exception exception)
		{
			_log?.LogWarning(exception, "Open-folder failed for {Path}", _sidecarPath);
		}
	}

	private void OnRichProgress(RepairProgress p)
	{
		Application.Current?.Dispatcher.BeginInvoke((Action)delegate
		{
			string stageLabel = p.Stage switch
			{
				"ocr" => SharedStrings.S858, 
				"merge" => SharedStrings.S859, 
				"build" => _inApplyPhase ? SharedStrings.S860 : SharedStrings.S861, 
				"backup" => SharedStrings.S862, 
				"inject" => SharedStrings.S863, 
				"save" => SharedStrings.S864, 
				"reindex" => SharedStrings.S865, 
				_ => p.Stage ?? SharedStrings.S866, 
			};
			StageLabel = stageLabel;
			if (p.Total > 0)
			{
				CurrentPage = p.Current;
				TotalPages = p.Total;
			}
			if (p.Total > 0)
			{
				double num = (double)p.Current / (double)p.Total;
				double value = ((!_inApplyPhase) ? (p.Stage switch
				{
					"ocr" => num * 50.0, 
					"merge" => 50.0 + num * 30.0, 
					"build" => 80.0 + num * 10.0, 
					_ => Percent, 
				}) : (90.0 + num * 10.0));
				Percent = Math.Round(value, 1);
			}
			if (!string.IsNullOrEmpty(p.LastLogLine) && p.LastLogLine != LastEngineLine)
			{
				LastEngineLine = p.LastLogLine;
				LogLines.Add($"[{DateTime.Now:HH:mm:ss}] {p.LastLogLine}");
				while (LogLines.Count > 100)
				{
					LogLines.RemoveAt(0);
				}
			}
			UpdateEtaForBar();
		});
	}

	private void OnCompleted(JobCompletion c)
	{
		_bg.JobProgress -= _progressHandler;
		_bg.JobCompleted -= _completedHandler;
		StopTickTimer();
		if (c.Cancelled)
		{
			State = RepairOcrState.Cancelled;
			StageLabel = SharedStrings.S756;
			SummaryText = SharedStrings.S867;
			return;
		}
		if (c.Error != null)
		{
			State = RepairOcrState.Failed;
			StageLabel = SharedStrings.S868;
			SummaryText = c.Error.Message;
			return;
		}
		State = RepairOcrState.Done;
		StageLabel = SharedStrings.S869;
		Percent = 100.0;
		TextLayerExtractResult textLayerExtractResult = _job?.ExtractResult;
		TextLayerApplyResult textLayerApplyResult = _job?.ApplyResult;
		SummaryText = $"{textLayerExtractResult?.PagesProcessed ?? 0}{SharedStrings.S2260}{textLayerExtractResult?.Words ?? 0:N0}{SharedStrings.S2261}{SharedStrings.S2262}{(((object)textLayerApplyResult != null && textLayerApplyResult.BackupCreated) ? SharedStrings.S213 : SharedStrings.S9076)}\n{SharedStrings.S2263}{(double)(textLayerApplyResult?.NewSizeBytes ?? 0) / 1024.0 / 1024.0:F2} MB\n{SharedStrings.S2264}{ElapsedText}";
		StartContributionAsync();
	}

	private async Task StartContributionAsync()
	{
		if (_isPersonal)
		{
			UploadStatus = SharedStrings.S874;
		}
		else
		{
			if (!_contributor.IsAvailable || !File.Exists(_sidecarPath))
			{
				return;
			}
			UploadStatus = SharedStrings.S875;
			ContributionResult result = await _contributor.ContributeAsync(_fileId, _sidecarPath).ConfigureAwait(continueOnCapturedContext: false);
			Application.Current?.Dispatcher.BeginInvoke((Action)delegate
			{
				if (result.Success)
				{
					HasPr = !string.IsNullOrEmpty(result.PullRequestUrl);
					UploadStatus = (HasPr ? SharedStrings.S876 : SharedStrings.S877);
				}
				else
				{
					UploadStatus = SharedStrings.S2265 + result.Message + SharedStrings.S2266;
				}
			});
		}
	}

	private void StartTickTimer()
	{
		_tick?.Dispose();
		_tick = new Timer(delegate
		{
			Application.Current?.Dispatcher.BeginInvoke(new Action(UpdateEtaForBar));
		}, null, TimeSpan.FromSeconds(1.0), TimeSpan.FromSeconds(1.0));
	}

	private void StopTickTimer()
	{
		_tick?.Dispose();
		_tick = null;
	}

	private void UpdateEtaForBar()
	{
		TimeSpan t = DateTime.UtcNow - _startedAtUtc;
		ElapsedText = FormatHms(t);
		if (Percent <= 0.5)
		{
			RemainingText = "—";
			return;
		}
		double num = t.TotalSeconds * 100.0 / Percent;
		double value = Math.Max(0.0, num - t.TotalSeconds);
		RemainingText = FormatHms(TimeSpan.FromSeconds(value));
	}

	private static string FormatHms(TimeSpan t)
	{
		if (t.TotalHours >= 1.0)
		{
			return $"{(int)t.TotalHours}:{t:mm\\:ss}";
		}
		return $"{t:mm\\:ss}";
	}





}
