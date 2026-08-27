using System;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using HebrewBooks.UI.ViewModels;

namespace HebrewBooks.UI.Converters;

public sealed class FileIdMarkedConverter : IMultiValueConverter
{
	public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
	{
		if (values == null || values.Length < 2)
		{
			return false;
		}
		string fileId = values[0] as string;
		return values[1] is LibraryViewModel libraryViewModel && libraryViewModel.IsMarked(fileId);
	}

	public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
	{
		return targetTypes.Select((Type _) => Binding.DoNothing).ToArray();
	}
}
