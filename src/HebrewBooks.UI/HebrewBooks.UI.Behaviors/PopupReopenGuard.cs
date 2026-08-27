using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls.Primitives;

namespace HebrewBooks.UI.Behaviors;

public static class PopupReopenGuard
{
	private const double GuardMs = 220.0;

	public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(PopupReopenGuard), new PropertyMetadata(false, OnEnabledChanged));

	private static readonly DependencyProperty UncheckedAtProperty = DependencyProperty.RegisterAttached("UncheckedAt", typeof(long), typeof(PopupReopenGuard), new PropertyMetadata(long.MinValue));

	private static readonly DependencyProperty HookedProperty = DependencyProperty.RegisterAttached("Hooked", typeof(bool), typeof(PopupReopenGuard), new PropertyMetadata(false));

	public static bool GetEnabled(DependencyObject d)
	{
		return (bool)d.GetValue(EnabledProperty);
	}

	public static void SetEnabled(DependencyObject d, bool value)
	{
		d.SetValue(EnabledProperty, value);
	}

	private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		if (d is Popup popup)
		{
			popup.Opened -= OnOpened;
			object newValue = e.NewValue;
			if (newValue is bool && (bool)newValue)
			{
				popup.Opened += OnOpened;
			}
		}
	}

	private static void OnOpened(object? sender, EventArgs e)
	{
		if (sender is Popup { PlacementTarget: ToggleButton placementTarget } && !(bool)placementTarget.GetValue(HookedProperty))
		{
			placementTarget.SetValue(HookedProperty, true);
			placementTarget.Unchecked += OnToggleUnchecked;
			placementTarget.Checked += OnToggleChecked;
		}
	}

	private static void OnToggleUnchecked(object sender, RoutedEventArgs e)
	{
		((ToggleButton)sender).SetValue(UncheckedAtProperty, Stopwatch.GetTimestamp());
	}

	private static void OnToggleChecked(object sender, RoutedEventArgs e)
	{
		ToggleButton toggleButton = (ToggleButton)sender;
		long num = (long)toggleButton.GetValue(UncheckedAtProperty);
		if (num != long.MinValue && !((double)(Stopwatch.GetTimestamp() - num) * 1000.0 / (double)Stopwatch.Frequency >= 220.0))
		{
			toggleButton.IsChecked = false;
		}
	}
}
