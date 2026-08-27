using System.Windows;

namespace HebrewBooks.UI.Dialogs;

public static class HebrewMessageBox
{
	private const MessageBoxOptions RtlOptions = MessageBoxOptions.RightAlign | MessageBoxOptions.RtlReading;

	public static MessageBoxResult Show(string messageBoxText)
	{
		return MessageBox.Show(messageBoxText, string.Empty, MessageBoxButton.OK, MessageBoxImage.None, MessageBoxResult.OK, MessageBoxOptions.RightAlign | MessageBoxOptions.RtlReading);
	}

	public static MessageBoxResult Show(string messageBoxText, string caption)
	{
		return MessageBox.Show(messageBoxText, caption, MessageBoxButton.OK, MessageBoxImage.None, MessageBoxResult.OK, MessageBoxOptions.RightAlign | MessageBoxOptions.RtlReading);
	}

	public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button)
	{
		return MessageBox.Show(messageBoxText, caption, button, MessageBoxImage.None, DefaultFor(button), MessageBoxOptions.RightAlign | MessageBoxOptions.RtlReading);
	}

	public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon)
	{
		return MessageBox.Show(messageBoxText, caption, button, icon, DefaultFor(button), MessageBoxOptions.RightAlign | MessageBoxOptions.RtlReading);
	}

	public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon, MessageBoxResult defaultResult)
	{
		return MessageBox.Show(messageBoxText, caption, button, icon, defaultResult, MessageBoxOptions.RightAlign | MessageBoxOptions.RtlReading);
	}

	public static MessageBoxResult Show(Window owner, string messageBoxText)
	{
		return MessageBox.Show(owner, messageBoxText, string.Empty, MessageBoxButton.OK, MessageBoxImage.None, MessageBoxResult.OK, MessageBoxOptions.RightAlign | MessageBoxOptions.RtlReading);
	}

	public static MessageBoxResult Show(Window owner, string messageBoxText, string caption)
	{
		return MessageBox.Show(owner, messageBoxText, caption, MessageBoxButton.OK, MessageBoxImage.None, MessageBoxResult.OK, MessageBoxOptions.RightAlign | MessageBoxOptions.RtlReading);
	}

	public static MessageBoxResult Show(Window owner, string messageBoxText, string caption, MessageBoxButton button)
	{
		return MessageBox.Show(owner, messageBoxText, caption, button, MessageBoxImage.None, DefaultFor(button), MessageBoxOptions.RightAlign | MessageBoxOptions.RtlReading);
	}

	public static MessageBoxResult Show(Window owner, string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon)
	{
		return MessageBox.Show(owner, messageBoxText, caption, button, icon, DefaultFor(button), MessageBoxOptions.RightAlign | MessageBoxOptions.RtlReading);
	}

	private static MessageBoxResult DefaultFor(MessageBoxButton button)
	{
		return button switch
		{
			MessageBoxButton.OK => MessageBoxResult.OK, 
			MessageBoxButton.OKCancel => MessageBoxResult.OK, 
			MessageBoxButton.YesNo => MessageBoxResult.Yes, 
			MessageBoxButton.YesNoCancel => MessageBoxResult.Yes, 
			_ => MessageBoxResult.None, 
		};
	}
}
