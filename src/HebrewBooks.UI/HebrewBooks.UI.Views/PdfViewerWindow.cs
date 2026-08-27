using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using HebrewBooks.Core.Abstractions;
using HebrewBooks.Core.Models;
using HebrewBooks.UI.Behaviors;
using HebrewBooks.UI.Controls;
using HebrewBooks.UI.Dialogs;
using HebrewBooks.UI.Navigation;
using HebrewBooks.UI.Resources;
using HebrewBooks.UI.Services;
using HebrewBooks.UI.ViewModels;
using Wpf.Ui.Controls;

namespace HebrewBooks.UI.Views;

public partial class PdfViewerWindow : FluentWindow, IShortcutTarget
{
	private readonly IPathResolver _paths;

	private readonly PdfViewerViewModel _vm;

	private readonly ICatalogRepository _catalog;

	private string? _lastOpenedPath;

	private ChromeAutoHideController? _chrome;

	private int _currentBookCatalogId;










	public PdfViewerWindow(PdfViewerViewModel vm, IPathResolver paths, ICatalogRepository catalog)
	{
		_vm = vm;
		_paths = paths;
		_catalog = catalog;
		InitializeComponent();
		this.OpenAtScaledSize(1200.0, 850.0);
		base.DataContext = _vm;
		_vm.PropertyChanged += OnVmChanged;
		if (App.IsProtectMode)
		{
			base.ResizeMode = ResizeMode.NoResize;
			base.WindowState = WindowState.Maximized;
		}
		PdfView.TocQuickAddRequested += async delegate
		{
			await OnQuickAddTocAsync();
		};
		PdfView.TocEditRequested += async delegate
		{
			await OnOpenTocEditorAsync();
		};
		PdfView.VerifiedHitPagesReceived += delegate(object? _, IReadOnlyList<int> pages)
		{
			base.Dispatcher.BeginInvoke((Action)delegate
			{
				_vm.ApplyVerifiedHitPages(pages);
			});
		};
		PdfView.FuzzyFinalPagesReceived += delegate(object? _, IReadOnlyList<int> pages)
		{
			base.Dispatcher.BeginInvoke((Action)delegate
			{
				_vm.ApplyFuzzyFinalPages(pages);
			});
		};
		PdfView.HighlightProgressChanged += delegate(object? _, int n)
		{
			base.Dispatcher.BeginInvoke((Action)delegate
			{
				_vm.ApplyHighlightProgress(n);
			});
		};
		PdfView.CurrentPageChanged += delegate(object? _, int p)
		{
			base.Dispatcher.BeginInvoke((Action)delegate
			{
				_vm.ReportViewerPage(p);
			});
		};
		_vm.ScrollToPageRequested += async delegate(int p)
		{
			if (string.IsNullOrEmpty(_lastOpenedPath))
			{
				return;
			}
			try
			{
				await PdfView.GoToPageAsync(p);
			}
			catch (Exception ex)
			{
				_vm.InBookSearchStatus = SharedStrings.S2367 + ex.Message;
			}
		};
		PdfView.ShortcutRequested += delegate(object? _, string t)
		{
			base.Dispatcher.BeginInvoke((Action)delegate
			{
				ShortcutAction? shortcutAction = ShortcutKeyMap.FromViewerToken(t);
				if (shortcutAction.HasValue)
				{
					HandleShortcut(shortcutAction.Value);
				}
			});
		};
		base.PreviewKeyDown += OnShortcutKeyDown;
		MainViewModel main = (MainViewModel)App.Services.GetService(typeof(MainViewModel));
		_chrome = new ChromeAutoHideController(main, _vm, () => !_vm.IsTextMode, InBookChromeBar, PdfView, PinChromeBtn, PinChromeIcon, InBookBox);
		base.Closed += delegate
		{
			_chrome?.Detach();
			try
			{
				App.DisposeWebView2In(this);
			}
			catch
			{
			}
		};
		RegionCopyBtn.Click += async delegate
		{
			await PdfView.StartRegionCopyAsync();
		};
		RegionCopyTextBtn.Click += async delegate
		{
			await PdfView.StartRegionCopyTextAsync();
		};
		PdfView.ShowLoading(show: true);
	}

