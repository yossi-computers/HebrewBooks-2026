using System;
using System.Globalization;
using System.Windows.Data;
using Wpf.Ui.Controls;

namespace HebrewBooks.UI.Converters;

public sealed class SourceTypeIconConverter : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
	{
		return ((value as string) ?? string.Empty) switch
		{
			"PDF" => SymbolRegular.DocumentPdf24, 
			"Text" => SymbolRegular.BookOpen24, 
			"Personal" => SymbolRegular.PersonAvailable24, 
			_ => SymbolRegular.Question24, 
		};
	}

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
	{
		return Binding.DoNothing;
	}
}
