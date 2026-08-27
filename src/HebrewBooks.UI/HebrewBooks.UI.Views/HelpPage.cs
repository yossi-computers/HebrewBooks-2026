using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Navigation;
using HebrewBooks.UI.ViewModels;
using Wpf.Ui.Controls;

namespace HebrewBooks.UI.Views;

public partial class HelpPage : Page
{
	private const double WheelStepPixels = 30.0;

















	public HelpPage()
	{
		InitializeComponent();
		base.DataContext = (HelpViewModel)App.Services.GetService(typeof(HelpViewModel));
	}

	private void OnTocClick(object sender, RoutedEventArgs e)
	{
		e.Handled = true;
		if (!(sender is Hyperlink { Tag: string tag }) || !(FindName(tag) is FrameworkElement frameworkElement))
		{
			return;
		}
		try
		{
			Point point = frameworkElement.TransformToAncestor(ContentScroller).Transform(new Point(0.0, 0.0));
			ContentScroller.ScrollToVerticalOffset(ContentScroller.VerticalOffset + point.Y);
		}
		catch
		{
			frameworkElement.BringIntoView();
		}
	}

	private void OnHelpWheel(object sender, MouseWheelEventArgs e)
	{
		double num = (double)e.Delta / 120.0;
		ContentScroller.ScrollToVerticalOffset(ContentScroller.VerticalOffset - num * 30.0);
		e.Handled = true;
	}

	private void OnOpenDiagnostics(object sender, RoutedEventArgs e)
	{
		if (!App.IsProtectMode)
		{
			(Application.Current.Windows.OfType<MainWindow>().FirstOrDefault() ?? (Application.Current.MainWindow as MainWindow))?.NavigateTo(typeof(DiagnosticsPage));
		}
	}

	private void OnContactEmailClick(object sender, RequestNavigateEventArgs e)
	{
		if (App.IsProtectMode)
		{
			e.Handled = true;
			return;
		}
		try
		{
			Process.Start(new ProcessStartInfo
			{
				FileName = e.Uri.ToString(),
				UseShellExecute = true
			});
		}
		catch
		{
		}
		e.Handled = true;
	}


}
