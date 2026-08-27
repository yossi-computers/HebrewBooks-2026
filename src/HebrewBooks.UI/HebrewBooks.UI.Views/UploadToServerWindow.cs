using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Markup;
using HebrewBooks.UI.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Serilog;
using Wpf.Ui.Controls;

namespace HebrewBooks.UI.Views;

public partial class UploadToServerWindow : FluentWindow
{
	private const string UploadUrl = "https://hebrewbooks.pages.dev/upload?embed=1";



	public UploadToServerWindow()
	{
		InitializeComponent();
		this.ClampToWorkArea();
		base.Loaded += OnLoaded;
	}

	private async void OnLoaded(object sender, RoutedEventArgs e)
	{
		_ = 1;
		try
		{
			CoreWebView2Environment environment = await WebViewEnvironment.GetAsync();
			await Web.EnsureCoreWebView2Async(environment);
			Web.CoreWebView2.Navigate("https://hebrewbooks.pages.dev/upload?embed=1");
		}
		catch (Exception exception)
		{
			Log.Warning(exception, "UploadToServerWindow: WebView2 init/navigate failed");
		}
	}


}
