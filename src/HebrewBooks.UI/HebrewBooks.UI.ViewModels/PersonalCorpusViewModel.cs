using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;
using CommunityToolkit.Mvvm.Input;
using HebrewBooks.Core.Abstractions;
using HebrewBooks.Core.Models;
using HebrewBooks.Services.Background;
using HebrewBooks.Services.Personal;
using HebrewBooks.UI.Resources;

namespace HebrewBooks.UI.ViewModels;

public partial class PersonalCorpusViewModel : ObservableObject
{
	private readonly PersonalCatalogIndexer _indexer;

	private readonly BackgroundProcessorService _bgProcessor;

	private readonly ISearchEngine _engine;

	private readonly IPathResolver _paths;

	[ObservableProperty]
	private string _folderPath = string.Empty;

	[ObservableProperty]
	private bool _folderExists;

	[ObservableProperty]
	[NotifyCanExecuteChangedFor("ScanCommand")]
	[NotifyCanExecuteChangedFor("CancelScanCommand")]
	private bool _scanning;

	[ObservableProperty]
	private int _scanProgressDone;

	[ObservableProperty]
	private int _scanProgressTotal;

	private CancellationTokenSource? _scanCts;





	public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();










	public PersonalCorpusViewModel(PersonalCatalogIndexer indexer, BackgroundProcessorService bgProcessor, ISearchEngine engine, IPathResolver paths)
	{
		_indexer = indexer;
		_bgProcessor = bgProcessor;
		_engine = engine;
		_paths = paths;
		FolderPath = _paths.PersonalRoot;
		FolderExists = Directory.Exists(_paths.PersonalRoot);
	}

	[RelayCommand(CanExecute = "CanScan")]
	private async Task ScanAsync()
	{
		if (Scanning)
		{
			return;
		}
		_scanCts?.Dispose();
		_scanCts = new CancellationTokenSource();
		CancellationToken token = _scanCts.Token;
		try
		{
			Scanning = true;
			ScanProgressDone = 0;
			ScanProgressTotal = 0;
			FolderExists = Directory.Exists(_paths.PersonalRoot);
			AddLog(SharedStrings.S2242 + FolderPath + ")");
			Progress<(int, int)> progress = new Progress<(int, int)>(delegate((int Done, int Total) p)
			{
				(ScanProgressDone, ScanProgressTotal) = p;
			});
			PersonalCatalogIndexer.ScanResult scanResult = await _indexer.ScanAsync(progress, token);
			AddLog($"{SharedStrings.S2243}{scanResult.FilesSeen}{SharedStrings.S2244}{scanResult.Inserted}{SharedStrings.S2245}{scanResult.Updated}{SharedStrings.S2246}{scanResult.Removed}{SharedStrings.S2247}{scanResult.Skipped}");
			if (scanResult.FilesSeen > 0)
			{
				AddLog(SharedStrings.S835);
			}
		}
		catch (OperationCanceledException)
		{
			AddLog($"{SharedStrings.S2248}{ScanProgressDone}/{ScanProgressTotal}");
		}
		catch (Exception ex2)
		{
			AddLog(SharedStrings.S2249 + ex2.Message);
		}
		finally
		{
			Scanning = false;
		}
	}

	[RelayCommand(CanExecute = "CanCancelScan")]
	private void CancelScan()
	{
		_scanCts?.Cancel();
	}

	[RelayCommand]
	private async Task BuildIndexAsync()
	{
		if (!Directory.Exists(_paths.PersonalRoot))
		{
			AddLog(SharedStrings.S838);
			return;
		}
		try
		{
			AddLog(SharedStrings.S839);
			IndexSpec spec = new IndexSpec(_paths.PersonalIndexPath, new string[1] { _paths.PersonalRoot }, UseNativeEnumeration: false, _paths.PersonalRoot);
			await _bgProcessor.EnqueueAsync(new IndexBuildJob(spec, _engine));
		}
		catch (Exception ex)
		{
			AddLog(SharedStrings.S2250 + ex.Message);
		}
	}

	[RelayCommand]
	private void OpenFolder()
	{
		try
		{
			Directory.CreateDirectory(_paths.PersonalRoot);
			FolderExists = true;
			Process.Start(new ProcessStartInfo
			{
				FileName = _paths.PersonalRoot,
				UseShellExecute = true
			});
		}
		catch (Exception ex)
		{
			AddLog(SharedStrings.S2251 + ex.Message);
		}
	}

	private bool CanScan()
	{
		return !Scanning;
	}

	private bool CanCancelScan()
	{
		return Scanning;
	}

	private void AddLog(string line)
	{
		string stamped = $"[{DateTime.Now:HH:mm:ss}] {line}";
		Dispatcher dispatcher = Application.Current?.Dispatcher;
		if (dispatcher == null || dispatcher.CheckAccess())
		{
			Logs.Add(stamped);
		}
		else
		{
			dispatcher.BeginInvoke((Action)delegate
			{
				Logs.Add(stamped);
			});
		}
		if (Logs.Count > 200)
		{
			Logs.RemoveAt(0);
		}
	}
}
