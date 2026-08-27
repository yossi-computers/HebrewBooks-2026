using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using HebrewBooks.UI.Resources;
using Wpf.Ui.Controls;

namespace HebrewBooks.UI.Views;

public partial class TocQuickAddDialog : FluentWindow
{




	public string EnteredTitle { get; private set; } = string.Empty;

	public TocQuickAddDialog(int currentPage)
	{
		InitializeComponent();
		LabelText.Text = $"{SharedStrings.S2386}{currentPage}:";
		base.Loaded += delegate
		{
			TitleInput.Focus();
		};
	}

	private void OnOk(object sender, RoutedEventArgs e)
	{
		EnteredTitle = (TitleInput.Text ?? string.Empty).Trim();
		base.DialogResult = !string.IsNullOrEmpty(EnteredTitle);
		Close();
	}

	private void OnCancel(object sender, RoutedEventArgs e)
	{
		base.DialogResult = false;
		Close();
	}


}
