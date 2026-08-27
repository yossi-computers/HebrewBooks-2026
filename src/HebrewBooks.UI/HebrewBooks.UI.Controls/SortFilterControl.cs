using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;

namespace HebrewBooks.UI.Controls;

public partial class SortFilterControl : UserControl
{



	public SortFilterControl()
	{
		InitializeComponent();
	}

	private void OnPopupOpened(object? sender, EventArgs e)
	{
		PopupZOrderHelper.BringToFront(sender as Popup);
	}



}
