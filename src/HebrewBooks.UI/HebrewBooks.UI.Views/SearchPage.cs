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
using System.Windows.Media;
using HebrewBooks.Core.Abstractions;
using HebrewBooks.Core.Models;
using HebrewBooks.UI.Behaviors;
using HebrewBooks.UI.Controls;
using HebrewBooks.UI.Navigation;
using HebrewBooks.UI.Resources;
using HebrewBooks.UI.Services;
using HebrewBooks.UI.ViewModels;
using Serilog;
using Wpf.Ui.Controls;

namespace HebrewBooks.UI.Views;

public partial class SearchPage : Page, IShortcutTarget
{
	private SearchViewModel? _vm;

	private IPathResolver? _paths;

	private string? _lastOpenedPath;

	private bool _initialized;

	private ChromeAutoHideController? _chrome;

	private bool _prewarmKicked;

	private GridLength _savedResultsWidth = new GridLength(1.0, GridUnitType.Star);

	private double _savedResultsMinWidth = 320.0;




























	public SearchPage()
	{
		InitializeComponent();
		base.Loaded += OnLoaded;
	}

	public void EnsureViewerPrewarmed()
	{
		if (!_prewarmKicked)
		{
			_prewarmKicked = true;
			if (_paths == null)
			{
				_paths = (IPathResolver)App.Services.GetService(typeof(IPathResolver));
			}
			PdfView.PrewarmAsync(_paths);
		}
	}

	private async void OnLoaded(object sender, EventArgs e)
	{
		_vm?.RefreshSearchOptionsFromDisk();
		if (_initialized)
		{
			return;
		}
		_initialized = true;
		_vm = (SearchViewModel)App.Services.GetService(typeof(SearchViewModel));
		_paths = (IPathResolver)App.Services.GetService(typeof(IPathResolver));
		base.DataContext = _vm;
		_vm.PropertyChanged += OnVmChanged;
		ApplyTabletSizing();
		App.TabletModeChanged += delegate
		{
			base.Dispatcher.Invoke(ApplyTabletSizing);
		};
		PdfView.TocQuickAddRequested += async delegate
		{
			await DoQuickAddTocAsync();
		};
		PdfView.TocEditRequested += async delegate
		{
			await DoOpenTocEditorAsync();
		};
		PdfView.VerifiedHitPagesReceived += delegate(object? _, IReadOnlyList<int> pages)
		{
			base.Dispatcher.BeginInvoke((Action)delegate
			{
				_vm?.ApplyVerifiedHitPages(pages);
			});
		};
		PdfView.FuzzyFinalPagesReceived += delegate(object? _, IReadOnlyList<int> pages)
		{
			base.Dispatcher.BeginInvoke((Action)delegate
			{
				_vm?.ApplyFuzzyFinalPages(pages);
			});
		};
		PdfView.HighlightProgressChanged += delegate(object? _, int n)
		{
			base.Dispatcher.BeginInvoke((Action)delegate
			{
				_vm?.ApplyHighlightProgress(n);
			});
		};
		PdfView.CurrentPageChanged += delegate(object? _, int p)
		{
			base.Dispatcher.BeginInvoke((Action)delegate
			{
				_vm?.ReportViewerPage(p);
			});
		};
		_vm.ScrollToPageRequested += async delegate(int p)
		{
			if (_vm.IsTextMode || string.IsNullOrEmpty(_lastOpenedPath))
			{
				return;
			}
			try
			{
				await PdfView.GoToPageAsync(p);
			}
			catch (Exception ex)
			{
				_vm.InBookSearchStatus = SharedStrings.S2380 + ex.Message;
			}
		};
		_vm.Main.PropertyChanged += OnMainPropertyChanged;
		PdfView.ImmersiveToggleRequested += delegate
		{
			base.Dispatcher.BeginInvoke((Action)delegate
			{
				_vm?.Main.ToggleImmersiveCommand.Execute(null);
			});
		};
		PdfView.ImmersiveExitRequested += delegate
		{
			base.Dispatcher.BeginInvoke((Action)delegate
			{
				if (_vm != null)
				{
					_vm.Main.ImmersiveReading = false;
				}
			});
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
		base.PreviewMouseDown += OnMouseSideButton;
		ApplyImmersiveLayout(_vm.Main.ImmersiveReading);
		_chrome = new ChromeAutoHideController(_vm.Main, _vm, () => _vm.CanSearchInBook, InBookChromeBar, PdfView, PinChromeBtn, PinChromeIcon, InBookBox);
		RegionCopyBtn.Click += async delegate
		{
			await PdfView.StartRegionCopyAsync();
		};
		RegionCopyTextBtn.Click += async delegate
		{
			await PdfView.StartRegionCopyTextAsync();
		};
		EnsureViewerPrewarmed();
		await _vm.EnsureCategoriesLoadedAsync();
		void ApplyTabletSizing()
		{
			ResultsGrid.RowHeight = (App.IsTabletMode ? 44 : 32);
		}
	}

	private void OnShortcutKeyDown(object sender, KeyEventArgs e)
	{
		if (e.Handled)
		{
			return;
		}
		if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.None && (Keyboard.Modifiers & (ModifierKeys.Alt | ModifierKeys.Shift)) == 0)
		{
			switch ((e.Key == Key.System) ? e.SystemKey : e.Key)
			{
			case Key.Oem4:
				TryGoBack();
				e.Handled = true;
				return;
			case Key.Oem6:
				TryGoForward();
				e.Handled = true;
				return;
			}
		}
		bool focusInTextBox = Keyboard.FocusedElement is TextBoxBase;
		ShortcutAction? shortcutAction = ShortcutKeyMap.FromKey(e, focusInTextBox);
		bool flag;
		if (shortcutAction.HasValue)
		{
			ShortcutAction valueOrDefault = shortcutAction.GetValueOrDefault();
			if ((uint)(valueOrDefault - 6) > 1u)
			{
				flag = false;
				goto IL_0098;
			}
		}
		flag = true;
		goto IL_0098;
		IL_0098:
		if (!flag)
		{
			HandleShortcut(shortcutAction.Value);
			e.Handled = true;
		}
	}

