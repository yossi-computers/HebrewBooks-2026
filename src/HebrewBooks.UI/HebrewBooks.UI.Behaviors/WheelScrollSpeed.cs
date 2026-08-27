using System;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace HebrewBooks.UI.Behaviors;

public static class WheelScrollSpeed
{
	private const double DefaultLinesPerNotch = 2.0;

	public static readonly DependencyProperty LinesPerNotchProperty = DependencyProperty.RegisterAttached("LinesPerNotch", typeof(double), typeof(WheelScrollSpeed), new PropertyMetadata(0.0));

	public static readonly DependencyProperty BypassProperty = DependencyProperty.RegisterAttached("Bypass", typeof(bool), typeof(WheelScrollSpeed), new PropertyMetadata(false));

	private static readonly ConditionalWeakTable<ScrollViewer, StrongBox<double>> Accumulators = new ConditionalWeakTable<ScrollViewer, StrongBox<double>>();

	public static double GetLinesPerNotch(DependencyObject d)
	{
		return (double)d.GetValue(LinesPerNotchProperty);
	}

	public static void SetLinesPerNotch(DependencyObject d, double value)
	{
		d.SetValue(LinesPerNotchProperty, value);
	}

	public static bool GetBypass(DependencyObject d)
	{
		return (bool)d.GetValue(BypassProperty);
	}

	public static void SetBypass(DependencyObject d, bool value)
	{
		d.SetValue(BypassProperty, value);
	}

	public static void Install()
	{
		EventManager.RegisterClassHandler(typeof(ScrollViewer), UIElement.PreviewMouseWheelEvent, new MouseWheelEventHandler(OnClassPreviewWheel));
	}

	private static void OnClassPreviewWheel(object sender, MouseWheelEventArgs e)
	{
		if (e.Handled || e.Delta == 0)
		{
			return;
		}
		ScrollViewer scrollViewer = NearestVerticalScroller(e.OriginalSource as DependencyObject);
		if (scrollViewer == null)
		{
			return;
		}
		for (DependencyObject dependencyObject = scrollViewer; dependencyObject != null; dependencyObject = VisualTreeHelper.GetParent(dependencyObject))
		{
			if (GetBypass(dependencyObject))
			{
				return;
			}
		}
		double num = ResolveLinesPerNotch(scrollViewer);
		StrongBox<double> value = Accumulators.GetValue(scrollViewer, (ScrollViewer _) => new StrongBox<double>(0.0));
		double num2 = value.Value;
		if (num2 != 0.0 && Math.Sign(e.Delta) != Math.Sign(num2))
		{
			num2 = 0.0;
		}
		double num3 = num2 + (double)e.Delta / 120.0 * num;
		int num4 = (int)num3;
		value.Value = num3 - (double)num4;
		if (num4 > 0)
		{
			for (int num5 = 0; num5 < num4; num5++)
			{
				scrollViewer.LineUp();
			}
		}
		else if (num4 < 0)
		{
			for (int num6 = 0; num6 < -num4; num6++)
			{
				scrollViewer.LineDown();
			}
		}
		e.Handled = true;
	}

	private static ScrollViewer? NearestVerticalScroller(DependencyObject? d)
	{
		DependencyObject dependencyObject = d;
		while (dependencyObject != null)
		{
			if (dependencyObject is ScrollViewer { ScrollableHeight: >0.0 } scrollViewer)
			{
				return scrollViewer;
			}
			bool flag = ((dependencyObject is Visual || dependencyObject is Visual3D) ? true : false);
			dependencyObject = (flag ? VisualTreeHelper.GetParent(dependencyObject) : LogicalTreeHelper.GetParent(dependencyObject));
		}
		return null;
	}

	private static double ResolveLinesPerNotch(DependencyObject d)
	{
		for (DependencyObject dependencyObject = d; dependencyObject != null; dependencyObject = VisualTreeHelper.GetParent(dependencyObject))
		{
			double linesPerNotch = GetLinesPerNotch(dependencyObject);
			if (linesPerNotch > 0.0)
			{
				return linesPerNotch;
			}
		}
		return 2.0;
	}
}
