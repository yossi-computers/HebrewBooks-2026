using System.Windows;
using System.Windows.Controls;
using HebrewBooks.UI.Resources;

namespace HebrewBooks.UI.Dialogs;

public static class ConfirmDownloadDialog
{
	public static DownloadPromptResult Show(Window? owner, string message, string caption, string? yesText = null, string? noText = null)
	{
		if (yesText == null)
		{
			yesText = SharedStrings.S570;
		}
		if (noText == null)
		{
			noText = SharedStrings.S1;
		}
		Window dialog = new Window
		{
			Title = caption,
			Width = 380.0,
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
			Content = SharedStrings.S571,
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
			Content = yesText,
			Width = 90.0,
			IsDefault = true
		};
		Button button2 = new Button
		{
			Content = noText,
			Width = 90.0,
			IsCancel = true,
			Margin = new Thickness(8.0, 0.0, 0.0, 0.0)
		};
		stackPanel2.Children.Add(button);
		stackPanel2.Children.Add(button2);
		stackPanel.Children.Add(stackPanel2);
		dialog.Content = stackPanel;
		button.Click += delegate
		{
			dialog.DialogResult = true;
		};
		button2.Click += delegate
		{
			dialog.DialogResult = false;
		};
		bool? flag = dialog.ShowDialog();
		bool valueOrDefault = flag == true;
		bool remember = flag.HasValue && checkBox.IsChecked == true;
		return new DownloadPromptResult(valueOrDefault, remember);
	}
}
