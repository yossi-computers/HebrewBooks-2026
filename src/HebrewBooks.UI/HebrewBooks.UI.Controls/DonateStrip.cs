using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using HebrewBooks.UI.ViewModels;

namespace HebrewBooks.UI.Controls;

public partial class DonateStrip : UserControl
{



	public DonateStrip()
	{
		InitializeComponent();
		DonateStripViewModel vm = App.Services?.GetService(typeof(DonateStripViewModel)) as DonateStripViewModel;
		if (vm != null)
		{
			base.DataContext = vm;
			if (vm.IsKiosk)
			{
				StripLine.Inlines.Remove(DedicateLink);
			}
			base.Loaded += delegate
			{
				vm.LoadAsync();
			};
		}
	}


}
