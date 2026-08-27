using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Markup;
using HebrewBooks.UI.Services;
using HebrewBooks.UI.ViewModels;
using Wpf.Ui.Controls;

namespace HebrewBooks.UI.Views;

public partial class DeleteBookWindow : FluentWindow
{

	public DeleteBookViewModel ViewModel => (DeleteBookViewModel)base.DataContext;

	public DeleteBookWindow(DeleteBookViewModel vm)
	{
		InitializeComponent();
		this.ClampToWorkArea();
		base.DataContext = vm;
	}

	private void OnCloseClick(object sender, RoutedEventArgs e)
	{
		Close();
	}


}
