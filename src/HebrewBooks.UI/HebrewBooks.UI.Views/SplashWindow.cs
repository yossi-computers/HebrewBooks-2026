using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;

namespace HebrewBooks.UI.Views;

public partial class SplashWindow : Window
{


	public SplashWindow()
	{
		InitializeComponent();
		base.Loaded += delegate
		{
			try
			{
				Activate();
				base.Topmost = true;
				base.Topmost = false;
			}
			catch
			{
			}
		};
	}

	public void SetStatus(string text)
	{
		if (base.Dispatcher.CheckAccess())
		{
			StatusText.Text = text;
			return;
		}
		base.Dispatcher.BeginInvoke((Action)delegate
		{
			StatusText.Text = text;
		});
	}

	private void OnDragMove(object sender, MouseButtonEventArgs e)
	{
		if (e.ChangedButton == MouseButton.Left)
		{
			try
			{
				DragMove();
			}
			catch
			{
			}
		}
	}


}
