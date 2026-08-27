using System.Windows;
using System.Windows.Controls;
using HebrewBooks.UI.Resources;

namespace HebrewBooks.UI.Dialogs;

public static class PerformanceHintDialog
{
	public static bool Show(Window? owner, string message)
	{
		Window dialog = new Window
		{
			Title = SharedStrings.PerfHintTitle,
			Width = 420.0,
			SizeToContent = SizeToContent.Height,
			WindowStartupLocation = ((owner == null) ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner),
			Owner = owner,
			FlowDirection = FlowDirection.RightToLeft,
			ResizeMode = ResizeMode.NoResize,
			ShowInTaskbar = false
		};
		StackPanel stackPanel = new StackPanel
		{
			Margin = new Thickness(16.0)
		};
		stackPanel.Children.Add(new TextBlock
		{
			Text = message,
			Margin = new Thickness(0.0, 0.0, 0.0, 12.0),
			TextWrapping = TextWrapping.Wrap
		});
		CheckBox checkBox = new CheckBox
		{
			Content = SharedStrings.PerfHintDontShow,
			Margin = new Thickness(0.0, 0.0, 0.0, 14.0)
		};
		stackPanel.Children.Add(checkBox);
		StackPanel stackPanel2 = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			HorizontalAlignment = HorizontalAlignment.Left
		};
		Button button = new Button
		{
			Content = SharedStrings.ButtonOk,
			Width = 90.0,
			IsDefault = true,
			IsCancel = true
		};
		stackPanel2.Children.Add(button);
		stackPanel.Children.Add(stackPanel2);
		dialog.Content = stackPanel;
		button.Click += delegate
		{
			dialog.DialogResult = true;
		};
		if (dialog.ShowDialog() == true)
		{
			return checkBox.IsChecked == true;
		}
		return false;
	}
}
