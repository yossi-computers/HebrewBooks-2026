using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using HebrewBooks.UI.ViewModels;
using Wpf.Ui.Controls;

namespace HebrewBooks.UI.Controls;

public partial class TopicFilterControl : UserControl
{




	private TopicFilterViewModel? Vm => base.DataContext as TopicFilterViewModel;

	public TopicFilterControl()
	{
		InitializeComponent();
	}

	private void OnPopupOpened(object? sender, EventArgs e)
	{
		SearchBox.Focus();
		Keyboard.Focus(SearchBox);
		PopupZOrderHelper.BringToFront(sender as Popup);
	}

	private void OnSearchBoxKeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key == Key.Escape)
		{
			TopicToggle.IsChecked = false;
			e.Handled = true;
		}
	}

	private void OnSuggestionPreviewMouseDown(object sender, MouseButtonEventArgs e)
	{
		TopicFilterViewModel vm = Vm;
		if (vm != null && sender is ItemsControl itemsControl)
		{
			DependencyObject element = e.OriginalSource as DependencyObject;
			if ((itemsControl.ContainerFromElement(element) as ListBoxItem)?.DataContext is string parameter)
			{
				vm.AddCommand.Execute(parameter);
				SearchBox.Focus();
				Keyboard.Focus(SearchBox);
				e.Handled = true;
			}
		}
	}



}
