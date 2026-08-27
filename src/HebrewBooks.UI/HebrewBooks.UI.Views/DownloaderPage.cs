using System;
using System.CodeDom.Compiler;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Threading;
using HebrewBooks.UI.Resources;
using HebrewBooks.UI.ViewModels;

namespace HebrewBooks.UI.Views;

public partial class DownloaderPage : Page
{
	private DownloaderViewModel? _vm;

	private bool _initialized;




	public DownloaderPage()
	{
		InitializeComponent();
		base.Loaded += OnLoaded;
	}

	private async void OnLoaded(object sender, RoutedEventArgs e)
	{
		if (_initialized)
		{
			return;
		}
		_initialized = true;
		if (App.IsNetworkInstall)
		{
			base.Content = new TextBlock
			{
				Text = SharedStrings.S991,
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center,
				TextWrapping = TextWrapping.Wrap,
				Margin = new Thickness(24.0),
				FontSize = 16.0
			};
			return;
		}
		_vm = (DownloaderViewModel)App.Services.GetService(typeof(DownloaderViewModel));
		base.DataContext = _vm;
		INotifyCollectionChanged logs = _vm.Logs;
		if (logs != null)
		{
			logs.CollectionChanged += delegate(object? _, NotifyCollectionChangedEventArgs args)
			{
				if (args.Action == NotifyCollectionChangedAction.Add)
				{
					base.Dispatcher.BeginInvoke(DispatcherPriority.Background, (Action)delegate
					{
						if (LogList.Items.Count > 0)
						{
							LogList.ScrollIntoView(LogList.Items[LogList.Items.Count - 1]);
						}
					});
				}
			};
		}
		await _vm.RefreshCommand.ExecuteAsync(null);
	}


}
