using System;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace HebrewBooks.UI.Behaviors;

public static class ListBoxScrollIntoView
{
	private static readonly ConditionalWeakTable<ListBox, StrongBox<bool>> _pressing = new ConditionalWeakTable<ListBox, StrongBox<bool>>();

	public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(ListBoxScrollIntoView), new PropertyMetadata(false, OnEnabledChanged));

	public static bool GetEnabled(DependencyObject d)
	{
		return (bool)d.GetValue(EnabledProperty);
	}

	public static void SetEnabled(DependencyObject d, bool value)
	{
		d.SetValue(EnabledProperty, value);
	}

	public static bool IsPressing(ListBox lb)
	{
		if (_pressing.TryGetValue(lb, out StrongBox<bool> value))
		{
			return value.Value;
		}
		return false;
	}

	private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		if (d is ListBox listBox)
		{
			if ((bool)e.NewValue)
			{
				listBox.SelectionChanged += OnSelectionChanged;
				listBox.PreviewMouseWheel += OnPreviewMouseWheel;
				listBox.PreviewMouseLeftButtonDown += OnPreviewMouseDown;
				listBox.PreviewMouseLeftButtonUp += OnPreviewMouseUp;
			}
			else
			{
				listBox.SelectionChanged -= OnSelectionChanged;
				listBox.PreviewMouseWheel -= OnPreviewMouseWheel;
				listBox.PreviewMouseLeftButtonDown -= OnPreviewMouseDown;
				listBox.PreviewMouseLeftButtonUp -= OnPreviewMouseUp;
			}
		}
	}

	private static void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
	{
		if (sender is ListBox key)
		{
			_pressing.GetValue(key, (ListBox _) => new StrongBox<bool>()).Value = true;
		}
	}

	private static void OnPreviewMouseUp(object sender, MouseButtonEventArgs e)
	{
		ListBox lb = sender as ListBox;
		if (lb == null)
		{
			return;
		}
		lb.Dispatcher.BeginInvoke(DispatcherPriority.Background, (Action)delegate
		{
			if (_pressing.TryGetValue(lb, out StrongBox<bool> value))
			{
				value.Value = false;
			}
			object selectedItem = lb.SelectedItem;
			if (selectedItem != null)
			{
				lb.ScrollIntoView(selectedItem);
			}
		});
	}

	private static void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (sender is ListBox { SelectedItem: { } selectedItem } listBox && (!_pressing.TryGetValue(listBox, out StrongBox<bool> value) || !value.Value))
		{
			listBox.ScrollIntoView(selectedItem);
		}
	}

	public static void ScrollHorizontally(ListBox listBox, double delta)
	{
		ScrollViewer scrollViewer = FindScrollViewer(listBox);
		scrollViewer?.ScrollToHorizontalOffset(scrollViewer.HorizontalOffset + delta);
	}

	private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
	{
		if (sender is ListBox root)
		{
			ScrollViewer scrollViewer = FindScrollViewer(root);
			if (scrollViewer != null)
			{
				scrollViewer.ScrollToHorizontalOffset(scrollViewer.HorizontalOffset - (double)e.Delta);
				e.Handled = true;
			}
		}
	}

	private static ScrollViewer? FindScrollViewer(DependencyObject root)
	{
		if (root is ScrollViewer result)
		{
			return result;
		}
		for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
		{
			ScrollViewer scrollViewer = FindScrollViewer(VisualTreeHelper.GetChild(root, i));
			if (scrollViewer != null)
			{
				return scrollViewer;
			}
		}
		return null;
	}
}
