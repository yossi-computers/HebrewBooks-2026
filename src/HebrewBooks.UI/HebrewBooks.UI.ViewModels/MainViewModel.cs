using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;
using CommunityToolkit.Mvvm.Input;
using HebrewBooks.Core.Abstractions;
using HebrewBooks.Core.Models;
using HebrewBooks.Infrastructure.Settings;
using HebrewBooks.Services.Background;
using HebrewBooks.UI.Resources;
using HebrewBooks.UI.Services;
using Serilog;
using Velopack;

namespace HebrewBooks.UI.ViewModels;

public partial class MainViewModel : ObservableObject
{
	private readonly IPathResolver _paths;

	private readonly JsonSettingsStore _settings;

	private readonly AppUpdateService _updates;

	private UpdateInfo? _pendingUpdate;

	private bool _startupCheckRan;

	[ObservableProperty]
	private string _appTitle = SharedStrings.AppTitle;

	[ObservableProperty]
	private string _drivePath = string.Empty;

	[ObservableProperty]
	private string _statusText = SharedStrings.StatusReady;

	[ObservableProperty]
	private bool _backgroundJobVisible;

	[ObservableProperty]
	private string _backgroundJobTitle = string.Empty;

	[ObservableProperty]
	private int _backgroundJobProgress;

	[ObservableProperty]
	private bool _backgroundJobCancellable;

	private long _backgroundJobGen;

	private BackgroundProcessorService? _bg;

	private IndexProgressReport? _lastIndexProgress;

	private readonly object _indexProgressLock = new object();

	[ObservableProperty]
	private bool _immersiveReading;

	[ObservableProperty]
	private bool _chromeAutoHide;

	private DispatcherTimer? _driveWatchdog;

	[ObservableProperty]
	private bool _dataRootMissing;

	[ObservableProperty]
	private string _dataRootMissingReason = string.Empty;

	[ObservableProperty]
	private bool _updateBannerVisible;

	[ObservableProperty]
	private string _updateBannerText = string.Empty;

	[ObservableProperty]
	private string _updateReleaseNotes = string.Empty;

	[ObservableProperty]
	private bool _updateDownloading;

	[ObservableProperty]
	private bool _updateReady;

	[ObservableProperty]
	private int _updateDownloadProgress;







	public bool NavPaneVisible => !ImmersiveReading;

	public bool HasUpdateReleaseNotes => !string.IsNullOrWhiteSpace(UpdateReleaseNotes);
























	public int? IndexBuildPercentFor(string? indexPath)
	{
		if (string.IsNullOrEmpty(indexPath))
		{
			return null;
		}
		IndexProgressReport lastIndexProgress;
		lock (_indexProgressLock)
		{
			lastIndexProgress = _lastIndexProgress;
		}
		if ((object)lastIndexProgress == null)
		{
			return null;
		}
		if (!string.Equals(Path.Combine(lastIndexProgress.IndexLocation ?? string.Empty, lastIndexProgress.IndexName ?? string.Empty).TrimEnd('\\', '/'), indexPath.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}
		return Math.Clamp(lastIndexProgress.PercentDone, 0, 100);
	}

	[RelayCommand]
	private void ToggleImmersive()
	{
		ImmersiveReading = !ImmersiveReading;
	}

	[RelayCommand]
	private void ToggleChromeAutoHide()
	{
		ChromeAutoHide = !ChromeAutoHide;
	}

	private void CheckDataRootReachable()
	{
		try
		{
			if (File.Exists(Path.Combine(_paths.DataDriveRoot, "App", "Katalog.db")))
			{
				if (DataRootMissing)
				{
					DataRootMissing = false;
					DataRootMissingReason = string.Empty;
				}
			}
			else if (!DataRootMissing)
			{
				DataRootMissingReason = SharedStrings.S2218 + _paths.DataDriveRoot;
				DataRootMissing = true;
			}
		}
		catch (Exception ex)
		{
			DataRootMissingReason = ex.Message;
			DataRootMissing = true;
		}
	}

	[RelayCommand]
	private void RefreshDataRoot()
	{
		CheckDataRootReachable();
	}

	public MainViewModel(IPathResolver paths, JsonSettingsStore settings, AppUpdateService updates, BackgroundProcessorService bg)
	{
		_paths = paths;
		_settings = settings;
		_updates = updates;
		DrivePath = _paths.DataDriveRoot;
		_driveWatchdog = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds(5.0)
		};
		_driveWatchdog.Tick += delegate
		{
			CheckDataRootReachable();
		};
		_driveWatchdog.Start();
		_chromeAutoHide = settings.Load().View.ChromeAutoHide;
		_bg = bg;
		bg.JobStarted += delegate(object? _, BackgroundProcessorService.Job job)
		{
			OnUI(delegate
			{
				long num = ++_backgroundJobGen;
				BackgroundJobTitle = job.Title;
				BackgroundJobProgress = 0;
				BackgroundJobVisible = true;
				BackgroundJobCancellable = true;
				_ = num;
			});
		};
		bg.JobProgress += delegate(object? _, JobProgress p)
		{
			OnUI(delegate
			{
				BackgroundJobProgress = (int)Math.Round(p.Percent * 100.0);
			});
		};
		bg.IndexProgress += delegate(object? _, IndexProgressReport r)
		{
			lock (_indexProgressLock)
			{
				_lastIndexProgress = r;
			}
		};
		bg.JobCompleted += delegate(object? _, JobCompletion c)
		{
			OnUI(delegate
			{
				lock (_indexProgressLock)
				{
					_lastIndexProgress = null;
				}
				long gen = ++_backgroundJobGen;
				if (c.Error is CorruptIndexException ex)
				{
					MessageBox.Show(SharedStrings.S823 + SharedStrings.S824 + SharedStrings.S825 + SharedStrings.S826 + SharedStrings.S2219 + ex.IndexPath + "\n" + SharedStrings.S828, SharedStrings.S829, MessageBoxButton.OK, MessageBoxImage.Exclamation);
				}
				BackgroundJobTitle = ((c.Error != null) ? (c.Job.Title + " ✗ " + c.Error.Message) : (c.Cancelled ? (c.Job.Title + SharedStrings.S2220) : (c.Job.Title + " ✓")));
				BackgroundJobProgress = 100;
				BackgroundJobCancellable = false;
				_ = Task.Delay(5000).ContinueWith(delegate
				{
					OnUI(delegate
					{
						if (_backgroundJobGen == gen)
						{
							BackgroundJobVisible = false;
						}
					});
				});
			});
		};
	}

