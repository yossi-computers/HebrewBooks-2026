using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using HebrewBooks.Infrastructure.Settings;
using HebrewBooks.UI.Services;
using Wpf.Ui.Controls;

namespace HebrewBooks.UI.Views;

public partial class SearchWindow : FluentWindow
{


	public SearchWindow()
	{
		InitializeComponent();
		this.OpenAtScaledSize(1280.0, 850.0);
		bool unifiedSearchLayout = ((JsonSettingsStore)App.Services.GetService(typeof(JsonSettingsStore))).Load().View.UnifiedSearchLayout;
		Host.Navigate(unifiedSearchLayout ? ((object)new LibraryPage()) : ((object)new SearchPage()));
		base.Closed += delegate
		{
			try
			{
				App.DisposeWebView2In(this);
			}
			catch
			{
			}
		};
		if (App.IsProtectMode)
		{
			base.ResizeMode = ResizeMode.NoResize;
			base.WindowState = WindowState.Maximized;
		}
	}


}
