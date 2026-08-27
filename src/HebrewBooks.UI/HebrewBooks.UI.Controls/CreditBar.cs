using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Navigation;
using HebrewBooks.UI.Resources;

namespace HebrewBooks.UI.Controls;

public partial class CreditBar : UserControl
{


	public CreditBar()
	{
		InitializeComponent();
		if (App.IsProtectMode)
		{
			EmailLink.TextDecorations = null;
			EmailLink.Foreground = null;
			EmailLink.Cursor = Cursors.Arrow;
			EmailLink.ToolTip = null;
		}
		else
		{
			EmailLink.ToolTip = SharedStrings.S555;
		}
	}

	private async void OnEmailClick(object sender, RequestNavigateEventArgs e)
	{
		e.Handled = true;
		if (App.IsProtectMode)
		{
			return;
		}
		string text = e.Uri.AbsoluteUri.Replace("mailto:", "");
		try
		{
			Clipboard.SetText(text);
		}
		catch
		{
			return;
		}
		Run run = EmailLink.Inlines.OfType<Run>().FirstOrDefault();
		if (run == null)
		{
			return;
		}
		string original = run.Text;
		run.Text = SharedStrings.S556;
		try
		{
			await Task.Delay(1500);
		}
		finally
		{
			run.Text = original;
		}
	}


}
