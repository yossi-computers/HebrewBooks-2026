using System;
using System.Globalization;
using System.Windows.Data;
using HebrewBooks.UI.Resources;

namespace HebrewBooks.UI.Converters;

public sealed class SourceTypeLabelConverter : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
	{
		string text = (value as string) ?? string.Empty;
		switch (text)
		{
		default:
			if (text.Length != 0)
			{
				break;
			}
			return string.Empty;
		case "PDF":
			return "HebrewBooks";
		case "Text":
			return SharedStrings.S568;
		case "Personal":
			return SharedStrings.S55;
		case null:
			break;
		}
		return text;
	}

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
	{
		return Binding.DoNothing;
	}
}
