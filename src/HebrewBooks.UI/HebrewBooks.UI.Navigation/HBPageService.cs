using System;
using System.Windows;
using Wpf.Ui;

namespace HebrewBooks.UI.Navigation;

internal sealed class HBPageService(IServiceProvider services) : IPageService
{
	public T? GetPage<T>() where T : class
	{
		return services.GetService(typeof(T)) as T;
	}

	public FrameworkElement? GetPage(Type pageType)
	{
		return services.GetService(pageType) as FrameworkElement;
	}
}
