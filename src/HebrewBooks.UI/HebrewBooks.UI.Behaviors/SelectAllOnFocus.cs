using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace HebrewBooks.UI.Behaviors;

public static class SelectAllOnFocus
{
	public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(SelectAllOnFocus), new PropertyMetadata(false, OnEnabledChanged));

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
		if (d is TextBox textBox)
		{
			textBox.GotKeyboardFocus -= OnGotKeyboardFocus;
			textBox.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;
			object newValue = e.NewValue;
			if (newValue is bool && (bool)newValue)
			{
				textBox.GotKeyboardFocus += OnGotKeyboardFocus;
				textBox.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
			}
		}
	}

	private static void OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
	{
		if (sender is TextBox textBox)
		{
			textBox.SelectAll();
		}
	}

	private static void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		if (sender is TextBox { IsKeyboardFocusWithin: false } textBox)
		{
			textBox.Focus();
			e.Handled = true;
		}
	}
}