	private void OnMouseSideButton(object sender, MouseButtonEventArgs e)
	{
		if (e.ChangedButton == MouseButton.XButton1)
		{
			TryGoBack();
			e.Handled = true;
		}
		else if (e.ChangedButton == MouseButton.XButton2)
		{
			TryGoForward();
			e.Handled = true;
		}
	}

	private void TryGoBack()
	{
		SearchViewModel? vm = _vm;
		if (vm != null && vm.GoBackCommand.CanExecute(null))
		{
			_vm.GoBackCommand.Execute(null);
		}
	}

	private void TryGoForward()
	{
		SearchViewModel? vm = _vm;
		if (vm != null && vm.GoForwardCommand.CanExecute(null))
		{
			_vm.GoForwardCommand.Execute(null);
		}
	}

	public void HandleShortcut(ShortcutAction action)
	{
		if (_vm == null)
		{
			return;
		}
		switch (action)
		{
		case ShortcutAction.NextResult:
			if (_vm.IsTextMode)
			{
				PdfView.NextTextHitAsync();
			}
			else
			{
				_vm.NextMatchCommand.Execute(null);
			}
			break;
		case ShortcutAction.PrevResult:
			if (_vm.IsTextMode)
			{
				PdfView.PrevTextHitAsync();
			}
			else
			{
				_vm.PrevMatchCommand.Execute(null);
			}
			break;
		case ShortcutAction.NextBook:
			_vm.NextResultCommand.Execute(null);
			break;
		case ShortcutAction.PrevBook:
			_vm.PrevResultCommand.Execute(null);
			break;
		case ShortcutAction.FocusInBookSearch:
			if (_vm.CanSearchInBook)
			{
				_chrome?.Reveal();
				InBookBox.Focus();
				InBookBox.SelectAll();
			}
			break;
		case ShortcutAction.FocusMainSearch:
			QueryBox.Focus();
			QueryBox.SelectAll();
			break;
		case ShortcutAction.GoToContentSearch:
		case ShortcutAction.GoToCatalog:
			(Window.GetWindow(this) as MainWindow)?.NavigateToSection(action);
			break;
		case ShortcutAction.NavBack:
			TryGoBack();
			break;
		case ShortcutAction.NavForward:
			TryGoForward();
			break;
		}
	}

