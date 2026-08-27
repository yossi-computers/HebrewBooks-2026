using System;
using System.Globalization;
using System.Windows.Data;

namespace HebrewBooks.UI.Converters;

public sealed class ClampedScaleConverter : IValueConverter
{
	private const double DefaultFloor = 0.92;

	public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		string[] array = (parameter as string)?.Split('|');
		if (value is double num && num > 0.0 && array != null && array.Length >= 1 && double.TryParse(array[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var result) && result > 0.0)
		{
			double result2;
			double num2 = ((array.Length > 1 && double.TryParse(array[1], NumberStyles.Any, CultureInfo.InvariantCulture, out result2)) ? result2 : 0.92);
			double num3 = num / result;
			return (num3 < num2) ? num2 : ((num3 > 1.0) ? 1.0 : num3);
		}
		return 1.0;
	}

	public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		throw new NotSupportedException();
	}
}
