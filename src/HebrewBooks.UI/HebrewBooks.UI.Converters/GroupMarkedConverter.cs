using System;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using HebrewBooks.Core.Catalog;
using HebrewBooks.UI.ViewModels;

namespace HebrewBooks.UI.Converters;

public sealed class GroupMarkedConverter : IMultiValueConverter
{
	public object? Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
	{
		if (values == null || values.Length < 2 || !(values[0] is GroupHeaderRow groupHeaderRow) || !(values[1] is LibraryViewModel libraryViewModel))
		{
			return false;
		}
		int num = 0;
		int num2 = 0;
		foreach (BookRow child in groupHeaderRow.Children)
		{
			if (!string.IsNullOrEmpty(child.FileID))
			{
				num++;
				if (libraryViewModel.IsMarked(child.FileID))
				{
					num2++;
				}
			}
		}
		if (num == 0 || num2 == 0)
		{
			return false;
		}
		return (num2 == num) ? new bool?(true) : ((bool?)null);
	}

	public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
	{
		return targetTypes.Select((Type _) => Binding.DoNothing).ToArray();
	}
}
