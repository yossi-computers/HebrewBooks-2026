using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace HebrewBooks.UI.Converters;

public sealed class InverseBooleanToVisibility : IValueConverter
{
	public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		bool flag = default(bool);
		int num;
		if (value is bool)
		{
			flag = (bool)value;
			num = 1;
		}
		else
		{
			num = 0;
		}
		return (((uint)num & (flag ? 1u : 0u)) != 0) ? Visibility.Collapsed : Visibility.Visible;
	}

	public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		return Binding.DoNothing;
	}
}