	private void OnShortcutKeyDown(object sender, KeyEventArgs e)
	{
		if (e.Handled)
		{
			return;
		}
		if (e.Key == Key.F9 && App.Services.GetService(typeof(MainViewModel)) is MainViewModel mainViewModel)
		{
			mainViewModel.ChromeAutoHide = !mainViewModel.ChromeAutoHide;
			e.Handled = true;
			return;
		}
		bool focusInTextBox = Keyboard.FocusedElement is TextBoxBase;
		ShortcutAction? shortcutAction = ShortcutKeyMap.FromKey(e, focusInTextBox);
		if (shortcutAction.HasValue)
		{
			HandleShortcut(shortcutAction.Value);
			e.Handled = true;
		}
	}

	public void HandleShortcut(ShortcutAction action)
	{
		switch (action)
		{
		case ShortcutAction.NextResult:
			_vm.NextHitCommand.Execute(null);
			break;
		case ShortcutAction.PrevResult:
			_vm.PrevHitCommand.Execute(null);
			break;
		case ShortcutAction.FocusInBookSearch:
			_chrome?.Reveal();
			InBookBox.Focus();
			InBookBox.SelectAll();
			break;
		case ShortcutAction.NextBook:
		case ShortcutAction.PrevBook:
			break;
		}
	}

	public async Task OpenAsync(string fileId, string title, string pdfPath, string? displayQuery = null, string? searchQuery = null, int page = 0)
	{
		if (!File.Exists(pdfPath))
		{
			HebrewMessageBox.Show(this, SharedStrings.S2368 + pdfPath, SharedStrings.S558, System.Windows.MessageBoxButton.OK, MessageBoxImage.Hand);
			Close();
			return;
		}
		PdfView.ShowLoading(show: true);
		_vm.LoadBasic(fileId, title, pdfPath, displayQuery, searchQuery);
		await _vm.StartInitialSearchAsync();
		if (page > 0)
		{
			_vm.CurrentPage = page;
		}
		await OpenOrRefreshAsync();
		await LoadTocIntoViewerAsync();
	}

	private async Task LoadTocIntoViewerAsync()
	{
		_ = 2;
		try
		{
			if (string.IsNullOrEmpty(_vm.CurrentFileId))
			{
				_currentBookCatalogId = 0;
				return;
			}
			Book book = await _catalog.GetByFileIdAsync(_vm.CurrentFileId);
			if ((object)book == null)
			{
				_currentBookCatalogId = 0;
				return;
			}
			_currentBookCatalogId = book.ID;
			IReadOnlyList<TocEntry> bookTocAsync = await _catalog.GetTocAsync(book.ID);
			await PdfView.SetBookTocAsync(bookTocAsync);
		}
		catch
		{
		}
	}

	private async Task OnOpenTocEditorAsync()
	{
		if (_currentBookCatalogId != 0)
		{
			TocEditorWindow obj = (TocEditorWindow)App.Services.GetService(typeof(TocEditorWindow));
			obj.Owner = this;
			if (await obj.EditAsync(_currentBookCatalogId, _vm.BookTitle, _vm.CurrentFileId) == true)
			{
				await LoadTocIntoViewerAsync();
			}
		}
	}

	private async Task OnQuickAddTocAsync()
	{
		if (_currentBookCatalogId != 0)
		{
			int page = await PdfView.GetCurrentPageAsync();
			if (page <= 0)
			{
				page = 1;
			}
			TocQuickAddDialog prompt = new TocQuickAddDialog(page)
			{
				Owner = this
			};
			if (prompt.ShowDialog() == true)
			{
				IReadOnlyList<TocEntry> readOnlyList = await _catalog.GetTocAsync(_currentBookCatalogId);
				List<TocEntry> combined = new List<TocEntry>(readOnlyList.Count + 1);
				combined.AddRange(readOnlyList);
				combined.Add(new TocEntry(prompt.EnteredTitle, page));
				await _catalog.SetTocAsync(_currentBookCatalogId, combined);
				await PdfView.SetBookTocAsync(combined);
			}
		}
	}