	private async void OnVmChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (_vm == null || _paths == null)
		{
			return;
		}
		try
		{
			switch (e.PropertyName)
			{
			case "IsBookLoading":
				if (_vm.IsBookLoading)
				{
					PdfView.ShowLoading(show: true);
					PdfPlaceholder.Visibility = Visibility.Collapsed;
				}
				break;
			case "PdfPath":
			case "MarkedPdfPath":
			case "CurrentTextRelativePath":
				await OpenSelectedAsync();
				break;
			case "CurrentBookHits":
				if (!_vm.IsTextMode && string.IsNullOrEmpty(_vm.MarkedPdfPath) && !string.IsNullOrEmpty(_lastOpenedPath))
				{
					await PdfView.SetHighlightXmlAsync(_vm.CurrentBookHits?.HighlightXml, _vm.CurrentBookHits?.MatchedTerms);
				}
				break;
			case "PdfPage":
				if (!_vm.IsTextMode && !string.IsNullOrEmpty(_lastOpenedPath))
				{
					await PdfView.GoToPageAsync(_vm.PdfPage);
				}
				break;
			}
		}
		catch (Exception ex)
		{
			_vm.StatusText = SharedStrings.S2381 + ex.Message;
		}
	}

	private async Task OpenSelectedAsync()
	{
		if (_vm == null || _paths == null)
		{
			return;
		}
		if (_vm.IsTextMode && !string.IsNullOrEmpty(_vm.CurrentTextRelativePath))
		{
			string rel = _vm.CurrentTextRelativePath;
			string absText = _paths.OtzrayaTextPath(rel);
			if (!(absText == _lastOpenedPath))
			{
				PdfView.ShowLoading(show: true);
				PdfPlaceholder.Visibility = Visibility.Collapsed;
				await PdfView.OpenTextAsync(_paths, rel, _vm.SelectedRow?.Book.BookName ?? "", _vm.CurrentTextTerms);
				if (!(_vm.CurrentTextRelativePath != rel))
				{
					_lastOpenedPath = absText;
				}
			}
			return;
		}
		if (string.IsNullOrEmpty(_vm.PdfPath))
		{
			PdfPlaceholder.Visibility = Visibility.Visible;
			_lastOpenedPath = null;
			return;
		}
		bool flag = !string.IsNullOrEmpty(_vm.MarkedPdfPath);
		string pathToOpen = (flag ? _vm.MarkedPdfPath : _vm.PdfPath);
		if (pathToOpen == _lastOpenedPath)
		{
			return;
		}
		PdfView.ShowLoading(show: true);
		PdfPlaceholder.Visibility = Visibility.Collapsed;
		Stopwatch pdfSw = Stopwatch.StartNew();
		Log.Information("TIMING: SearchPage.PdfView.OpenAsync start page={Page} useMarked={UseMarked}", _vm.PdfPage, flag);
		await PdfView.OpenAsync(_paths, pathToOpen, _vm.PdfPage, flag ? null : _vm.CurrentBookHits?.HighlightXml, flag ? null : _vm.CurrentBookHits?.MatchedTerms);
		Log.Information("TIMING: SearchPage.PdfView.OpenAsync returned after {Elapsed}ms (JS HB_loadPdf has been kicked off; first-page render still in flight)", pdfSw.ElapsedMilliseconds);
		_lastOpenedPath = pathToOpen;
		Book book = _vm.SelectedRow?.Book;
		if ((object)book == null)
		{
			return;
		}
		try
		{
			IReadOnlyList<TocEntry> bookTocAsync = await ((ICatalogRepository)App.Services.GetService(typeof(ICatalogRepository))).GetTocAsync(book.ID);
			await PdfView.SetBookTocAsync(bookTocAsync);
		}
		catch
		{
		}
	}

	private async Task DoOpenTocEditorAsync()
	{
		Book book = _vm?.SelectedRow?.Book;
		if ((object)book != null && !_vm.IsTextMode)
		{
			TocEditorWindow obj = (TocEditorWindow)App.Services.GetService(typeof(TocEditorWindow));
			obj.Owner = Window.GetWindow(this);
			if (await obj.EditAsync(book.ID, book.BookName ?? "", book.FileID, book.SourceType) == true)
			{
				IReadOnlyList<TocEntry> bookTocAsync = await ((ICatalogRepository)App.Services.GetService(typeof(ICatalogRepository))).GetTocAsync(book.ID);
				await PdfView.SetBookTocAsync(bookTocAsync);
			}
		}
	}

	private async Task DoQuickAddTocAsync()
	{
		Book book = _vm?.SelectedRow?.Book;
		if ((object)book == null || _vm.IsTextMode)
		{
			return;
		}
		int page = await PdfView.GetCurrentPageAsync();
		if (page <= 0)
		{
			page = 1;
		}
		TocQuickAddDialog dialog = new TocQuickAddDialog(page)
		{
			Owner = Window.GetWindow(this)
		};
		if (dialog.ShowDialog() == true)
		{
			ICatalogRepository catalogRepo = (ICatalogRepository)App.Services.GetService(typeof(ICatalogRepository));
			IReadOnlyList<TocEntry> readOnlyList = await catalogRepo.GetTocAsync(book.ID);
			List<TocEntry> combined = new List<TocEntry>(readOnlyList.Count + 1);
			combined.AddRange(readOnlyList);
			combined.Add(new TocEntry(dialog.EnteredTitle, page));
			await catalogRepo.SetTocAsync(book.ID, combined);
			await PdfView.SetBookTocAsync(combined);
			if (_vm != null)
			{
				_vm.StatusText = $"{SharedStrings.S2382}{page}";
			}
		}
	}

	private void OnNewWindowClick(object sender, RoutedEventArgs e)
	{
		new SearchWindow().Show();
	}

	private void OnOpenExternalClick(object sender, RoutedEventArgs e)
	{
		if (_vm == null)
		{
			return;
		}
		if (App.IsProtectMode)
		{
			_vm.InBookSearchStatus = SharedStrings.S1026;
			return;
		}
		string text = ((!_vm.IsTextMode && !string.IsNullOrEmpty(_vm.PdfPath)) ? _vm.PdfPath : _lastOpenedPath);
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
			_vm.InBookSearchStatus = SharedStrings.S2383 + ex.Message;
		}
	}

	private void OnToggleImmersiveClick(object sender, RoutedEventArgs e)
	{
		_vm?.Main.ToggleImmersiveCommand.Execute(null);
	}

	private void OnHitStripScrollClick(object sender, RoutedEventArgs e)
	{
		if (sender is FrameworkElement { Tag: string tag } && double.TryParse(tag, out var result))
		{
			ListBoxScrollIntoView.ScrollHorizontally(HitStripList, result * 120.0);
		}
	}

	private void OnMainPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == "ImmersiveReading" && _vm != null)
		{
			ApplyImmersiveLayout(_vm.Main.ImmersiveReading);
		}
	}

	private void ApplyImmersiveLayout(bool immersive)
	{
		if (immersive)
		{
			_savedResultsWidth = ResultsCol.Width;
			_savedResultsMinWidth = ResultsCol.MinWidth;
			ResultsCol.MinWidth = 0.0;
			ResultsCol.Width = new GridLength(0.0);
			SplitterCol.Width = new GridLength(0.0);
			ResultsPanel.Visibility = Visibility.Collapsed;
			SearchSplitter.Visibility = Visibility.Collapsed;
			SearchCard.Visibility = Visibility.Collapsed;
		}
		else
		{
			ResultsCol.MinWidth = _savedResultsMinWidth;
			ResultsCol.Width = _savedResultsWidth;
			SplitterCol.Width = new GridLength(6.0);
			ResultsPanel.Visibility = Visibility.Visible;
			SearchSplitter.Visibility = Visibility.Visible;
			SearchCard.Visibility = Visibility.Visible;
		}
		ImmersiveIcon.Symbol = (immersive ? SymbolRegular.FullScreenMinimize24 : SymbolRegular.FullScreenMaximize24);
		ImmersiveBtn.ToolTip = (immersive ? SharedStrings.S1033 : SharedStrings.S291);
	}

	private async void OnHistoryItemClick(object sender, MouseButtonEventArgs e)
	{
		if (e.OriginalSource is DependencyObject dependencyObject && FindAncestor<ListBoxItem>(dependencyObject)?.Content is string parameter && _vm != null)
		{
			HistoryPopup.IsOpen = false;
			await _vm.UseHistoryEntryCommand.ExecuteAsync(parameter);
		}
	}

	private void OnClearHistoryClick(object sender, RoutedEventArgs e)
	{
		HistoryPopup.IsOpen = false;
	}

	private async void OnResultsDoubleClick(object sender, MouseButtonEventArgs e)
	{
		if (!(e.OriginalSource is DependencyObject dependencyObject) || FindAncestor<DataGridRow>(dependencyObject) == null || (object)_vm?.SelectedRow == null || _paths == null)
		{
			return;
		}
		Book book = _vm.SelectedRow.Book;
		PdfViewerWindow viewer = (PdfViewerWindow)App.Services.GetService(typeof(PdfViewerWindow));
		viewer.Show();
		int result;
		if (string.Equals(book.SourceType, "Text", StringComparison.Ordinal))
		{
			IReadOnlyList<string> terms = ((_vm.SelectedRow.HitCount > 0 && (object)_vm.CurrentBookHits != null) ? _vm.CurrentBookHits.MatchedTerms : null);
			await viewer.OpenTextAsync(book, _vm.QueryText, terms);
		}
		else if (string.Equals(book.SourceType, "Personal", StringComparison.Ordinal))
		{
			string text = ((!string.IsNullOrEmpty(book.RelativePath)) ? book.RelativePath : book.FileID);
			if (!string.IsNullOrEmpty(text))
			{
				string text2 = _paths.PersonalFilePath(text);
				if (!File.Exists(text2))
				{
					_vm.StatusText = SharedStrings.S2384 + text2;
				}
				else
				{
					await viewer.OpenAsync(book.FileID ?? text, book.BookName ?? "", text2, _vm.QueryText, string.IsNullOrEmpty(_vm.ActiveQueryText) ? _vm.QueryText : _vm.ActiveQueryText);
				}
			}
		}
		else if (!string.IsNullOrEmpty(book.FileID) && int.TryParse(book.FileID, out result))
		{
			string path = _paths.PdfPath(result, book.Folder);
			if (!(await ((OnDemandBookService)App.Services.GetService(typeof(OnDemandBookService))).EnsureLocalAsync(book, viewer)))
			{
				_vm.StatusText = SharedStrings.S2385 + path;
			}
			else
			{
				await viewer.OpenAsync(book.FileID, book.BookName ?? "", path, _vm.QueryText, string.IsNullOrEmpty(_vm.ActiveQueryText) ? _vm.QueryText : _vm.ActiveQueryText);
			}
		}
	}

	private static T? FindAncestor<T>(DependencyObject? from) where T : DependencyObject
	{
		while (from != null && !(from is T))
		{
			from = VisualTreeHelper.GetParent(from);
		}
		return from as T;
	}



}
