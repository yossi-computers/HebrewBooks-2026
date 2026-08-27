using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;
using CommunityToolkit.Mvvm.Input;
using HebrewBooks.Infrastructure.Paths;
using HebrewBooks.Infrastructure.Settings;
using HebrewBooks.Services.Downloader;
using HebrewBooks.Services.Provisioning;
using HebrewBooks.UI.Dialogs;
using HebrewBooks.UI.Resources;
using Microsoft.Win32;

namespace HebrewBooks.UI.ViewModels;

public partial class SetupWizardViewModel : ObservableObject
{
	public enum WizardStep
	{
		Welcome,
		ChooseTier,
		Provisioning
	}

	private sealed record PendingProvision(string Root, InstallTier Tier, bool BuildIndexLocally);

	private readonly R2MirrorClient _r2;

	private readonly DiskSpaceService _disk;

	private readonly ProvisioningService _prov;

	private readonly JsonSettingsStore _settings;

	private readonly DataRootResolver _resolver;

	[ObservableProperty]
	[NotifyPropertyChangedFor("IsWelcome")]
	[NotifyPropertyChangedFor("IsChooseTier")]
	[NotifyPropertyChangedFor("IsProvisioning")]
	private WizardStep _step;

	[ObservableProperty]
	private string _targetFolder = "";

	[ObservableProperty]
	private string _freeSpaceText = "";

	[ObservableProperty]
	private string _statusText = "";

	[ObservableProperty]
	private bool _statusIsError;

	[ObservableProperty]
	private bool _buildIndexLocally;

	[ObservableProperty]
	private long _bytesDone;

	[ObservableProperty]
	private long _bytesTotal;

	[ObservableProperty]
	private string _retryMessage = "";

	[ObservableProperty]
	[NotifyPropertyChangedFor("ShowBuildLocally")]
	[NotifyCanExecuteChangedFor("StartProvisionCommand")]
	private TierCardViewModel? _selectedTier;

	private CancellationTokenSource? _cts;

	[ObservableProperty]
	private bool _rememberOnlineChoice = true;








	public string? Result { get; private set; }

	public bool IsWelcome => Step == WizardStep.Welcome;

	public bool IsChooseTier => Step == WizardStep.ChooseTier;

	public bool IsProvisioning => Step == WizardStep.Provisioning;

	public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

	public ObservableCollection<TierCardViewModel> Tiers { get; } = new ObservableCollection<TierCardViewModel>();

	public bool ShowBuildLocally
	{
		get
		{
			TierCardViewModel? selectedTier = SelectedTier;
			if (selectedTier == null)
			{
				return false;
			}
			return selectedTier.Tier == InstallTier.Full;
		}
	}

	public bool ShouldAutoGoOnline => _settings.Load().Paths.PreferOnlineWhenNoDrive;



















	public event Action? RequestClose;

	public SetupWizardViewModel(R2MirrorClient r2, DiskSpaceService disk, ProvisioningService prov, JsonSettingsStore settings, DataRootResolver resolver)
	{
		_r2 = r2;
		_disk = disk;
		_prov = prov;
		_settings = settings;
		_resolver = resolver;
	}