	public async Task OpenTextAsync(Book book, string? displayQuery = null, IReadOnlyList<string>? terms = null)
	{
		if (string.IsNullOrEmpty(book.RelativePath))
		{
			HebrewMessageBox.Show(this, SharedStrings.S1051, SharedStrings.S558, System.Windows.MessageBoxButton.OK, MessageBoxImage.Hand);
			Close();
			return;
		}
		string absolute = _paths.OtzrayaTextPath(book.RelativePath);
		if (!File.Exists(absolute))
		{
			HebrewMessageBox.Show(this, SharedStrings.S2369 + absolute, SharedStrings.S558, System.Windows.MessageBoxButton.OK, MessageBoxImage.Hand);
			Close();
			return;
		}
		PdfView.ShowLoading(show: true);
		_vm.LoadTextBook(book.FileID ?? book.RelativePath, book.RelativePath, book.BookName ?? "", displayQuery, terms);
		await PdfView.OpenTextAsync(_paths, book.RelativePath, book.BookName ?? "", _vm.TextHighlightTerms);
		_lastOpenedPath = absolute;
	}

	private async void OnVmChanged(object? sender, PropertyChangedEventArgs e)
	{
		_ = 2;
		try
		{
			switch (e.PropertyName)
			{
			case "MarkedPdfPath":
				if (!string.IsNullOrEmpty(_vm.CurrentPdfPath) && _lastOpenedPath != null)
				{
					await OpenOrRefreshAsync();
				}
				break;
			case "CurrentBookHits":
				if (_lastOpenedPath != null && _lastOpenedPath == _vm.CurrentPdfPath)
				{
					await PdfView.SetHighlightXmlAsync(_vm.CurrentBookHits?.HighlightXml, _vm.CurrentBookHits?.MatchedTerms);
				}
				break;
			case "CurrentPage":
				if (!string.IsNullOrEmpty(_lastOpenedPath))
				{
					await PdfView.GoToPageAsync(_vm.CurrentPage);
				}
				break;
			}
		}
		catch (Exception ex)
		{
			_vm.InBookSearchStatus = SharedStrings.S2370 + ex.Message;
		}
	}

	private async Task OpenOrRefreshAsync()
	{
		if (!string.IsNullOrEmpty(_vm.CurrentPdfPath))
		{
			bool flag = !string.IsNullOrEmpty(_vm.MarkedPdfPath);
			string pathToOpen = (flag ? _vm.MarkedPdfPath : _vm.CurrentPdfPath);
			await PdfView.OpenAsync(_paths, pathToOpen, _vm.CurrentPage, flag ? null : _vm.CurrentBookHits?.HighlightXml, flag ? null : _vm.CurrentBookHits?.MatchedTerms);
			_lastOpenedPath = pathToOpen;
		}
	}

	private void OnHitStripScrollClick(object sender, RoutedEventArgs e)
	{
		if (sender is FrameworkElement { Tag: string tag } && double.TryParse(tag, out var result))
		{
			ListBoxScrollIntoView.ScrollHorizontally(HitStripList, result * 120.0);
		}
	}

	private void OnOpenExternalClick(object sender, RoutedEventArgs e)
	{
		if (App.IsProtectMode)
		{
			_vm.InBookSearchStatus = SharedStrings.S1026;
			return;
		}
		string text = ((!string.IsNullOrEmpty(_vm.CurrentPdfPath)) ? _vm.CurrentPdfPath : _lastOpenedPath);
		if (string.IsNullOrEmpty(text) || !File.Exists(text))
		{
			_vm.InBookSearchStatus = SharedStrings.S1027;
			return;
		}
		try
		{
			Process.Start(new ProcessStartInfo
			{
				FileName = text,
				UseShellExecute = true
			});
		}
		catch (Exception ex)
		{
			_vm.InBookSearchStatus = SharedStrings.S2371 + ex.Message;
		}
	}



}
