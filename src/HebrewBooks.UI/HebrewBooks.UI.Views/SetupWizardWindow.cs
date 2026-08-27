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

public partial class SetupWizardWindow : FluentWindow
{
	private readonly SetupWizardViewModel _vm;


	public string? Result => _vm.Result;

	public SetupWizardWindow(SetupWizardViewModel vm)
	{
		InitializeComponent();
		this.ClampToWorkArea();
		_vm = vm;
		base.DataContext = vm;
		_vm.RequestClose += OnRequestClose;
		base.Loaded += delegate
		{
			if (_vm.ShouldAutoGoOnline)
			{
				_vm.GoOnlineCommand.Execute(null);
			}
		};
	}

	private void OnRequestClose()
	{
		if (base.Dispatcher.CheckAccess())
		{
			Close();
		}
		else
		{
			base.Dispatcher.Invoke(base.Close);
		}
	}


}
