using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using HebrewBooks.UI.ViewModels;

namespace HebrewBooks.UI.Views;

public partial class SettingsPage : Page
{

	public SettingsPage()
	{
		InitializeComponent();
		SettingsViewModel vm = (SettingsViewModel)App.Services.GetService(typeof(SettingsViewModel));
		base.DataContext = vm;
		base.Loaded += delegate
		{
			vm.RefreshFromDisk();
			vm.RefreshLibraryDownloadStatusCommand.ExecuteAsync(null);
		};
	}



}
