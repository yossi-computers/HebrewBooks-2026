using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;
using CommunityToolkit.Mvvm.Input;
using HebrewBooks.Core.Abstractions;
using HebrewBooks.UI.Resources;
using HebrewBooks.UI.Services;
using HebrewBooks.UI.Views;
using Serilog;

namespace HebrewBooks.UI.ViewModels;

public partial class DonateStripViewModel : ObservableObject
{
	private static readonly TimeSpan RotateInterval = TimeSpan.FromSeconds(9.0);

	private readonly DonationsClient _client;

	private readonly IProtectMode? _protect;

	private DonationConfig? _config;

	private IReadOnlyList<string> _dedicationLines = Array.Empty<string>();

	private int _rotateIndex;

	private DispatcherTimer? _rotateTimer;

	private bool _loaded;

	[ObservableProperty]
	private bool _visible;

	[ObservableProperty]
	private bool _meterVisible;

	[ObservableProperty]
	private string _meterLabel = string.Empty;

	[ObservableProperty]
	private int _meterPercent;

	[ObservableProperty]
	private string _dedicationLine = string.Empty;

	[ObservableProperty]
	private bool _dedicationLineVisible;



	public bool IsKiosk => _protect?.IsActive ?? false;

	public bool CanDedicate => !IsKiosk;

	public string StripText => SharedStrings.DonateStripText;

	public string DedicationLinkText => SharedStrings.DonateStripDedicationLink;

	public string ButtonText
	{
		get
		{
			if (!IsKiosk)
			{
				return SharedStrings.DonateStripButton;
			}
			return SharedStrings.DonateKioskButton;
		}
	}

	public string ButtonTooltip => SharedStrings.DonateStripButtonTitle;









	public DonateStripViewModel(DonationsClient client, IProtectMode? protectMode = null)
	{
		_client = client;
		_protect = protectMode;
	}

	public async Task LoadAsync(CancellationToken ct = default(CancellationToken))
	{
		if (_loaded)
		{
			return;
		}
		_loaded = true;
		try
		{
			DonationConfig donationConfig = await _client.GetConfigAsync(ct).ConfigureAwait(continueOnCapturedContext: true);
			if ((object)donationConfig != null && donationConfig.Enabled)
			{
				_config = donationConfig;
				Visible = true;
				DonationProgress donationProgress = await _client.GetProgressAsync(ct).ConfigureAwait(continueOnCapturedContext: true);
				if ((object)donationProgress != null)
				{
					MeterLabel = string.Format(SharedStrings.DonateMeterLabel, Money(donationProgress.Raised, donationProgress.Currency), Money(donationProgress.Goal, donationProgress.Currency));
					MeterPercent = donationProgress.Percent;
					MeterVisible = true;
				}
				_dedicationLines = (await _client.GetActiveDedicationsAsync(ct).ConfigureAwait(continueOnCapturedContext: true)).Select(Format).ToList();
				StartRotation();
			}
		}
		catch (Exception exception)
		{
			Log.Debug(exception, "DonateStripViewModel: load failed; strip stays hidden");
		}
	}

	private static string Format(PublicDedication d)
	{
		string text = (d.Kind switch
		{
			"healing" => SharedStrings.DonateKindHealing, 
			"success" => SharedStrings.DonateKindSuccess, 
			"merit" => SharedStrings.DonateKindMerit, 
			_ => SharedStrings.DonateKindMemory, 
		} + " " + d.Text).Trim();
		if (!string.IsNullOrWhiteSpace(d.DonorName))
		{
			return text + " · " + string.Format(SharedStrings.DonateDedicatedBy, d.DonorName);
		}
		return text;
	}

	private static string Money(double n, string currency)
	{
		string text = ((Math.Abs(n - Math.Round(n)) < 0.005) ? Math.Round(n).ToString("0") : n.ToString("0.00"));
		return currency + text;
	}

	private void StartRotation()
	{
		if (_dedicationLines.Count == 0)
		{
			return;
		}
		DedicationLine = _dedicationLines[0];
		DedicationLineVisible = true;
		if (_dedicationLines.Count != 1)
		{
			_rotateTimer = new DispatcherTimer
			{
				Interval = RotateInterval
			};
			_rotateTimer.Tick += delegate
			{
				_rotateIndex = (_rotateIndex + 1) % _dedicationLines.Count;
				DedicationLine = _dedicationLines[_rotateIndex];
			};
			_rotateTimer.Start();
		}
	}

	[RelayCommand]
	private void Donate()
	{
		if (IsKiosk)
		{
			ShowKioskInfo();
			return;
		}
		string text = _config?.DonateUrl;
		if (!string.IsNullOrWhiteSpace(text))
		{
			try
			{
				Process.Start(new ProcessStartInfo(text)
				{
					UseShellExecute = true
				});
			}
			catch (Exception exception)
			{
				Log.Warning(exception, "DonateStripViewModel: opening the donation page failed");
				return;
			}
			OpenDedication();
		}
	}

	[RelayCommand]
	private void OpenDedication()
	{
		if (IsKiosk)
		{
			ShowKioskInfo();
			return;
		}
		try
		{
			DedicationWindow obj = (DedicationWindow)App.Services.GetService(typeof(DedicationWindow));
			obj.Configure(_config);
			obj.Owner = Application.Current?.MainWindow;
			obj.ShowDialog();
		}
		catch (Exception exception)
		{
			Log.Warning(exception, "DonateStripViewModel: opening the dedication form failed");
		}
	}

	private void ShowKioskInfo()
	{
		try
		{
			DonateInfoWindow donateInfoWindow = new DonateInfoWindow();
			donateInfoWindow.Owner = Application.Current?.MainWindow;
			donateInfoWindow.ShowDialog();
		}
		catch (Exception exception)
		{
			Log.Warning(exception, "DonateStripViewModel: opening the kiosk donation info failed");
		}
	}
}
