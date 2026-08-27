using System;
using System.Globalization;
using System.Windows.Data;
using HebrewBooks.Infrastructure.Settings;
using HebrewBooks.UI.Resources;

namespace HebrewBooks.UI.Converters;

public sealed class MissingBookActionNameConverter : IValueConverter
{
	public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		if (value is MissingBookAction)
		{
			switch ((MissingBookAction)value)
			{
			case MissingBookAction.Ask:
				return SharedStrings.S565;
			case MissingBookAction.AlwaysDownload:
				return SharedStrings.S566;
			case MissingBookAction.NeverDownload:
				return SharedStrings.S567;
			}
		}
		return value ?? string.Empty;
	}

	public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		return Binding.DoNothing;
	}
}
