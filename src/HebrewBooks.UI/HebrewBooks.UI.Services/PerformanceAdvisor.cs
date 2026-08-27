using System;
using System.IO;
using System.Windows;
using HebrewBooks.Core.Abstractions;
using HebrewBooks.Infrastructure.Settings;
using HebrewBooks.UI.Dialogs;
using HebrewBooks.UI.Resources;

namespace HebrewBooks.UI.Services;

public sealed class PerformanceAdvisor
{
	private enum StorageKind
	{
		External,
		Network,
		Online,
		Local
	}

	private readonly IPathResolver _paths;

	private readonly JsonSettingsStore _settings;

	private const long SearchSlowMs = 8000L;

	private const long OpenSlowMs = 6000L;

	private const int SlowEventsBeforeWarn = 2;

	private readonly object _gate = new object();

	private bool _warnedThisSession;

	public PerformanceAdvisor(IPathResolver paths, JsonSettingsStore settings)
	{
		_paths = paths;
		_settings = settings;
	}

	public void ReportOperation(SlowStage stage, long elapsedMs)
	{
		long num = ((stage == SlowStage.Search) ? 8000 : 6000);
		if (elapsedMs < num)
		{
			return;
		}
		lock (_gate)
		{
			if (_warnedThisSession || !_settings.Load().View.ShowPerformanceHints)
			{
				return;
			}
			int events = _settings.Load().View.SlowStorageEvents + 1;
			if (events < 2)
			{
				_settings.Update(delegate(BookshelfOptions o)
				{
					o.View.SlowStorageEvents = events;
				});
				return;
			}
			_warnedThisSession = true;
			_settings.Update(delegate(BookshelfOptions o)
			{
				o.View.SlowStorageEvents = 0;
			});
		}
		ShowOnUiThread();
	}

	private void ShowOnUiThread()
	{
		Application app = Application.Current;
		if (app == null)
		{
			return;
		}
		app.Dispatcher.InvokeAsync(delegate
		{
			if (PerformanceHintDialog.Show((app.Windows.Count > 0) ? app.MainWindow : null, BuildMessage()))
			{
				_settings.Update(delegate(BookshelfOptions o)
				{
					o.View.ShowPerformanceHints = false;
				});
			}
		});
	}

	private string BuildMessage()
	{
		return ClassifyStorage() switch
		{
			StorageKind.Online => SharedStrings.PerfHintOnline, 
			StorageKind.Network => SharedStrings.PerfHintNetwork, 
			StorageKind.External => SharedStrings.PerfHintExternal, 
			_ => SharedStrings.PerfHintLocal, 
		};
	}

	private StorageKind ClassifyStorage()
	{
		BookshelfOptions bookshelfOptions = _settings.Load();
		if (bookshelfOptions.UseOnlineService)
		{
			return StorageKind.Online;
		}
		if (bookshelfOptions.NetworkInstall)
		{
			return StorageKind.Network;
		}
		try
		{
			string pathRoot = Path.GetPathRoot(_paths.DataDriveRoot);
			if (string.IsNullOrEmpty(pathRoot))
			{
				return StorageKind.Local;
			}
			if (pathRoot.StartsWith("\\\\", StringComparison.Ordinal))
			{
				return StorageKind.Network;
			}
			return new DriveInfo(pathRoot).DriveType switch
			{
				DriveType.Removable => StorageKind.External, 
				DriveType.Network => StorageKind.Network, 
				_ => StorageKind.Local, 
			};
		}
		catch
		{
			return StorageKind.Local;
		}
	}
}