	[RelayCommand]
	private async Task BrowseAsync()
	{
		OpenFolderDialog openFolderDialog = new OpenFolderDialog
		{
			Title = SharedStrings.S948
		};
		if (openFolderDialog.ShowDialog() != true)
		{
			return;
		}
		string folderName = openFolderDialog.FolderName;
		if (!IsWritable(folderName))
		{
			HebrewMessageBox.Show(SharedStrings.S949, SharedStrings.S950, MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}
		PendingProvision pendingProvision = FindPendingProvision(folderName);
		if ((object)pendingProvision != null && HebrewMessageBox.Show(SharedStrings.S2387, SharedStrings.S2388, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
		{
			TargetFolder = pendingProvision.Root;
			BuildIndexLocally = pendingProvision.BuildIndexLocally;
			await RunCatalogProvisionAsync(pendingProvision.Tier);
			return;
		}
		string text = (_prov.IsCompleteDataRoot(folderName) ? folderName : (_prov.IsCompleteDataRoot(Path.Combine(folderName, "HebrewBooks")) ? Path.Combine(folderName, "HebrewBooks") : null));
		if (text != null && HebrewMessageBox.Show(SharedStrings.S951, SharedStrings.S952, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
		{
			Persist(text, "Full", buildLocally: false, provisionPending: false, false);
			Result = text;
			this.RequestClose?.Invoke();
		}
		else
		{
			TargetFolder = ResolveDataRoot(folderName);
			long num = _disk.AvailableFreeBytes(folderName);
			FreeSpaceText = SharedStrings.S2334 + FormatBytes(num);
			await BuildTierCardsAsync(num);
			Step = WizardStep.ChooseTier;
		}
	}

	[RelayCommand]
	private void RetryDetect()
	{
		RetryMessage = "";
		try
		{
			string result = _resolver.Resolve();
			Result = result;
			this.RequestClose?.Invoke();
		}
		catch (DataRootNotFoundException)
		{
			RetryMessage = SharedStrings.S954;
		}
	}

	[RelayCommand]
	private void Exit()
	{
		Result = null;
		this.RequestClose?.Invoke();
	}

	private async Task BuildTierCardsAsync(long freeBytes)
	{
		long num = await Task.Run(() => _disk.UsedByExistingData(TargetFolder));
		Tiers.Clear();
		foreach (TierInfo item in InstallTiers.Build(1503238553L, 34305552145L, 688900000000L))
		{
			bool flag = _disk.Fits(item.RequiredBytes, TargetFolder, 5368709120L, num);
			long bytes = Math.Max(0L, item.RequiredBytes - Math.Min(num, item.RequiredBytes));
			string sizeText = ((item.Tier == InstallTier.Empty) ? SharedStrings.S955 : (SharedStrings.S2335 + FormatBytes(bytes)));
			Tiers.Add(new TierCardViewModel(item.Tier, TierTitle(item.Tier), TierSubtitle(item.Tier), sizeText, flag, flag ? "" : SharedStrings.S957));
		}
		SelectedTier = Tiers.FirstOrDefault((TierCardViewModel t) => t.Fits);
	}

	private static string TierTitle(InstallTier t)
	{
		return t switch
		{
			InstallTier.Online => SharedStrings.S958, 
			InstallTier.Empty => SharedStrings.S959, 
			InstallTier.CatalogOnly => SharedStrings.S960, 
			InstallTier.CatalogPlusIndex => SharedStrings.S961, 
			_ => SharedStrings.S962, 
		};
	}

	private static string TierSubtitle(InstallTier t)
	{
		return t switch
		{
			InstallTier.Online => SharedStrings.S963, 
			InstallTier.Empty => SharedStrings.S964, 
			InstallTier.CatalogOnly => SharedStrings.S965, 
			InstallTier.CatalogPlusIndex => SharedStrings.S966, 
			_ => SharedStrings.S967, 
		};
	}

	private bool CanStartProvision()
	{
		return SelectedTier?.Fits ?? false;
	}

	[RelayCommand(CanExecute = "CanStartProvision")]
	private async Task StartProvisionAsync()
	{
		TierCardViewModel selectedTier = SelectedTier;
		if (selectedTier == null)
		{
			return;
		}
		if (selectedTier.Tier == InstallTier.Empty)
		{
			try
			{
				_prov.CreateEmptyDataRoot(TargetFolder);
				Persist(TargetFolder, "Empty", buildLocally: false, provisionPending: false);
				Result = TargetFolder;
				this.RequestClose?.Invoke();
				return;
			}
			catch (Exception ex)
			{
				StatusIsError = true;
				StatusText = SharedStrings.S9086 + ex.Message;
				return;
			}
		}
		await RunCatalogProvisionAsync(selectedTier.Tier);
	}

	private async Task RunCatalogProvisionAsync(InstallTier tier)
	{
		Step = WizardStep.Provisioning;
		StatusIsError = false;
		_cts = new CancellationTokenSource();
		AddLog(SharedStrings.S2336 + TargetFolder + "\\App ...");
		Progress<(long, long, int, int)> progress = new Progress<(long, long, int, int)>(delegate((long Bytes, long Total, int Files, int TotalFiles) t)
		{
			BytesDone = t.Bytes;
			BytesTotal = t.Total;
			StatusText = $"{SharedStrings.S2337}{FormatBytes(t.Bytes)} / {FormatBytes(t.Total)} ({t.Files}/{t.TotalFiles}{SharedStrings.S2338}";
		});
		string installType = InstallTiers.ToInstallType(tier);
		ProvisionPlan plan = InstallTiers.ToPlan(tier, BuildIndexLocally);
		Persist(TargetFolder, installType, BuildIndexLocally, provisionPending: true);
		try
		{
			await _prov.ProvisionCatalogBlockingAsync(TargetFolder, progress, _cts.Token);
			Persist(TargetFolder, installType, BuildIndexLocally, plan.HasWork, tier == InstallTier.Online);
			AddLog(plan.HasWork ? SharedStrings.S971 : ((tier == InstallTier.Online) ? SharedStrings.S972 : SharedStrings.S973));
			Result = TargetFolder;
			this.RequestClose?.Invoke();
		}
		catch (OperationCanceledException)
		{
			StatusText = SharedStrings.S974;
			AddLog(SharedStrings.S975);
			Step = ((tier != InstallTier.Online) ? WizardStep.ChooseTier : WizardStep.Welcome);
		}
		catch (Exception ex2)
		{
			StatusIsError = true;
			StatusText = SharedStrings.S9087 + ex2.Message;
			AddLog("✗ " + ex2.Message);
			Step = ((tier != InstallTier.Online) ? WizardStep.ChooseTier : WizardStep.Welcome);
		}
		finally
		{
			_cts?.Dispose();
			_cts = null;
		}
	}

	[RelayCommand]
	private async Task GoOnlineAsync()
	{
		string text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HebrewBooks", "Data");
		try
		{
			Directory.CreateDirectory(text);
		}
		catch
		{
		}
		if (!IsWritable(text))
		{
			HebrewMessageBox.Show(SharedStrings.S977, SharedStrings.S978, MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}
		TargetFolder = ResolveDataRoot(text);
		_settings.Update(delegate(BookshelfOptions o)
		{
			o.Paths.PreferOnlineWhenNoDrive = RememberOnlineChoice;
		});
		await RunCatalogProvisionAsync(InstallTier.Online);
	}

	[RelayCommand]
	private void CancelProvision()
	{
		_cts?.Cancel();
	}

	[RelayCommand]
	private void Back()
	{
		Step = WizardStep.Welcome;
	}

	private static string ResolveDataRoot(string chosen)
	{
		if (!string.Equals(Path.GetFileName(chosen.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)), "HebrewBooks", StringComparison.OrdinalIgnoreCase))
		{
			return Path.Combine(chosen, "HebrewBooks");
		}
		return chosen;
	}

	private void Persist(string root, string installType, bool buildLocally, bool provisionPending, bool? useOnlineService = null)
	{
		string fullPath = Path.GetFullPath(root);
		string text = Path.GetPathRoot(fullPath) ?? fullPath;
		object obj;
		if (fullPath.Length <= text.Length)
		{
			obj = "";
		}
		else
		{
			string text2 = fullPath;
			int length = text.Length;
			obj = text2.Substring(length, text2.Length - length).Trim('\\', '/');
		}
		string sub = (string)obj;
		_settings.Update(delegate(BookshelfOptions o)
		{
			o.Paths.DataSubdir = sub;
			o.Paths.InstallType = installType;
			o.Paths.BuildIndexLocally = buildLocally;
			o.Paths.ProvisionPending = provisionPending;
			if (useOnlineService.HasValue)
			{
				bool valueOrDefault = useOnlineService == true;
				o.UseOnlineService = valueOrDefault;
			}
		});
	}

	private PendingProvision? FindPendingProvision(string folder)
	{
		try
		{
			BookshelfOptions bookshelfOptions = _settings.Load();
			if (!bookshelfOptions.Paths.ProvisionPending || string.IsNullOrWhiteSpace(bookshelfOptions.Paths.DataSubdir))
			{
				return null;
			}
			string fullPath = Path.GetFullPath(folder);
			string pathRoot = Path.GetPathRoot(fullPath);
			if (string.IsNullOrEmpty(pathRoot))
			{
				return null;
			}
			string fullPath2 = Path.GetFullPath(Path.Combine(pathRoot, bookshelfOptions.Paths.DataSubdir));
			if (!string.Equals(Norm(fullPath2), Norm(fullPath), StringComparison.OrdinalIgnoreCase) && !string.Equals(Norm(fullPath2), Norm(ResolveDataRoot(fullPath)), StringComparison.OrdinalIgnoreCase))
			{
				return null;
			}
			return new PendingProvision(fullPath2, InstallTiers.FromInstallType(bookshelfOptions.Paths.InstallType, bookshelfOptions.UseOnlineService), bookshelfOptions.Paths.BuildIndexLocally);
		}
		catch
		{
			return null;
		}
		static string Norm(string p)
		{
			return p.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		}
	}

	private static bool IsWritable(string folder)
	{
		try
		{
			string path = Path.Combine(folder, ".hbwrite");
			File.WriteAllText(path, "");
			File.Delete(path);
			return true;
		}
		catch
		{
			return false;
		}
	}

	private void AddLog(string line)
	{
		Dispatcher dispatcher = Application.Current?.Dispatcher;
		if (dispatcher == null || dispatcher.CheckAccess())
		{
			Add();
		}
		else
		{
			dispatcher.Invoke(Add);
		}
		void Add()
		{
			Logs.Add($"[{DateTime.Now:HH:mm:ss}] {line}");
			if (Logs.Count > 300)
			{
				Logs.RemoveAt(0);
			}
		}
	}

	internal static string FormatBytes(long bytes)
	{
		if (bytes <= 0)
		{
			return "0";
		}
		double num = bytes;
		string[] array = new string[5] { "B", "KB", "MB", "GB", "TB" };
		int num2 = 0;
		while (num >= 1024.0 && num2 < array.Length - 1)
		{
			num /= 1024.0;
			num2++;
		}
		return $"{num:0.#} {array[num2]}";
	}
}
