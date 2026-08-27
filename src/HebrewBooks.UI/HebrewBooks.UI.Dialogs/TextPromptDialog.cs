using System.Windows;
using System.Windows.Controls;
using HebrewBooks.UI.Resources;

namespace HebrewBooks.UI.Dialogs;

public static class TextPromptDialog
{
	public static string? Show(Window? owner, string title, string label, string initial = "", string? okText = null)
	{
		if (okText == null)
		{
			okText = SharedStrings.S572;
		}
		Window dialog = new Window
		{
			Title = title,
			Width = 360.0,
			SizeToContent = SizeToContent.Height,
			WindowStartupLocation = ((owner == null) ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner),
			Owner = owner,
			FlowDirection = FlowDirection.RightToLeft,
			ResizeMode = ResizeMode.NoResize,
			ShowInTaskbar = false
		};
		StackPanel stackPanel = new StackPanel
		{
			Margin = new Thickness(14.0)
		};
		stackPanel.Children.Add(new TextBlock
		{
			Text = label,
			Margin = new Thickness(0.0, 0.0, 0.0, 6.0),
			TextWrapping = TextWrapping.Wrap
		});
		TextBox input = new TextBox
		{
			Text = initial,
			Margin = new Thickness(0.0, 0.0, 0.0, 12.0)
		};
		stackPanel.Children.Add(input);
		StackPanel stackPanel2 = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			HorizontalAlignment = HorizontalAlignment.Left
		};
		Button button = new Button
		{
			Content = okText,
			Width = 84.0,
			IsDefault = true
		};
		Button element = new Button
		{
			Content = SharedStrings.S359,
			Width = 84.0,
			IsCancel = true,
			Margin = new Thickness(8.0, 0.0, 0.0, 0.0)
		};
		stackPanel2.Children.Add(button);
		stackPanel2.Children.Add(element);
		stackPanel.Children.Add(stackPanel2);
		dialog.Content = stackPanel;
		button.Click += delegate
		{
			dialog.DialogResult = true;
		};
		dialog.Loaded += delegate
		{
			input.Focus();
			input.SelectAll();
		};
		if (dialog.ShowDialog() != true)
		{
			return null;
		}
		string text = input.Text?.Trim();
		if (!string.IsNullOrEmpty(text))
		{
			return text;
		}
		return null;
	}
}
