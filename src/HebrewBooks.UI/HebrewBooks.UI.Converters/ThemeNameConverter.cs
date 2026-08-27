using System;
using System.Globalization;
using System.Windows.Data;
using HebrewBooks.UI.Resources;
using HebrewBooks.UI.Services.Theming;

namespace HebrewBooks.UI.Converters;

public sealed class ThemeNameConverter : IValueConverter
{
	public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		if (value is string text)
		{
			if (text == "System")
			{
				return SharedStrings.S569;
			}
			Palette palette = PaletteRegistry.Find(text);
			if ((object)palette != null)
			{
				return palette.DisplayName;
			}
		}
		return value ?? string.Empty;
	}

	public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		return Binding.DoNothing;
	}
}
