using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using HebrewBooks.UI.Services;
using HebrewBooks.UI.ViewModels;
using Wpf.Ui.Controls;

namespace HebrewBooks.UI.Views;

public partial class TocEditorWindow : FluentWindow
{
	private readonly TocEditorViewModel _vm;



	public TocEditorWindow(TocEditorViewModel vm)
	{
		InitializeComponent();
		this.ClampToWorkArea();
		_vm = vm;
		base.DataContext = vm;
		vm.Saved += delegate
		{
			base.DialogResult = true;
			Close();
		};
	}

	public async Task<bool?> EditAsync(int bookId, string bookTitle, string? fileId = null, string? sourceType = null)
	{
		await _vm.LoadAsync(bookId, bookTitle, fileId, sourceType);
		return ShowDialog();
	}

	private void OnCancel(object sender, RoutedEventArgs e)
	{
		base.DialogResult = false;
		Close();
	}

	private void OnGridKeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key == Key.Tab && !(Keyboard.FocusedElement is System.Windows.Controls.TextBox) && _vm.SelectedEntry != null)
		{
			if ((Keyboard.Modifiers & ModifierKeys.Shift) != ModifierKeys.None)
			{
				_vm.OutdentCommand.Execute(null);
			}
			else
			{
				_vm.IndentCommand.Execute(null);
			}
			e.Handled = true;
		}
	}


}
