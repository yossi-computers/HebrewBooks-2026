using System;
using System.Windows;

namespace HebrewBooks.UI.Services;

public static class WindowFit
{
	private const double WorkAreaFill = 0.9;

	private const double MinScale = 0.5;

	private const double MaxScale = 1.5;

	public static void OpenAtScaledSize(this Window window, double designWidth, double designHeight)
	{
		if (!(designWidth <= 0.0) && !(designHeight <= 0.0))
		{
			Rect workArea = SystemParameters.WorkArea;
			double val = workArea.Width * 0.9 / designWidth;
			double val2 = workArea.Height * 0.9 / designHeight;
			double val3 = Math.Min(val, val2);
			val3 = Math.Max(0.5, Math.Min(1.5, val3));
			window.Width = designWidth * val3;
			window.Height = designHeight * val3;
			window.MaxWidth = workArea.Width;
			window.MaxHeight = workArea.Height;
		}
	}

	public static void ClampToWorkArea(this Window window, double margin = 24.0)
	{
		Rect workArea = SystemParameters.WorkArea;
		window.MaxWidth = Math.Max(320.0, workArea.Width - margin);
		window.MaxHeight = Math.Max(320.0, workArea.Height - margin);
		if (!double.IsNaN(window.Width) && window.Width > window.MaxWidth)
		{
			window.Width = window.MaxWidth;
		}
		if (!double.IsNaN(window.Height) && window.Height > window.MaxHeight)
		{
			window.Height = window.MaxHeight;
		}
	}
}
