using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;

namespace HebrewBooks.Diagnostics.Ui;

public partial class DiagnosticsView : UserControl
{


	public double WheelStepPixels { get; set; }

	public DiagnosticsView()
	{
		InitializeComponent();
	}

	private void OnResultsWheel(object sender, MouseWheelEventArgs e)
	{
		if (!(WheelStepPixels <= 0.0))
		{
			double num = (double)e.Delta / 120.0;
			ResultsScroll.ScrollToVerticalOffset(ResultsScroll.VerticalOffset - num * WheelStepPixels);
			e.Handled = true;
		}
	}


}
