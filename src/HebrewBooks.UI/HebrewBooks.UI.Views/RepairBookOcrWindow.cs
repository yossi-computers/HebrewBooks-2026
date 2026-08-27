using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Markup;
using HebrewBooks.UI.Dialogs;
using HebrewBooks.UI.Resources;
using HebrewBooks.UI.Services;
using HebrewBooks.UI.ViewModels;
using Wpf.Ui.Controls;

namespace HebrewBooks.UI.Views;

public partial class RepairBookOcrWindow : FluentWindow
{


	public RepairBookOcrWindow(RepairBookOcrViewModel vm)
	{
		InitializeComponent();
		this.ClampToWorkArea();
		base.DataContext = vm;
	}

	private void OnCloseClick(object sender, RoutedEventArgs e)
	{
		Close();
	}

	private void OnClosing(object? sender, CancelEventArgs e)
	{
		if (base.DataContext is RepairBookOcrViewModel { IsRunning: not false } repairBookOcrViewModel)
		{
			switch (HebrewMessageBox.Show(SharedStrings.RepairOcrCloseWhileRunning, SharedStrings.RepairOcrCloseTitle, System.Windows.MessageBoxButton.YesNoCancel, MessageBoxImage.Question))
			{
			case System.Windows.MessageBoxResult.No:
				repairBookOcrViewModel.CancelCommand.Execute(null);
				break;
			default:
				e.Cancel = true;
				break;
			case System.Windows.MessageBoxResult.Yes:
				break;
			}
		}
	}


}
