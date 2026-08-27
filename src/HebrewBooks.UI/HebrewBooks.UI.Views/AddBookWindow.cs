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

public partial class AddBookWindow : FluentWindow
{
	private readonly AddBookViewModel _vm;



	public AddBookWindow(AddBookViewModel vm)
	{
		InitializeComponent();
		this.ClampToWorkArea();
		_vm = vm;
		base.DataContext = vm;
		vm.Saved += delegate
		{
			base.DialogResult = true;
			Close();
		};
	}

	private void OnCancel(object sender, RoutedEventArgs e)
	{
		base.DialogResult = false;
		Close();
	}


}
