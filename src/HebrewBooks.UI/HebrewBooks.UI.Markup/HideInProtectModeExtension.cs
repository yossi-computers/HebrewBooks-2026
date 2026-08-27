using System;
using System.Windows;
using System.Windows.Markup;

namespace HebrewBooks.UI.Markup;

[MarkupExtensionReturnType(typeof(Visibility))]
public sealed class HideInProtectModeExtension : MarkupExtension
{
	public bool Invert { get; set; }

	public override object ProvideValue(IServiceProvider serviceProvider)
	{
		return (App.IsProtectMode ^ Invert) ? Visibility.Collapsed : Visibility.Visible;
	}
}
