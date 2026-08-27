using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace HebrewBooks.UI.Converters;

public sealed class StringNotEmptyToVisibility : IValueConverter
{
	public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		return string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;
	}

	public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		return Binding.DoNothing;
	}
}
