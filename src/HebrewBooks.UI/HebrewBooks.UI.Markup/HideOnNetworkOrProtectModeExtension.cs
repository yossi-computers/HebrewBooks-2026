using System;
using System.Windows;
using System.Windows.Markup;

namespace HebrewBooks.UI.Markup;

[MarkupExtensionReturnType(typeof(Visibility))]
public sealed class HideOnNetworkOrProtectModeExtension : MarkupExtension
{
	public override object ProvideValue(IServiceProvider serviceProvider)
	{
		return (App.IsProtectMode || App.IsNetworkInstall) ? Visibility.Collapsed : Visibility.Visible;
	}
}
