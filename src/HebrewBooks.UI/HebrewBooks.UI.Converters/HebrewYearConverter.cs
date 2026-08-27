using System;
using System.Globalization;
using System.Windows.Data;
using HebrewBooks.Core.Catalog;

namespace HebrewBooks.UI.Converters;

public sealed class HebrewYearConverter : IValueConverter
{
	public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		if (value == null)
		{
			return string.Empty;
		}
		string text = value.ToString();
		if (string.IsNullOrWhiteSpace(text))
		{
			return string.Empty;
		}
		text = text.Trim();
		if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
		{
			return text;
		}
		if (result < 4500 || result > 6500)
		{
			return text;
		}
		return HebrewYear.ToGematria(result);
	}

	public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		return Binding.DoNothing;
	}
}