	[RelayCommand]
	private void CancelBackgroundJob()
	{
		_bg?.CancelCurrentJob();
	}

	private static void OnUI(Action a)
	{
		Application current = Application.Current;
		if (current == null)
		{
			a();
		}
		else if (current.Dispatcher.CheckAccess())
		{
			a();
		}
		else
		{
			current.Dispatcher.BeginInvoke(a);
		}
	}

	public async Task StartupUpdateCheckAsync()
	{
		if (_startupCheckRan)
		{
			return;
		}
		_startupCheckRan = true;
		if (!_updates.IsEnabled)
		{
			return;
		}
		UpdatesOptions opts = _settings.Load().Updates;
		if (!opts.CheckOnStartup)
		{
			return;
		}
		try
		{
			UpdateInfo info = await _updates.CheckAsync().ConfigureAwait(continueOnCapturedContext: true);
			if (info == null)
			{
				return;
			}
			_pendingUpdate = info;
			UpdateBannerText = $"{SharedStrings.S2221}{info.TargetFullRelease.Version}";
			UpdateBannerVisible = true;
			Task.Run(async delegate
			{
				try
				{
					string notes = await _updates.GetCumulativeReleaseNotesAsync(_updates.CurrentVersion, info.TargetFullRelease.Version.ToString()).ConfigureAwait(continueOnCapturedContext: false);
					if (!string.IsNullOrWhiteSpace(notes))
					{
						OnUI(delegate
						{
							UpdateReleaseNotes = notes;
						});
					}
				}
				catch (Exception exception2)
				{
					Log.Warning(exception2, "Cumulative release-notes fetch failed");
				}
			});
			if (opts.AutoDownload)
			{
				await DownloadAsync().ConfigureAwait(continueOnCapturedContext: true);
			}
		}
		catch (Exception exception)
		{
			Log.Warning(exception, "Startup update check failed");
		}
	}

	[RelayCommand]
	private async Task DownloadAndInstallUpdate()
	{
		if (_pendingUpdate == null)
		{
			return;
		}
		if (!UpdateReady)
		{
			await DownloadAsync().ConfigureAwait(continueOnCapturedContext: true);
			if (!UpdateReady)
			{
				return;
			}
		}
		_updates.ApplyAndRestart(_pendingUpdate);
	}

	private async Task DownloadAsync()
	{
		if (_pendingUpdate == null || UpdateReady || UpdateDownloading)
		{
			return;
		}
		try
		{
			UpdateDownloading = true;
			UpdateDownloadProgress = 0;
			await _updates.DownloadAsync(_pendingUpdate, new Progress<int>(delegate(int p)
			{
				UpdateDownloadProgress = p;
			})).ConfigureAwait(continueOnCapturedContext: true);
			UpdateReady = true;
		}
		catch (Exception ex)
		{
			Log.Warning(ex, "Update download failed");
			UpdateBannerText = SharedStrings.S2222 + ex.Message;
		}
		finally
		{
			UpdateDownloading = false;
		}
	}

	[RelayCommand]
	private void DismissUpdate()
	{
		UpdateBannerVisible = false;
	}

	public void SetOpenBookTitle(string? bookName, string? searchQuery = null)
	{
		string appTitle = SharedStrings.AppTitle;
		if (string.IsNullOrWhiteSpace(bookName))
		{
			AppTitle = appTitle;
		}
		else
		{
			AppTitle = appTitle + " - " + bookName.Trim();
		}
	}



}
