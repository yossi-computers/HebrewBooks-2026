using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using HebrewBooks.UI.Resources;
using HebrewBooks.UI.Services;
using Wpf.Ui.Controls;

namespace HebrewBooks.UI.Views;

public partial class DedicationWindow : FluentWindow
{
	private static readonly string[] Kinds = new string[4] { "memory", "healing", "success", "merit" };

	private readonly DonationsClient _client;












	public DedicationWindow(DonationsClient client)
	{
		_client = client;
		InitializeComponent();
		KindBox.ItemsSource = new string[4]
		{
			SharedStrings.DonateKindMemory,
			SharedStrings.DonateKindHealing,
			SharedStrings.DonateKindSuccess,
			SharedStrings.DonateKindMerit
		};
		KindBox.SelectedIndex = 0;
		base.Loaded += delegate
		{
			WordingInput.Focus();
		};
	}

	public void Configure(DonationConfig? config)
	{
		if ((object)config != null && !(config.DedicationPrice <= 0.0))
		{
			string arg = config.Currency + ((Math.Abs(config.DedicationPrice - Math.Round(config.DedicationPrice)) < 0.005) ? Math.Round(config.DedicationPrice).ToString("0") : config.DedicationPrice.ToString("0.00"));
			TermsText.Text = string.Format(SharedStrings.DedicationTerms, arg, config.Days);
			TermsBox.Visibility = Visibility.Visible;
		}
	}

	private void OnTextChanged(object sender, TextChangedEventArgs e)
	{
		SendButton.IsEnabled = !string.IsNullOrWhiteSpace(WordingInput.Text);
	}

	private void OnCancel(object sender, RoutedEventArgs e)
	{
		Close();
	}

	private async void OnSend(object sender, RoutedEventArgs e)
	{
		string text = WordingInput.Text?.Trim();
		if (!string.IsNullOrWhiteSpace(text))
		{
			SetBusy(busy: true);
			string kind = Kinds[Math.Clamp(KindBox.SelectedIndex, 0, Kinds.Length - 1)];
			DedicationSubmitResult dedicationSubmitResult = await _client.SubmitDedicationAsync(kind, text, DonorInput.Text, EmailInput.Text);
			SetBusy(busy: false);
			if (dedicationSubmitResult.Ok)
			{
				System.Windows.MessageBox.Show(this, SharedStrings.DedicationSent, SharedStrings.DedicationTitle, System.Windows.MessageBoxButton.OK, MessageBoxImage.Asterisk);
				Close();
				return;
			}
			System.Windows.Controls.TextBlock statusText = StatusText;
			string errorCode = dedicationSubmitResult.ErrorCode;
			string text2 = ((errorCode == "rate-limited") ? SharedStrings.DedicationTooMany : ((!(errorCode == "offline")) ? SharedStrings.DedicationFailed : SharedStrings.DedicationOffline));
			statusText.Text = text2;
			StatusText.Foreground = (Brush)FindResource("SystemFillColorCriticalBrush");
			StatusText.Visibility = Visibility.Visible;
		}
	}

	private void SetBusy(bool busy)
	{
		Busy.Visibility = ((!busy) ? Visibility.Collapsed : Visibility.Visible);
		SendButton.IsEnabled = !busy && !string.IsNullOrWhiteSpace(WordingInput.Text);
		CancelButton.IsEnabled = !busy;
		KindBox.IsEnabled = !busy;
		WordingInput.IsEnabled = !busy;
		DonorInput.IsEnabled = !busy;
		EmailInput.IsEnabled = !busy;
		if (busy)
		{
			StatusText.Visibility = Visibility.Collapsed;
		}
	}


}
