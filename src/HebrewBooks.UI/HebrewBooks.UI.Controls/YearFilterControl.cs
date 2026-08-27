using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using Wpf.Ui.Controls;

namespace HebrewBooks.UI.Controls;

public partial class YearFilterControl : UserControl
{




	public YearFilterControl()
	{
		InitializeComponent();
	}

	private void OnPopupOpened(object? sender, EventArgs e)
	{
		FromBox.Focus();
		Keyboard.Focus(FromBox);
		PopupZOrderHelper.BringToFront(sender as Popup);
	}

	private void OnBoxKeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key == Key.Escape)
		{
			YearToggle.IsChecked = false;
			e.Handled = true;
		}
		else if (e.Key == Key.Return && sender is System.Windows.Controls.TextBox textBox)
		{
			textBox.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
			e.Handled = true;
		}
	}



}
