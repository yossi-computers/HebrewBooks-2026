using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Markup;
using HebrewBooks.Core.Models;
using HebrewBooks.UI.Services;
using HebrewBooks.UI.ViewModels;
using Wpf.Ui.Controls;

namespace HebrewBooks.UI.Views;

public partial class EditBookWindow : FluentWindow
{
	private readonly EditBookViewModel _vm;


	public EditBookWindow(EditBookViewModel vm)
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

	public void LoadBook(Book book)
	{
		_vm.Load(book);
	}

	private void OnCancel(object sender, RoutedEventArgs e)
	{
		base.DialogResult = false;
		Close();
	}


}
