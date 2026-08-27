using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Markup;
using Wpf.Ui.Controls;

namespace HebrewBooks.UI.Views;

public partial class DonateInfoWindow : FluentWindow
{

	public DonateInfoWindow()
	{
		InitializeComponent();
	}

	private void OnClose(object sender, RoutedEventArgs e)
	{
		Close();
	}


}
