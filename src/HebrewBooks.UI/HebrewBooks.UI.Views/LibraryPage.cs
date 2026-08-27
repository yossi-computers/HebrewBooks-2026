using System;
using System.CodeDom.Compiler;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Messaging;
using HebrewBooks.Core.Abstractions;
using HebrewBooks.Core.Catalog;
using HebrewBooks.Core.Models;
using HebrewBooks.Infrastructure.OS;
using HebrewBooks.Services.Search;
using HebrewBooks.Services.TextLayer;
using HebrewBooks.UI.Behaviors;
using HebrewBooks.UI.Controls;
using HebrewBooks.UI.Dialogs;
using HebrewBooks.UI.Messages;
using HebrewBooks.UI.Navigation;
using HebrewBooks.UI.Resources;
using HebrewBooks.UI.Services;
using HebrewBooks.UI.ViewModels;
using Serilog;
using Wpf.Ui.Controls;

namespace HebrewBooks.UI.Views;

public partial class LibraryPage : Page, IShortcutTarget, IStyleConnector
{
	private sealed partial class TabInsertionAdorner : Adorner
	{
		private readonly Pen _pen;

		private readonly Brush _brush;

		public bool RightEdge { get; }

		public TabInsertionAdorner(UIElement adorned, bool rightEdge, Brush brush)
			: base(adorned)
		{
			RightEdge = rightEdge;
			base.IsHitTestVisible = false;
			_brush = brush;
			_pen = new Pen(brush, 2.5);
			_pen.Freeze();
		}

		protected override void OnRender(DrawingContext dc)
		{
			double actualWidth = ((FrameworkElement)base.AdornedElement).ActualWidth;
			double actualHeight = ((FrameworkElement)base.AdornedElement).ActualHeight;
			double x = (RightEdge ? actualWidth : 0.0);
			dc.DrawLine(_pen, new Point(x, 1.0), new Point(x, actualHeight - 1.0));
			dc.DrawEllipse(_brush, null, new Point(x, 1.0), 2.5, 2.5);
			dc.DrawEllipse(_brush, null, new Point(x, actualHeight - 1.0), 2.5, 2.5);
		}
	}

	private LibraryViewModel? _vm;

	private IPathResolver? _paths;

	private bool _suppressColumnSync;

	private bool _initialized;

	private bool _isSessionOwner;

	private ChromeAutoHideController? _chrome;

	private Dictionary<string, string>? _shelfBookNames;

	private readonly Dictionary<OpenBookTab, PdfJsHost> _tabHosts = new Dictionary<OpenBookTab, PdfJsHost>();

	private readonly Dictionary<PdfJsHost, string?> _hostLoadedPath = new Dictionary<PdfJsHost, string>();

	private PdfJsHost? _activeHost;

	private static bool _sessionPersistenceClaimed;

	private static readonly List<LibraryPage> _livePages = new List<LibraryPage>();

	private static DispatcherTimer? _sessionSaveTimer;

	private const int LongEscMs = 600;

	private DispatcherTimer? _longEscTimer;

	private bool _longEscFired;

	private const int ResetHoldMs = 350;

	private int _emptyDeleteSince;

	private int _emptyDeleteCount;

	private Point _tabDragStart;

	private OpenBookTab? _tabDragItem;

	private static LibraryPage? _dragSourcePage;

	private static bool _dragDroppedOnStrip;

	private static bool _dragImportedElsewhere;

	private int _tabTouchId = -1;

	private OpenBookTab? _tabTouchItem;

	private Point _tabTouchStart;

	private bool _tabTouchDragging;

	private FrameworkElement? _tabTouchBorder;

	private UIElement? _tabGhost;

	private static Adorner? _tabInsertAdorner;

	private static AdornerLayer? _tabInsertLayer;

	private static RepairBookOcrWindow? _repairWindow;

	private const double NarrowThreshold = 720.0;

	private bool _narrow;

	private bool _narrowPreferCatalog;

	private const double HeaderTapSlop = 12.0;

	private GroupHeaderRow? _pendingHeaderTap;

	private Point _headerTapStart;




































	private string? _lastOpenedPath
	{
		get
		{
			if (_activeHost == null || !_hostLoadedPath.TryGetValue(_activeHost, out string value))
			{
				return null;
			}
			return value;
		}
		set
		{
			if (_activeHost != null)
			{
				_hostLoadedPath[_activeHost] = value;
			}
		}
	}

	private static void ScheduleSessionSave()
	{
		if (App.IsProtectMode)
		{
			return;
		}
		Application current = Application.Current;
		if (current == null)
		{
			return;
		}
		if (_sessionSaveTimer == null)
		{
			_sessionSaveTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(300.0), DispatcherPriority.Background, delegate
			{
				_sessionSaveTimer.Stop();
				SaveSessionNow();
			}, current.Dispatcher);
		}
		_sessionSaveTimer.Stop();
		_sessionSaveTimer.Start();
	}

	private static void SaveSessionNow()
	{
		if (App.IsProtectMode)
		{
			return;
		}
		List<OpenTabsPersistence.SavedTabs> list = new List<OpenTabsPersistence.SavedTabs>();
		foreach (LibraryPage item in _livePages.OrderByDescending((LibraryPage p) => p._isSessionOwner))
		{
			OpenTabsPersistence.SavedTabs savedTabs = item._vm?.SnapshotOpenTabs();
			if ((object)savedTabs != null)
			{
				IReadOnlyList<OpenTabsPersistence.SavedTab> tabs = savedTabs.Tabs;
				if (tabs != null && tabs.Count > 0)
				{
					list.Add(savedTabs);
				}
			}
		}
		if (list.Count > 0)
		{
			OpenTabsPersistence.SaveSession(new OpenTabsPersistence.SavedSession(list));
		}
	}

	public LibraryPage()
	{
		InitializeComponent();
		base.Loaded += OnLoaded;
		OuterGrid.SizeChanged += delegate
		{
			ApplyResponsiveLayout();
		};
	}

	private async void OnLoaded(object sender, RoutedEventArgs e)
	{
		_vm?.RefreshSearchOptionsFromDisk();
		if (_vm != null)
		{
			string pendingExternalSearch = App.PendingExternalSearch;
			if (pendingExternalSearch != null)
			{
				App.PendingExternalSearch = null;
				_vm.RunSearchFromExternal(pendingExternalSearch);
			}
		}
		if (_initialized)
		{
			return;
		}
		_initialized = true;
		_vm = (LibraryViewModel)App.Services.GetService(typeof(LibraryViewModel));
		_paths = (IPathResolver)App.Services.GetService(typeof(IPathResolver));
		base.DataContext = _vm;
		_vm.PropertyChanged += OnVmChanged;
		_livePages.Add(this);
		Window window = Window.GetWindow(this);
		if (window != null)
		{
			window.Closed += delegate
			{
				_livePages.Remove(this);
			};
		}
		_vm.OpenTabs.CollectionChanged += delegate
		{
			ScheduleSessionSave();
		};
		if (App.PendingWindowTabs.Count > 0)
		{
			OpenTabsPersistence.SavedTabs pendingTearOff = App.PendingWindowTabs.Dequeue();
			_vm.SetPendingTearOff(pendingTearOff);
			_vm.RestoreTornOffTab();
		}
		ApplyTabletSizing();
		App.TabletModeChanged += delegate
		{
			base.Dispatcher.Invoke(ApplyTabletSizing);
		};
		if (!_sessionPersistenceClaimed)
		{
			_sessionPersistenceClaimed = true;
			_isSessionOwner = true;
			App.MainLibraryViewModel = _vm;
			string filePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HebrewBooks", "session.json");
			_vm.EnableSessionPersistence(filePath);
			Window window2 = Window.GetWindow(this);
			_vm.SpawnRestoreWindows = delegate(IReadOnlyList<OpenTabsPersistence.SavedTabs> windows)
			{
				foreach (OpenTabsPersistence.SavedTabs window3 in windows)
				{
					App.PendingWindowTabs.Enqueue(window3);
					new SearchWindow().Show();
				}
			};
			_vm.RestoreOpenTabs();
			if (window2 != null && !App.IsProtectMode)
			{
				window2.Closing += OnAppClosingSaveTabs;
			}
		}
		_vm.OpenTabs.CollectionChanged += OnOpenTabsChanged;
		_vm.ScrollToPageRequested += async delegate(int p)
		{
			if (_vm.IsTextMode || _activeHost == null || string.IsNullOrEmpty(_lastOpenedPath))
			{
				return;
			}
			try
			{
				await _activeHost.GoToPageAsync(p);
			}
			catch (Exception ex)
			{
				_vm.StatusText = SharedStrings.S2344 + ex.Message;
			}
		};
		_vm.Main.PropertyChanged += OnMainPropertyChanged;
		base.PreviewKeyDown += OnShortcutKeyDown;
		base.PreviewMouseDown += OnMouseSideButton;
		CatalogFilterBox.PreviewKeyDown += OnSearchBoxKeyDown;
		CatalogFilterBox.PreviewKeyUp += OnSearchBoxKeyUp;
		ApplyImmersiveLayout(_vm.Main.ImmersiveReading);
		RegionCopyBtn.Click += async delegate
		{
			if (_activeHost != null)
			{
				await _activeHost.StartRegionCopyAsync();
			}
		};
		RegionCopyTextBtn.Click += async delegate
		{
			if (_activeHost != null)
			{
				await _activeHost.StartRegionCopyTextAsync();
			}
		};
		ApplyResponsiveLayout();
		Splitter.DragCompleted += OnSplitterDragCompleted;
		CatalogFilterBox.GotKeyboardFocus += delegate
		{
			if (_vm != null)
			{
				_vm.ChipsScrollDismissed = false;
			}
			if (_narrow && !_narrowPreferCatalog)
			{
				_narrowPreferCatalog = true;
				ApplyResponsiveLayout();
			}
		};
		CatalogGrid.PreviewMouseWheel += delegate
		{
			LibraryViewModel vm = _vm;
			if (vm != null && !vm.ChipsScrollDismissed)
			{
				_vm.ChipsScrollDismissed = true;
			}
		};
		CatalogFilterBox.PreviewMouseLeftButtonDown += delegate
		{
			if (_vm != null)
			{
				_vm.ChipsScrollDismissed = false;
			}
		};
		_vm.CatalogScrollToTopRequested += delegate
		{
			base.Dispatcher.BeginInvoke(DispatcherPriority.Background, (Action)delegate
			{
				FindDescendant<ScrollViewer>(CatalogGrid)?.ScrollToTop();
			});
		};
		await _vm.LoadCommand.ExecuteAsync(null);
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
		LibraryViewModel? vm = _vm;
		if (vm != null && vm.GoBackCommand.CanExecute(null))
		{
			_vm.GoBackCommand.Execute(null);
		}
	}

	private void TryGoForward()
	{
		LibraryViewModel? vm = _vm;
		if (vm != null && vm.GoForwardCommand.CanExecute(null))
		{
			_vm.GoForwardCommand.Execute(null);
		}
	}

	private void OnSearchBoxKeyDown(object sender, KeyEventArgs e)
	{
		Key key = e.Key;
		if ((key == Key.Back || key == Key.Delete) ? true : false)
		{
			HandleHeldDeleteReset(sender as System.Windows.Controls.TextBox, e);
			return;
		}
		if (e.Key == Key.Return)
		{
			(sender as System.Windows.Controls.TextBox)?.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
		}
		_emptyDeleteSince = 0;
		_emptyDeleteCount = 0;
		if (e.Key != Key.Escape || e.IsRepeat)
		{
			return;
		}
		_longEscFired = false;
		_longEscTimer?.Stop();
		_longEscTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromMilliseconds(600.0)
		};
		_longEscTimer.Tick += delegate
		{
			_longEscTimer?.Stop();
			_longEscFired = true;
			if (_vm != null)
			{
				if (_vm.IsContentMode)
				{
					_vm.ExitContentModeCommand.Execute(null);
				}
				else
				{
					_vm.FilterText = string.Empty;
				}
				_vm.ClearNavigationHistoryCommand.Execute(null);
			}
		};
		_longEscTimer.Start();
	}

	private void OnSearchBoxKeyUp(object sender, KeyEventArgs e)
	{
		if (e.Key != Key.Escape)
		{
			return;
		}
		_longEscTimer?.Stop();
		if (_longEscFired)
		{
			_longEscFired = false;
		}
		else if (_vm != null)
		{
			if (_vm.IsContentMode)
			{
				_vm.ExitContentModeCommand.Execute(null);
			}
			else
			{
				_vm.FilterText = string.Empty;
			}
		}
	}

	private void HandleHeldDeleteReset(System.Windows.Controls.TextBox? box, KeyEventArgs e)
	{
		if (box == null || !string.IsNullOrEmpty(box.Text) || _vm == null || !_vm.IsContentMode)
		{
			_emptyDeleteSince = 0;
			_emptyDeleteCount = 0;
			return;
		}
		int tickCount = Environment.TickCount;
		if (_emptyDeleteSince == 0)
		{
			_emptyDeleteSince = tickCount;
			_emptyDeleteCount = 1;
			return;
		}
		_emptyDeleteCount++;
		if (_emptyDeleteCount >= 2 && tickCount - _emptyDeleteSince >= 350)
		{
			_emptyDeleteSince = 0;
			_emptyDeleteCount = 0;
			_vm.ExitContentModeCommand.Execute(null);
			e.Handled = true;
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
				if (_activeHost != null)
				{
					_activeHost.NextTextHitAsync();
				}
			}
			else
			{
				_vm.NextHitCommand.Execute(null);
			}
			break;
		case ShortcutAction.PrevResult:
			if (_vm.IsTextMode)
			{
				if (_activeHost != null)
				{
					_activeHost.PrevTextHitAsync();
				}
			}
			else
			{
				_vm.PrevHitCommand.Execute(null);
			}
			break;
		case ShortcutAction.NextBook:
			_vm.NextBookCommand.Execute(null);
			ScrollSelectedBookIntoView();
			break;
		case ShortcutAction.PrevBook:
			_vm.PrevBookCommand.Execute(null);
			ScrollSelectedBookIntoView();
			break;
		case ShortcutAction.FocusInBookSearch:
			if (_vm.ShowInBookChrome)
			{
				_chrome?.Reveal();
				InBookBox.Focus();
				InBookBox.SelectAll();
			}
			break;
		case ShortcutAction.FocusMainSearch:
			CatalogFilterBox.Focus();
			CatalogFilterBox.SelectAll();
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
		case ShortcutAction.NextTab:
			_vm.ActivateAdjacentTab(1);
			break;
		case ShortcutAction.PrevTab:
			_vm.ActivateAdjacentTab(-1);
			break;
		case ShortcutAction.CloseActiveTab:
			_vm.CloseActiveTab();
			break;
		}
	}

	private void ScrollSelectedBookIntoView()
	{
		if (_vm == null)
		{
			return;
		}
		if (_vm.IsContentMode)
		{
			if ((object)_vm.SelectedRow != null)
			{
				ResultsGrid.ScrollIntoView(_vm.SelectedRow);
			}
		}
		else if (_vm.SelectedCatalogRow != null)
		{
			CatalogGrid.ScrollIntoView(_vm.SelectedCatalogRow);
		}
	}

	private static T? FindDescendant<T>(DependencyObject? root) where T : DependencyObject
	{
		if (root == null)
		{
			return null;
		}
		int childrenCount = VisualTreeHelper.GetChildrenCount(root);
		for (int i = 0; i < childrenCount; i++)
		{
			DependencyObject child = VisualTreeHelper.GetChild(root, i);
			if (child is T result)
			{
				return result;
			}
			T val = FindDescendant<T>(child);
			if (val != null)
			{
				return val;
			}
		}
		return null;
	}

	private void OnCatalogGridKeyDown(object sender, KeyEventArgs e)
	{
		if (_vm == null || Keyboard.Modifiers != ModifierKeys.None)
		{
			return;
		}
		switch (e.Key)
		{
		case Key.Down:
			_vm.StepCatalogRow(1);
			ScrollSelectedBookIntoView();
			e.Handled = true;
			break;
		case Key.Up:
			_vm.StepCatalogRow(-1);
			ScrollSelectedBookIntoView();
			e.Handled = true;
			break;
		case Key.Left:
			if (_vm.SelectedCatalogRow is GroupHeaderRow)
			{
				_vm.SetSelectedGroupExpanded(expanded: true);
				e.Handled = true;
			}
			break;
		case Key.Right:
			if (_vm.SelectedCatalogRow is GroupHeaderRow)
			{
				_vm.SetSelectedGroupExpanded(expanded: false);
				e.Handled = true;
			}
			break;
		}
	}

	private void OnToggleRowDetails(object sender, RoutedEventArgs e)
	{
		DataGridRow dataGridRow = FindAncestor<DataGridRow>(sender as DependencyObject);
		if (dataGridRow != null)
		{
			dataGridRow.DetailsVisibility = ((dataGridRow.DetailsVisibility == Visibility.Visible) ? Visibility.Collapsed : Visibility.Visible);
			e.Handled = true;
		}
	}

	private void OnCatalogRightButtonUp(object sender, MouseButtonEventArgs e)
	{
		if (App.IsProtectMode)
		{
			e.Handled = true;
			return;
		}
		DependencyObject dependencyObject = e.OriginalSource as DependencyObject;
		DataGridRow dataGridRow = null;
		while (dependencyObject != null)
		{
			if (dependencyObject is DataGridRow dataGridRow2)
			{
				dataGridRow = dataGridRow2;
				break;
			}
			dependencyObject = VisualTreeHelper.GetParent(dependencyObject);
		}
		if (!(dataGridRow?.Item is BookRow bookRow))
		{
			return;
		}
		Book book = bookRow.Book;
		if (_vm != null && CatalogGrid.SelectedItem != dataGridRow.Item)
		{
			_vm.SuppressNextOpen = true;
			dataGridRow.IsSelected = true;
			CatalogGrid.SelectedItem = dataGridRow.Item;
		}
		ContextMenu contextMenu = new ContextMenu();
		System.Windows.Controls.MenuItem menuItem = new System.Windows.Controls.MenuItem
		{
			Header = SharedStrings.S993,
			Icon = new SymbolIcon
			{
				Symbol = SymbolRegular.Edit24
			}
		};
		menuItem.Click += delegate
		{
			LibraryViewModel? vm = _vm;
			if (vm != null && vm.EditSelectedCommand.CanExecute(null))
			{
				_vm.EditSelectedCommand.Execute(null);
			}
		};
		contextMenu.Items.Add(menuItem);
		if (_vm != null && !string.IsNullOrWhiteSpace(book.AuthorName))
		{
			string author = book.AuthorName;
			System.Windows.Controls.MenuItem menuItem2 = new System.Windows.Controls.MenuItem
			{
				Header = SharedStrings.S994,
				Icon = new SymbolIcon
				{
					Symbol = SymbolRegular.Person24
				}
			};
			menuItem2.Click += delegate
			{
				if (_vm != null)
				{
					_vm.FilterText = author;
				}
			};
			contextMenu.Items.Add(menuItem2);
		}
		System.Windows.Controls.MenuItem menuItem3 = new System.Windows.Controls.MenuItem
		{
			Header = SharedStrings.S995,
			Icon = new SymbolIcon
			{
				Symbol = SymbolRegular.ArrowSync24
			}
		};
		menuItem3.Click += delegate
		{
			LibraryViewModel? vm = _vm;
			if (vm != null && vm.RefreshCommand.CanExecute(null))
			{
				_vm.RefreshCommand.Execute(null);
			}
		};
		contextMenu.Items.Add(menuItem3);
		contextMenu.Items.Add(new Separator());
		ISearchScopeContext searchScopeContext = (ISearchScopeContext)App.Services.GetService(typeof(ISearchScopeContext));
		IReadOnlyCollection<string> markedIds = searchScopeContext.MarkedFileIds;
		if (!App.IsNetworkInstall)
		{
			System.Windows.Controls.MenuItem menuItem4 = new System.Windows.Controls.MenuItem
			{
				Header = SharedStrings.S996,
				Icon = new SymbolIcon
				{
					Symbol = SymbolRegular.Delete24,
					Foreground = Brushes.IndianRed
				}
			};
			menuItem4.Click += delegate
			{
				OpenDeleteBookDialog(new Book[1] { book });
			};
			contextMenu.Items.Add(menuItem4);
			if (markedIds != null && markedIds.Count > 1 && _vm != null)
			{
				List<Book> markedBooks = _vm.AllBooks.Where((Book b) => !string.IsNullOrEmpty(b.FileID) && markedIds.Contains<string>(b.FileID)).ToList();
				if (markedBooks.Count > 1)
				{
					contextMenu.Items.Add(new Separator());
					System.Windows.Controls.MenuItem menuItem5 = new System.Windows.Controls.MenuItem
					{
						Header = $"{SharedStrings.S2345}{markedBooks.Count}{SharedStrings.S2346}",
						Icon = new SymbolIcon
						{
							Symbol = SymbolRegular.Delete24,
							Foreground = Brushes.IndianRed
						}
					};
					menuItem5.Click += delegate
					{
						OpenDeleteBookDialog(markedBooks);
					};
					contextMenu.Items.Add(menuItem5);
				}
			}
		}
		TextLayerService textLayerService = App.Services.GetService(typeof(TextLayerService)) as TextLayerService;
		if (WinOcrCommand.IsEngineInstalled && textLayerService != null && textLayerService.IsEligible(book))
		{
			if (contextMenu.Items.Count > 0)
			{
				contextMenu.Items.Add(new Separator());
			}
			System.Windows.Controls.MenuItem menuItem6 = new System.Windows.Controls.MenuItem
			{
				Header = SharedStrings.S998,
				Icon = new SymbolIcon
				{
					Symbol = SymbolRegular.TextGrammarWand24
				}
			};
			menuItem6.Click += delegate
			{
				StartTextLayerRepair(book);
			};
			contextMenu.Items.Add(menuItem6);
		}
		List<ShelfTreeNode> list = ((!App.IsProtectMode && _vm != null) ? _vm.ShelfTree.Where((ShelfTreeNode n) => !n.IsPublisher && n.Kind == ShelfNodeKind.Shelf).ToList() : new List<ShelfTreeNode>());
		if (list.Count > 0 && book.ID > 0 && !string.IsNullOrEmpty(book.FileID))
		{
			if (contextMenu.Items.Count > 0)
			{
				contextMenu.Items.Add(new Separator());
			}
			System.Windows.Controls.MenuItem menuItem7 = new System.Windows.Controls.MenuItem
			{
				Header = SharedStrings.S999,
				Icon = new SymbolIcon
				{
					Symbol = SymbolRegular.Add24
				}
			};
			MirrorSubmenuChevron(menuItem7);
			BuildAddToShelfItems(menuItem7.Items, list, new Book[1] { book }, book.FileID);
			contextMenu.Items.Add(menuItem7);
			if (markedIds != null && markedIds.Count > 1 && _vm != null)
			{
				List<Book> list2 = _vm.AllBooks.Where((Book b) => b.ID > 0 && !string.IsNullOrEmpty(b.FileID) && markedIds.Contains<string>(b.FileID)).ToList();
				if (list2.Count > 1)
				{
					System.Windows.Controls.MenuItem menuItem8 = new System.Windows.Controls.MenuItem
					{
						Header = $"{SharedStrings.S2347}{list2.Count}{SharedStrings.S2348}",
						Icon = new SymbolIcon
						{
							Symbol = SymbolRegular.Add24
						}
					};
					MirrorSubmenuChevron(menuItem8);
					BuildAddToShelfItems(menuItem8.Items, list, list2, null);
					contextMenu.Items.Add(menuItem8);
				}
			}
		}
		if (contextMenu.Items.Count == 0)
		{
			e.Handled = true;
			return;
		}
		contextMenu.PlacementTarget = dataGridRow;
		contextMenu.IsOpen = true;
		e.Handled = true;
	}

	private void OnTabRightButtonUp(object sender, MouseButtonEventArgs e)
	{
		if (_vm == null)
		{
			return;
		}
		object obj = (sender as FrameworkElement)?.DataContext;
		OpenBookTab tab = obj as OpenBookTab;
		if (tab == null)
		{
			return;
		}
		int num = _vm.OpenTabs.IndexOf(tab);
		if (num < 0)
		{
			return;
		}
		int count = _vm.OpenTabs.Count;
		ContextMenu contextMenu = new ContextMenu();
		System.Windows.Controls.MenuItem menuItem = new System.Windows.Controls.MenuItem
		{
			Header = SharedStrings.S1001,
			Icon = new SymbolIcon
			{
				Symbol = SymbolRegular.Dismiss24
			}
		};
		menuItem.Click += delegate
		{
			_vm?.CloseTabCommand.Execute(tab);
		};
		contextMenu.Items.Add(menuItem);
		if (!App.IsProtectMode)
		{
			System.Windows.Controls.MenuItem menuItem2 = new System.Windows.Controls.MenuItem
			{
				Header = SharedStrings.S1002,
				Icon = new SymbolIcon
				{
					Symbol = SymbolRegular.WindowNew24
				}
			};
			menuItem2.Click += delegate
			{
				DetachTabToNewWindow(tab);
			};
			contextMenu.Items.Add(menuItem2);
		}
		if (count > 1)
		{
			System.Windows.Controls.MenuItem menuItem3 = new System.Windows.Controls.MenuItem
			{
				Header = SharedStrings.S1003
			};
			menuItem3.Click += delegate
			{
				_vm?.CloseOtherTabsCommand.Execute(tab);
			};
			contextMenu.Items.Add(menuItem3);
			System.Windows.Controls.MenuItem menuItem4 = new System.Windows.Controls.MenuItem
			{
				Header = SharedStrings.S1004,
				IsEnabled = (num > 0)
			};
			menuItem4.Click += delegate
			{
				_vm?.CloseTabsToRightCommand.Execute(tab);
			};
			contextMenu.Items.Add(menuItem4);
		}
		contextMenu.Items.Add(new Separator());
		System.Windows.Controls.MenuItem menuItem5 = new System.Windows.Controls.MenuItem
		{
			Header = SharedStrings.S1005,
			Icon = new SymbolIcon
			{
				Symbol = SymbolRegular.DismissCircle24
			}
		};
		menuItem5.Click += delegate
		{
			_vm?.CloseAllTabsCommand.Execute(null);
		};
		contextMenu.Items.Add(menuItem5);
		contextMenu.PlacementTarget = sender as UIElement;
		contextMenu.IsOpen = true;
		e.Handled = true;
	}

	private async void DetachTabToNewWindow(OpenBookTab tab)
	{
		if (_vm == null || App.IsProtectMode || string.IsNullOrEmpty(tab.FileId))
		{
			return;
		}
		int? livePage = null;
		if (tab == _vm.ActiveTab && _activeHost != null && !_vm.IsTextMode)
		{
			try
			{
				int num = await _activeHost.GetCurrentPageAsync();
				if (num > 0)
				{
					livePage = num;
				}
			}
			catch (Exception exception)
			{
				Log.Warning(exception, "DetachTabToNewWindow: GetCurrentPageAsync failed");
			}
		}
		App.PendingWindowTabs.Enqueue(_vm.BuildTearOffPayload(tab, livePage));
		new SearchWindow().Show();
		_vm.CloseTabCommand.Execute(tab);
	}

	private void OnTabPreviewLeftDown(object sender, MouseButtonEventArgs e)
	{
		_tabDragStart = e.GetPosition(null);
		_tabDragItem = (sender as FrameworkElement)?.DataContext as OpenBookTab;
	}

	private void OnTabPreviewMove(object sender, MouseEventArgs e)
	{
		if (e.LeftButton != MouseButtonState.Pressed || _tabDragItem == null)
		{
			return;
		}
		if (e.StylusDevice != null)
		{
			_tabDragItem = null;
			return;
		}
		Point position = e.GetPosition(null);
		if (Math.Abs(position.X - _tabDragStart.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(position.Y - _tabDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
		{
			return;
		}
		OpenBookTab tabDragItem = _tabDragItem;
		_tabDragItem = null;
		if (_vm == null || !_vm.OpenTabs.Contains(tabDragItem))
		{
			return;
		}
		_dragSourcePage = this;
		_dragDroppedOnStrip = false;
		_dragImportedElsewhere = false;
		GhostDraggedTab(tabDragItem, on: true);
		try
		{
			DragDrop.DoDragDrop((DependencyObject)sender, tabDragItem, DragDropEffects.Move);
		}
		catch
		{
		}
		finally
		{
			RemoveTabInsertionAdorner();
			GhostDraggedTab(tabDragItem, on: false);
		}
		if (_dragImportedElsewhere)
		{
			_vm.CloseTabCommand.Execute(tabDragItem);
		}
		else if (!_dragDroppedOnStrip && !App.IsProtectMode)
		{
			DetachTabToNewWindow(tabDragItem);
		}
		_dragSourcePage = null;
	}

	private void OnTabPreviewTouchDown(object sender, TouchEventArgs e)
	{
		_tabTouchBorder = sender as FrameworkElement;
		_tabTouchItem = _tabTouchBorder?.DataContext as OpenBookTab;
		_tabTouchStart = e.GetTouchPoint(this).Position;
		_tabTouchId = e.TouchDevice.Id;
		_tabTouchDragging = false;
	}

	private void OnTabPreviewTouchMove(object sender, TouchEventArgs e)
	{
		if (_tabTouchItem == null || e.TouchDevice.Id != _tabTouchId || _vm == null)
		{
			return;
		}
		Point position = e.GetTouchPoint(this).Position;
		if (!_tabTouchDragging)
		{
			if (Math.Abs(position.X - _tabTouchStart.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(position.Y - _tabTouchStart.Y) < SystemParameters.MinimumVerticalDragDistance)
			{
				return;
			}
			if (!_vm.OpenTabs.Contains(_tabTouchItem))
			{
				_tabTouchItem = null;
				return;
			}
			_tabTouchDragging = true;
			_tabTouchBorder?.CaptureTouch(e.TouchDevice);
			GhostDraggedTab(_tabTouchItem, on: true);
		}
		if (!TouchPointOverStrip(position))
		{
			RemoveTabInsertionAdorner();
		}
		else
		{
			ComputeTouchInsertIndex(position, out FrameworkElement container, out bool rightHalf);
			if (container != null)
			{
				ShowTabInsertionAdorner(container, rightHalf);
			}
		}
		e.Handled = true;
	}

	private void OnTabPreviewTouchUp(object sender, TouchEventArgs e)
	{
		if (e.TouchDevice.Id != _tabTouchId)
		{
			return;
		}
		OpenBookTab tabTouchItem = _tabTouchItem;
		bool tabTouchDragging = _tabTouchDragging;
		try
		{
			if (!tabTouchDragging || tabTouchItem == null || _vm == null)
			{
				return;
			}
			e.Handled = true;
			Point position = e.GetTouchPoint(this).Position;
			if (!TouchPointOverStrip(position))
			{
				if (!App.IsProtectMode)
				{
					DetachTabToNewWindow(tabTouchItem);
				}
				return;
			}
			int num = _vm.OpenTabs.IndexOf(tabTouchItem);
			if (num >= 0)
			{
				FrameworkElement container;
				bool rightHalf;
				int num2 = ComputeTouchInsertIndex(position, out container, out rightHalf);
				if (num < num2)
				{
					num2--;
				}
				_vm.MoveTab(num, Math.Clamp(num2, 0, _vm.OpenTabs.Count - 1));
			}
		}
		finally
		{
			if (_tabTouchBorder != null && tabTouchDragging)
			{
				try
				{
					_tabTouchBorder.ReleaseTouchCapture(e.TouchDevice);
				}
				catch
				{
				}
			}
			RemoveTabInsertionAdorner();
			if (tabTouchItem != null)
			{
				GhostDraggedTab(tabTouchItem, on: false);
			}
			_tabTouchItem = null;
			_tabTouchBorder = null;
			_tabTouchId = -1;
			_tabTouchDragging = false;
		}
	}

	private bool TouchPointOverStrip(Point p)
	{
		return BookTabStrip.TransformToVisual(this).TransformBounds(new Rect(new Point(0.0, 0.0), BookTabStrip.RenderSize)).Contains(p);
	}

	private int ComputeTouchInsertIndex(Point p, out FrameworkElement? container, out bool rightHalf)
	{
		container = null;
		rightHalf = false;
		if (_vm == null || _vm.OpenTabs.Count == 0)
		{
			return 0;
		}
		for (int i = 0; i < _vm.OpenTabs.Count; i++)
		{
			if (!(TabItems.ItemContainerGenerator.ContainerFromItem(_vm.OpenTabs[i]) is FrameworkElement frameworkElement))
			{
				continue;
			}
			Rect rect = frameworkElement.TransformToVisual(this).TransformBounds(new Rect(new Point(0.0, 0.0), frameworkElement.RenderSize));
			if (p.X >= rect.Left && p.X <= rect.Right)
			{
				container = frameworkElement;
				rightHalf = p.X > rect.Left + rect.Width / 2.0;
				if (!rightHalf)
				{
					return i + 1;
				}
				return i;
			}
		}
		ItemContainerGenerator itemContainerGenerator = TabItems.ItemContainerGenerator;
		ObservableCollection<OpenBookTab> openTabs = _vm.OpenTabs;
		container = itemContainerGenerator.ContainerFromItem(openTabs[openTabs.Count - 1]) as FrameworkElement;
		rightHalf = false;
		return _vm.OpenTabs.Count;
	}

	private void GhostDraggedTab(OpenBookTab tab, bool on)
	{
		if (on)
		{
			_tabGhost = TabItems.ItemContainerGenerator.ContainerFromItem(tab) as UIElement;
			if (_tabGhost != null)
			{
				_tabGhost.Opacity = 0.35;
			}
		}
		else
		{
			if (_tabGhost != null)
			{
				_tabGhost.Opacity = 1.0;
			}
			_tabGhost = null;
		}
	}

	private void OnTabDragOver(object sender, DragEventArgs e)
	{
		bool dataPresent = e.Data.GetDataPresent(typeof(OpenBookTab));
		e.Effects = (dataPresent ? DragDropEffects.Move : DragDropEffects.None);
		e.Handled = true;
		if (!dataPresent || !(sender is FrameworkElement frameworkElement))
		{
			RemoveTabInsertionAdorner();
			return;
		}
		bool rightEdge = e.GetPosition(frameworkElement).X > frameworkElement.ActualWidth / 2.0;
		ShowTabInsertionAdorner(frameworkElement, rightEdge);
	}

	private void OnTabDrop(object sender, DragEventArgs e)
	{
		RemoveTabInsertionAdorner();
		_dragDroppedOnStrip = true;
		if (_vm == null || !(e.Data.GetData(typeof(OpenBookTab)) is OpenBookTab openBookTab) || !(sender is FrameworkElement { DataContext: OpenBookTab dataContext } frameworkElement))
		{
			return;
		}
		int num = _vm.OpenTabs.IndexOf(dataContext);
		if (num < 0)
		{
			return;
		}
		int num2 = ((e.GetPosition(frameworkElement).X > frameworkElement.ActualWidth / 2.0) ? num : (num + 1));
		e.Handled = true;
		int num3 = _vm.OpenTabs.IndexOf(openBookTab);
		if (num3 >= 0)
		{
			if (num3 < num2)
			{
				num2--;
			}
			_vm.MoveTab(num3, Math.Clamp(num2, 0, _vm.OpenTabs.Count - 1));
		}
		else
		{
			ImportForeignTab(openBookTab, num2);
		}
	}

	private void ImportForeignTab(OpenBookTab dragged, int insertIndex)
	{
		if (_vm != null && !App.IsProtectMode && !string.IsNullOrEmpty(dragged.FileId) && _vm.CanImportBook(dragged.FileId))
		{
			int page = dragged.LastPage;
			string query = dragged.InBookQuery;
			LibraryViewModel libraryViewModel = _dragSourcePage?._vm;
			if (libraryViewModel != null)
			{
				OpenTabsPersistence.SavedTab savedTab = libraryViewModel.BuildTearOffPayload(dragged).Tabs[0];
				page = savedTab.Page;
				query = savedTab.Query ?? string.Empty;
			}
			_dragImportedElsewhere = true;
			_vm.ImportTabAsync(dragged.FileId, page, query, !dragged.IsPreview, insertIndex);
		}
	}

	private void OnPageDragOver(object sender, DragEventArgs e)
	{
		if (e.Data.GetDataPresent(typeof(OpenBookTab)))
		{
			e.Effects = DragDropEffects.Move;
			e.Handled = true;
		}
	}

	private void OnPageDrop(object sender, DragEventArgs e)
	{
		if (_vm != null && e.Data.GetData(typeof(OpenBookTab)) is OpenBookTab openBookTab)
		{
			_dragDroppedOnStrip = true;
			e.Handled = true;
			if (!_vm.OpenTabs.Contains(openBookTab))
			{
				ImportForeignTab(openBookTab, _vm.OpenTabs.Count);
			}
		}
	}

	private void OnViewerDragOver(object sender, DragEventArgs e)
	{
		e.Effects = (e.Data.GetDataPresent(typeof(OpenBookTab)) ? DragDropEffects.Move : DragDropEffects.None);
		e.Handled = true;
	}

	private void OnViewerDrop(object sender, DragEventArgs e)
	{
		SetTabDropZoneHot(hot: false);
		if (_vm != null && e.Data.GetData(typeof(OpenBookTab)) is OpenBookTab openBookTab)
		{
			_dragDroppedOnStrip = true;
			e.Handled = true;
			if (!_vm.OpenTabs.Contains(openBookTab))
			{
				ImportForeignTab(openBookTab, _vm.OpenTabs.Count);
			}
		}
	}

	private void OnTabDropZoneToggle(object sender, DragEventArgs e)
	{
		bool tabDropZoneHot = e.RoutedEvent == DragDrop.DragEnterEvent && e.Data.GetDataPresent(typeof(OpenBookTab));
		SetTabDropZoneHot(tabDropZoneHot);
	}

	private void SetTabDropZoneHot(bool hot)
	{
		Brush brush = TryFindResource("AccentFillColorDefaultBrush") as Brush;
		TabDropZone.BorderBrush = ((hot && brush != null) ? brush : ((Brush)FindResource("ControlElevationBorderBrush")));
		TabDropZoneHint.Opacity = (hot ? 1.0 : 0.6);
	}

	private void ShowTabInsertionAdorner(FrameworkElement target, bool rightEdge)
	{
		AdornerLayer adornerLayer = AdornerLayer.GetAdornerLayer(target);
		if (adornerLayer != null && (!(_tabInsertAdorner is TabInsertionAdorner tabInsertionAdorner) || tabInsertionAdorner.AdornedElement != target || tabInsertionAdorner.RightEdge != rightEdge))
		{
			RemoveTabInsertionAdorner();
			Brush brush = (target.TryFindResource("AccentFillColorDefaultBrush") as Brush) ?? Brushes.DodgerBlue;
			_tabInsertAdorner = new TabInsertionAdorner(target, rightEdge, brush);
			_tabInsertLayer = adornerLayer;
			adornerLayer.Add(_tabInsertAdorner);
		}
	}

	private void RemoveTabInsertionAdorner()
	{
		if (_tabInsertAdorner != null && _tabInsertLayer != null)
		{
			_tabInsertLayer.Remove(_tabInsertAdorner);
		}
		_tabInsertAdorner = null;
		_tabInsertLayer = null;
	}

	private void OnResultsRightButtonUp(object sender, MouseButtonEventArgs e)
	{
		if (App.IsProtectMode)
		{
			e.Handled = true;
			return;
		}
		DependencyObject dependencyObject = e.OriginalSource as DependencyObject;
		DataGridRow dataGridRow = null;
		while (dependencyObject != null)
		{
			if (dependencyObject is DataGridRow dataGridRow2)
			{
				dataGridRow = dataGridRow2;
				break;
			}
			dependencyObject = VisualTreeHelper.GetParent(dependencyObject);
		}
		if (!(dataGridRow?.Item is SearchResultRow searchResultRow))
		{
			return;
		}
		Book book = searchResultRow.Book;
		if (_vm == null || (object)book == null || string.IsNullOrEmpty(book.FileID))
		{
			return;
		}
		ContextMenu contextMenu = new ContextMenu();
		bool isFav = _vm.IsFavorited(book.FileID);
		System.Windows.Controls.MenuItem menuItem = new System.Windows.Controls.MenuItem
		{
			Header = (isFav ? SharedStrings.S1006 : SharedStrings.S1007),
			Icon = new SymbolIcon
			{
				Symbol = (isFav ? SymbolRegular.StarOff24 : SymbolRegular.Star24)
			}
		};
		menuItem.Click += delegate
		{
			if (_vm != null)
			{
				_vm.ToggleFavoriteCommand.Execute(book);
				_vm.StatusText = (isFav ? (SharedStrings.S2349 + book.BookName) : (SharedStrings.S2350 + book.BookName));
			}
		};
		contextMenu.Items.Add(menuItem);
		List<ShelfTreeNode> list = _vm.ShelfTree.Where((ShelfTreeNode n) => !n.IsPublisher && n.Kind == ShelfNodeKind.Shelf).ToList();
		if (list.Count > 0 && book.ID > 0)
		{
			contextMenu.Items.Add(new Separator());
			System.Windows.Controls.MenuItem menuItem2 = new System.Windows.Controls.MenuItem
			{
				Header = SharedStrings.S999,
				Icon = new SymbolIcon
				{
					Symbol = SymbolRegular.Add24
				}
			};
			MirrorSubmenuChevron(menuItem2);
			BuildAddToShelfItems(menuItem2.Items, list, new Book[1] { book }, book.FileID);
			contextMenu.Items.Add(menuItem2);
		}
		contextMenu.PlacementTarget = dataGridRow;
		contextMenu.IsOpen = true;
		e.Handled = true;
	}

	private async void OnShelfMenuClick(object sender, RoutedEventArgs e)
	{
		if (_vm == null)
		{
			return;
		}
		await _vm.LoadShelfTreeAsync();
		LibraryViewModel vm = _vm;
		Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (Book allBook in vm.AllBooks)
		{
			if (!string.IsNullOrEmpty(allBook.FileID) && allBook.BookName != null)
			{
				dictionary.TryAdd(allBook.FileID, allBook.BookName);
			}
		}
		_shelfBookNames = dictionary;
		ContextMenu menu = new ContextMenu
		{
			PlacementTarget = ShelfMenuButton,
			Placement = PlacementMode.Bottom,
			FlowDirection = FlowDirection.RightToLeft
		};
		Wpf.Ui.Controls.TextBox search = new Wpf.Ui.Controls.TextBox
		{
			PlaceholderText = SharedStrings.S1010,
			MinWidth = 240.0,
			Margin = new Thickness(6.0, 4.0, 6.0, 6.0),
			FontSize = 13.0
		};
		menu.Items.Add(search);
		menu.Items.Add(new Separator());
		search.TextChanged += delegate
		{
			Rebuild();
		};
		search.KeyDown += delegate(object _, KeyEventArgs ke)
		{
			if (ke.Key == Key.Return)
			{
				foreach (object item in (IEnumerable)menu.Items)
				{
					if (item is System.Windows.Controls.MenuItem { IsEnabled: not false } menuItem)
					{
						menuItem.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
						ke.Handled = true;
						break;
					}
				}
			}
		};
		Rebuild();
		menu.Opened += delegate
		{
			base.Dispatcher.BeginInvoke((Action)delegate
			{
				search.Focus();
				Keyboard.Focus(search);
			}, DispatcherPriority.Input);
		};
		ShelfMenuButton.ContextMenu = menu;
		menu.IsOpen = true;
		void Rebuild()
		{
			while (menu.Items.Count > 2)
			{
				menu.Items.RemoveAt(2);
			}
			string text = search.Text?.Trim() ?? string.Empty;
			if (text.Length == 0)
			{
				BuildShelfMenuBody(menu.Items, vm);
			}
			else
			{
				BuildShelfSearchResults(menu.Items, vm, text);
			}
		}
	}

	private void BuildShelfMenuBody(ItemCollection target, LibraryViewModel vm)
	{
		System.Windows.Controls.MenuItem menuItem = new System.Windows.Controls.MenuItem
		{
			Header = SharedStrings.S743
		};
		menuItem.Click += delegate
		{
			vm.ClearShelfFilter();
		};
		target.Add(menuItem);
		target.Add(new Separator());
		List<ShelfTreeNode> list = vm.ShelfTree.Where((ShelfTreeNode n) => n.IsPublisher).ToList();
		List<ShelfTreeNode> list2 = vm.ShelfTree.Where((ShelfTreeNode n) => !n.IsPublisher).ToList();
		if (list.Count == 0 && list2.Count == 0)
		{
			target.Add(new System.Windows.Controls.MenuItem
			{
				Header = SharedStrings.S1011,
				IsEnabled = false
			});
			return;
		}
		if (list.Count > 0)
		{
			BuildShelfMenuItems(target, list);
		}
		if (list.Count > 0 && list2.Count > 0)
		{
			target.Add(new Separator());
		}
		if (list2.Count > 0)
		{
			BuildShelfMenuItems(target, list2);
		}
	}

	private void BuildShelfSearchResults(ItemCollection target, LibraryViewModel vm, string query)
	{
		int added = 0;
		Walk(vm.ShelfTree, string.Empty);
		if (added == 0)
		{
			target.Add(new System.Windows.Controls.MenuItem
			{
				Header = SharedStrings.S1015,
				IsEnabled = false
			});
		}
		void Walk(IReadOnlyList<ShelfTreeNode> nodes, string path)
		{
			foreach (ShelfTreeNode node in nodes)
			{
				if (added >= 60)
				{
					break;
				}
				string text;
				switch (node.Kind)
				{
				case ShelfNodeKind.Book:
					text = ResolveBookName(node.FileId);
					break;
				case ShelfNodeKind.Page:
				{
					object obj = node.Title;
					if (obj == null)
					{
						int? page = node.Page;
						if (page.HasValue)
						{
							int valueOrDefault = page.GetValueOrDefault();
							obj = $"{SharedStrings.S2351}{valueOrDefault}";
						}
						else
						{
							obj = "דף";
						}
					}
					text = (string)obj;
					break;
				}
				default:
					text = node.Title ?? SharedStrings.S1014;
					break;
				}
				string text2 = text;
				if (text2.Contains(query, StringComparison.OrdinalIgnoreCase))
				{
					System.Windows.Controls.MenuItem menuItem = new System.Windows.Controls.MenuItem
					{
						Header = (string.IsNullOrEmpty(path) ? text2 : (text2 + "        " + path))
					};
					AttachShelfSearchAction(menuItem, node);
					target.Add(menuItem);
					added++;
				}
				if (node.Children.Count > 0)
				{
					Walk(node.Children, string.IsNullOrEmpty(path) ? text2 : (path + " ‹ " + text2));
				}
			}
		}
	}

	private void AttachShelfSearchAction(System.Windows.Controls.MenuItem item, ShelfTreeNode node)
	{
		switch (node.Kind)
		{
		case ShelfNodeKind.Shelf:
		{
			ShelfTreeNode shelf = node;
			item.Click += delegate
			{
				_vm?.FilterByShelfNode(shelf);
			};
			break;
		}
		case ShelfNodeKind.Book:
		{
			string bookFile = node.FileId;
			item.Click += async delegate
			{
				await ((App)Application.Current).OpenShelfTargetAsync(bookFile, 0);
			};
			break;
		}
		case ShelfNodeKind.Page:
		{
			string pageFile = node.FileId;
			int page = node.Page.GetValueOrDefault();
			item.Click += async delegate
			{
				await ((App)Application.Current).OpenShelfTargetAsync(pageFile, page);
			};
			break;
		}
		}
	}

	private static void MirrorSubmenuChevron(System.Windows.Controls.MenuItem item)
	{
		item.Loaded += delegate(object s, RoutedEventArgs _)
		{
			System.Windows.Controls.MenuItem menuItem = (System.Windows.Controls.MenuItem)s;
			menuItem.ApplyTemplate();
			if (menuItem.Template?.FindName("Chevron", menuItem) is FrameworkElement frameworkElement)
			{
				frameworkElement.RenderTransformOrigin = new Point(0.5, 0.5);
				frameworkElement.RenderTransform = new ScaleTransform(-1.0, 1.0);
			}
		};
	}

	private void AttachLazyChildren(System.Windows.Controls.MenuItem parent, IReadOnlyList<ShelfTreeNode> children)
	{
		System.Windows.Controls.MenuItem placeholder = new System.Windows.Controls.MenuItem
		{
			Header = "…",
			IsEnabled = false
		};
		parent.Items.Add(placeholder);
		bool built = false;
		parent.SubmenuOpened += delegate
		{
			if (!built)
			{
				built = true;
				parent.Items.Remove(placeholder);
				BuildShelfMenuItems(parent.Items, children);
			}
		};
	}

	private void BuildShelfMenuItems(ItemCollection target, IEnumerable<ShelfTreeNode> nodes)
	{
		foreach (ShelfTreeNode node in nodes)
		{
			switch (node.Kind)
			{
			case ShelfNodeKind.Shelf:
			{
				System.Windows.Controls.MenuItem menuItem2 = new System.Windows.Controls.MenuItem
				{
					Header = (node.Title ?? SharedStrings.S1014)
				};
				MirrorSubmenuChevron(menuItem2);
				if (node.Pinned)
				{
					menuItem2.FontWeight = FontWeights.Bold;
				}
				ShelfTreeNode shelf = node;
				System.Windows.Controls.MenuItem menuItem3 = new System.Windows.Controls.MenuItem
				{
					Header = SharedStrings.S1016
				};
				menuItem3.Click += delegate
				{
					_vm?.FilterByShelfNode(shelf);
				};
				menuItem2.Items.Add(menuItem3);
				menuItem2.Items.Add(new Separator());
				if (node.Children.Count == 0)
				{
					menuItem2.Items.Add(new System.Windows.Controls.MenuItem
					{
						Header = SharedStrings.S1017,
						IsEnabled = false
					});
				}
				else
				{
					AttachLazyChildren(menuItem2, node.Children);
				}
				target.Add(menuItem2);
				break;
			}
			case ShelfNodeKind.Book:
			{
				System.Windows.Controls.MenuItem menuItem4 = new System.Windows.Controls.MenuItem
				{
					Header = ResolveBookName(node.FileId)
				};
				string fileId2 = node.FileId;
				if (node.Children.Count > 0)
				{
					MirrorSubmenuChevron(menuItem4);
					System.Windows.Controls.MenuItem menuItem5 = new System.Windows.Controls.MenuItem
					{
						Header = SharedStrings.S1018
					};
					menuItem5.Click += async delegate
					{
						await ((App)Application.Current).OpenShelfTargetAsync(fileId2, 0);
					};
					menuItem4.Items.Add(menuItem5);
					menuItem4.Items.Add(new Separator());
					AttachLazyChildren(menuItem4, node.Children);
				}
				else
				{
					menuItem4.Click += async delegate
					{
						await ((App)Application.Current).OpenShelfTargetAsync(fileId2, 0);
					};
				}
				target.Add(menuItem4);
				break;
			}
			case ShelfNodeKind.Page:
			{
				object obj = node.Title;
				if (obj == null)
				{
					int? page = node.Page;
					if (page.HasValue)
					{
						int valueOrDefault = page.GetValueOrDefault();
						obj = $"{SharedStrings.S2352}{valueOrDefault}";
					}
					else
					{
						obj = "דף";
					}
				}
				string header = (string)obj;
				System.Windows.Controls.MenuItem menuItem = new System.Windows.Controls.MenuItem
				{
					Header = header
				};
				string fileId = node.FileId;
				int page2 = node.Page.GetValueOrDefault();
				menuItem.Click += async delegate
				{
					await ((App)Application.Current).OpenShelfTargetAsync(fileId, page2);
				};
				target.Add(menuItem);
				break;
			}
			}
		}
	}

	private void BuildAddToShelfItems(ItemCollection target, IEnumerable<ShelfTreeNode> shelves, IReadOnlyList<Book> books, string? checkFileId)
	{
		foreach (ShelfTreeNode item in shelves.Where((ShelfTreeNode n) => n.Kind == ShelfNodeKind.Shelf))
		{
			int nodeId = item.NodeId;
			List<ShelfTreeNode> list = item.Children.Where((ShelfTreeNode c) => c.Kind == ShelfNodeKind.Shelf).ToList();
			bool flag = checkFileId != null && item.Children.Any((ShelfTreeNode c) => c.Kind == ShelfNodeKind.Book && string.Equals(c.FileId, checkFileId, StringComparison.Ordinal));
			System.Windows.Controls.MenuItem menuItem = new System.Windows.Controls.MenuItem
			{
				Header = (item.Title ?? SharedStrings.S1014)
			};
			if (item.Pinned)
			{
				menuItem.FontWeight = FontWeights.Bold;
			}
			if (list.Count > 0)
			{
				MirrorSubmenuChevron(menuItem);
				System.Windows.Controls.MenuItem menuItem2 = new System.Windows.Controls.MenuItem
				{
					Header = SharedStrings.S1019,
					Icon = (flag ? Check() : null),
					IsEnabled = !flag
				};
				if (!flag)
				{
					menuItem2.Click += async delegate
					{
						await AddBookToShelfAsync(books, nodeId);
					};
				}
				menuItem.Items.Add(menuItem2);
				menuItem.Items.Add(new Separator());
				BuildAddToShelfItems(menuItem.Items, list, books, checkFileId);
			}
			else if (flag)
			{
				menuItem.IsEnabled = false;
				menuItem.Icon = Check();
			}
			else
			{
				menuItem.Click += async delegate
				{
					await AddBookToShelfAsync(books, nodeId);
				};
			}
			target.Add(menuItem);
		}
		static SymbolIcon Check()
		{
			return new SymbolIcon
			{
				Symbol = SymbolRegular.Checkmark24
			};
		}
	}

	private string ResolveBookName(string? fileId)
	{
		if (string.IsNullOrEmpty(fileId))
		{
			return SharedStrings.S795;
		}
		if (_shelfBookNames != null && _shelfBookNames.TryGetValue(fileId, out string value))
		{
			return value;
		}
		return SharedStrings.S2353 + fileId;
	}

	private async Task AddBookToShelfAsync(IReadOnlyList<Book> books, int madafId)
	{
		if (books.Count == 0 || madafId <= 0)
		{
			return;
		}
		try
		{
			IMadafRepository madafs = (IMadafRepository)App.Services.GetService(typeof(IMadafRepository));
			foreach (Book book in books)
			{
				if (book.ID > 0)
				{
					await madafs.AddBookAsync(madafId, book.ID);
				}
			}
			WeakReferenceMessenger.Default.Send(new ShelvesChangedMessage());
			if (_vm != null)
			{
				_vm.StatusText = ((books.Count == 1) ? (SharedStrings.S2354 + books[0].BookName) : $"{SharedStrings.S2355}{books.Count}{SharedStrings.S2356}");
			}
		}
		catch (Exception ex)
		{
			if (_vm != null)
			{
				_vm.StatusText = SharedStrings.S2357 + ex.Message;
			}
		}
	}

	private void StartTextLayerRepair(Book book)
	{
		if (string.IsNullOrEmpty(book.FileID) && string.IsNullOrEmpty(book.RelativePath))
		{
			return;
		}
		if (_repairWindow != null)
		{
			if (_repairWindow.WindowState == WindowState.Minimized)
			{
				_repairWindow.WindowState = WindowState.Normal;
			}
			_repairWindow.Activate();
			return;
		}
		RepairBookOcrWindow window = App.Services.GetService(typeof(RepairBookOcrWindow)) as RepairBookOcrWindow;
		if (window == null)
		{
			HebrewMessageBox.Show(SharedStrings.S1023, SharedStrings.S558, System.Windows.MessageBoxButton.OK, MessageBoxImage.Hand);
			return;
		}
		((RepairBookOcrViewModel)window.DataContext).InitializeAsync(book);
		_repairWindow = window;
		window.Closed += delegate
		{
			if (_repairWindow == window)
			{
				_repairWindow = null;
			}
		};
		window.Show();
	}

	private void OpenDeleteBookDialog(IReadOnlyList<Book> books)
	{
		if (!App.IsProtectMode && !App.IsNetworkInstall && books.Count != 0)
		{
			DeleteBookViewModel deleteBookViewModel = (DeleteBookViewModel)App.Services.GetService(typeof(DeleteBookViewModel));
			deleteBookViewModel.Books = books;
			DeleteBookWindow obj = (DeleteBookWindow)App.Services.GetService(typeof(DeleteBookWindow));
			obj.DataContext = deleteBookViewModel;
			obj.Owner = Window.GetWindow(this);
			obj.ShowDialog();
			if (deleteBookViewModel.AnyDeleted)
			{
				_vm?.LoadCommand.ExecuteAsync(null);
			}
		}
	}

	private void WireHost(PdfJsHost host)
	{
		host.TocQuickAddRequested += async delegate
		{
			if (host == _activeHost)
			{
				await DoQuickAddTocAsync();
			}
		};
		host.TocEditRequested += async delegate
		{
			if (host == _activeHost)
			{
				await DoOpenTocEditorAsync();
			}
		};
		host.VerifiedHitPagesReceived += delegate(object? _, IReadOnlyList<int> pages)
		{
			if (host == _activeHost)
			{
				base.Dispatcher.BeginInvoke((Action)delegate
				{
					_vm?.ApplyVerifiedHitPages(pages);
				});
			}
		};
		host.FuzzyFinalPagesReceived += delegate(object? _, IReadOnlyList<int> pages)
		{
			if (host == _activeHost)
			{
				base.Dispatcher.BeginInvoke((Action)delegate
				{
					_vm?.ApplyFuzzyFinalPages(pages);
				});
			}
		};
		host.HighlightProgressChanged += delegate(object? _, int n)
		{
			if (host == _activeHost)
			{
				base.Dispatcher.BeginInvoke((Action)delegate
				{
					_vm?.ApplyHighlightProgress(n);
				});
			}
		};
		host.CurrentPageChanged += delegate(object? _, int p)
		{
			if (host == _activeHost)
			{
				base.Dispatcher.BeginInvoke((Action)delegate
				{
					_vm?.ReportViewerPage(p);
				});
			}
		};
		host.ImmersiveToggleRequested += delegate
		{
			if (host == _activeHost)
			{
				base.Dispatcher.BeginInvoke((Action)delegate
				{
					_vm?.Main.ToggleImmersiveCommand.Execute(null);
				});
			}
		};
		host.ImmersiveExitRequested += delegate
		{
			if (host == _activeHost)
			{
				base.Dispatcher.BeginInvoke((Action)delegate
				{
					if (_vm != null)
					{
						_vm.Main.ImmersiveReading = false;
					}
				});
			}
		};
		host.ShortcutRequested += delegate(object? _, string t)
		{
			if (host == _activeHost)
			{
				base.Dispatcher.BeginInvoke((Action)delegate
				{
					ShortcutAction? shortcutAction = ShortcutKeyMap.FromViewerToken(t);
					if (shortcutAction.HasValue)
					{
						HandleShortcut(shortcutAction.Value);
					}
				});
			}
		};
	}

	private PdfJsHost EnsureHost(OpenBookTab tab)
	{
		if (_tabHosts.TryGetValue(tab, out PdfJsHost value))
		{
			return value;
		}
		PdfJsHost pdfJsHost = new PdfJsHost
		{
			HorizontalAlignment = HorizontalAlignment.Stretch,
			VerticalAlignment = VerticalAlignment.Stretch,
			Visibility = Visibility.Collapsed
		};
		_tabHosts[tab] = pdfJsHost;
		ViewerHostContainer.Children.Insert(0, pdfJsHost);
		WireHost(pdfJsHost);
		if (_paths != null)
		{
			pdfJsHost.PrewarmAsync(_paths);
		}
		return pdfJsHost;
	}

	private void SetActiveHost(PdfJsHost? host)
	{
		_activeHost = host;
		foreach (PdfJsHost value in _tabHosts.Values)
		{
			value.Visibility = ((value != host) ? Visibility.Collapsed : Visibility.Visible);
		}
		PdfPlaceholder.Visibility = ((host != null) ? Visibility.Collapsed : Visibility.Visible);
		TabDropZone.Visibility = ((host != null) ? Visibility.Collapsed : Visibility.Visible);
		if (host != null)
		{
			SetTabDropZoneHot(hot: false);
		}
		RebuildChromeController();
	}

	private void RebuildChromeController()
	{
		_chrome?.Detach();
		_chrome = null;
		if (_vm != null && _activeHost != null)
		{
			_chrome = new ChromeAutoHideController(_vm.Main, _vm, () => _vm.ShowInBookChrome, InBookChromeBar, _activeHost, PinChromeBtn, PinChromeIcon, InBookBox);
		}
	}

	private void BringActiveTabIntoView()
	{
		if (_vm?.ActiveTab == null || TabItems == null)
		{
			return;
		}
		OpenBookTab tab = _vm.ActiveTab;
		base.Dispatcher.BeginInvoke((Action)delegate
		{
			if (TabItems.ItemContainerGenerator.ContainerFromItem(tab) is FrameworkElement frameworkElement)
			{
				frameworkElement.BringIntoView();
			}
		}, DispatcherPriority.Loaded);
	}

	private void OnOpenTabsChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		if (e.Action == NotifyCollectionChangedAction.Move || e.OldItems == null)
		{
			return;
		}
		foreach (OpenBookTab oldItem in e.OldItems)
		{
			if (_tabHosts.TryGetValue(oldItem, out PdfJsHost value))
			{
				_tabHosts.Remove(oldItem);
				_hostLoadedPath.Remove(value);
				ViewerHostContainer.Children.Remove(value);
				if (value == _activeHost)
				{
					_activeHost = null;
				}
				try
				{
					value.Dispose();
				}
				catch
				{
				}
			}
		}
		if (_vm == null || _vm.OpenTabs.Count != 0 || _isSessionOwner)
		{
			return;
		}
		base.Dispatcher.BeginInvoke((Action)delegate
		{
			LibraryViewModel? vm = _vm;
			if (vm != null && vm.OpenTabs.Count == 0)
			{
				Window.GetWindow(this)?.Close();
			}
		});
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
			case "ActiveTab":
				if (_vm.ActiveTab == null)
				{
					SetActiveHost(null);
				}
				else
				{
					SetActiveHost(EnsureHost(_vm.ActiveTab));
				}
				BringActiveTabIntoView();
				break;
			case "CurrentPdfPath":
			case "MarkedPdfPath":
			case "CurrentTextRelativePath":
				if (!_vm.BatchingInlineOpen)
				{
					await OpenOrClearAsync();
					_narrowPreferCatalog = false;
					ApplyResponsiveLayout();
				}
				break;
			case "CurrentBookHits":
				if (!_vm.BatchingInlineOpen && !_vm.IsTextMode && _activeHost != null && string.IsNullOrEmpty(_vm.MarkedPdfPath))
				{
					await _activeHost.SetHighlightXmlAsync(_vm.CurrentBookHits?.HighlightXml, _vm.CurrentBookHits?.MatchedTerms);
				}
				break;
			case "CurrentPage":
				if (!_vm.BatchingInlineOpen && !_vm.IsTextMode && _activeHost != null)
				{
					await _activeHost.GoToPageAsync(_vm.CurrentPage);
				}
				break;
			}
		}
		catch (Exception ex)
		{
			_vm.StatusText = SharedStrings.S2358 + ex.Message;
		}
	}

	private async Task OpenOrClearAsync()
	{
		if (_vm == null || _paths == null)
		{
			return;
		}
		PdfJsHost host = _activeHost;
		if (host == null)
		{
			return;
		}
		if (_vm.IsTextMode && !string.IsNullOrEmpty(_vm.CurrentTextRelativePath))
		{
			string rel = _vm.CurrentTextRelativePath;
			string absText = _paths.OtzrayaTextPath(rel);
			if (!(absText == _lastOpenedPath))
			{
				host.ShowLoading(show: true);
				await host.OpenTextAsync(_paths, rel, _vm.CurrentBookTitle, _vm.CurrentTextTerms);
				if (host == _activeHost && !(_vm.CurrentTextRelativePath != rel))
				{
					_lastOpenedPath = absText;
				}
			}
		}
		else
		{
			if (string.IsNullOrEmpty(_vm.CurrentPdfPath))
			{
				return;
			}
			bool flag = !string.IsNullOrEmpty(_vm.MarkedPdfPath);
			string pathToOpen = (flag ? _vm.MarkedPdfPath : _vm.CurrentPdfPath);
			if (pathToOpen == _lastOpenedPath)
			{
				return;
			}
			host.ShowLoading(show: true);
			await host.OpenAsync(_paths, pathToOpen, _vm.CurrentPage, flag ? null : _vm.CurrentBookHits?.HighlightXml, flag ? null : _vm.CurrentBookHits?.MatchedTerms);
			if (host != _activeHost)
			{
				return;
			}
			_lastOpenedPath = pathToOpen;
			if ((object)_vm.SelectedBook == null)
			{
				return;
			}
			try
			{
				IReadOnlyList<TocEntry> bookTocAsync = await ((ICatalogRepository)App.Services.GetService(typeof(ICatalogRepository))).GetTocAsync(_vm.SelectedBook.ID);
				if (host == _activeHost)
				{
					await host.SetBookTocAsync(bookTocAsync);
				}
			}
			catch
			{
			}
		}
	}

	private void ApplyTabletSizing()
	{
		CatalogGrid.RowHeight = (App.IsTabletMode ? 44 : 32);
	}

	private void ApplyResponsiveLayout()
	{
		if (_vm != null && !(OuterGrid.ActualWidth <= 0.0) && !_vm.Main.ImmersiveReading)
		{
			_narrow = OuterGrid.ActualWidth < 720.0;
			if (!_narrow)
			{
				CatalogCol.MinWidth = 320.0;
				PdfCol.MinWidth = 320.0;
				LibSplitterCol.Width = new GridLength(6.0);
				Splitter.Visibility = Visibility.Visible;
				CatalogPanel.Visibility = Visibility.Visible;
				Grid.SetColumnSpan(TopSearchCard, 1);
				Grid.SetRow(BookPaneBorder, 0);
				Grid.SetRowSpan(BookPaneBorder, 3);
				Grid.SetRow(Splitter, 0);
				Grid.SetRowSpan(Splitter, 3);
				ApplyRatioToColumns();
			}
			else
			{
				bool flag = _vm.HasOpenBook && !_narrowPreferCatalog;
				Splitter.Visibility = Visibility.Collapsed;
				LibSplitterCol.Width = new GridLength(0.0);
				CatalogCol.MinWidth = 0.0;
				PdfCol.MinWidth = 0.0;
				CatalogCol.Width = new GridLength((!flag) ? 1 : 0, GridUnitType.Star);
				PdfCol.Width = new GridLength(flag ? 1 : 0, GridUnitType.Star);
				CatalogPanel.Visibility = (flag ? Visibility.Collapsed : Visibility.Visible);
				Grid.SetColumnSpan(TopSearchCard, 3);
				Grid.SetRow(BookPaneBorder, 1);
				Grid.SetRowSpan(BookPaneBorder, 2);
				Grid.SetRow(Splitter, 1);
				Grid.SetRowSpan(Splitter, 2);
			}
		}
	}

	private void ApplyRatioToColumns()
	{
		if (_vm == null || OuterGrid.ActualWidth <= 0.0 || _vm.Main.ImmersiveReading)
		{
			return;
		}
		_suppressColumnSync = true;
		try
		{
			CatalogCol.Width = new GridLength(_vm.CatalogRatio, GridUnitType.Star);
			PdfCol.Width = new GridLength(1.0 - _vm.CatalogRatio, GridUnitType.Star);
		}
		finally
		{
			_suppressColumnSync = false;
		}
	}

	private void OnSplitterDragCompleted(object sender, DragCompletedEventArgs e)
	{
		if (_vm != null && !_suppressColumnSync)
		{
			double num = CatalogCol.ActualWidth + PdfCol.ActualWidth;
			if (!(num <= 0.0))
			{
				double ratio = CatalogCol.ActualWidth / num;
				_vm.PersistRatio(ratio);
				ApplyRatioToColumns();
			}
		}
	}

	private void OnCatalogPreviewLeftDown(object sender, MouseButtonEventArgs e)
	{
		_pendingHeaderTap = null;
		if (FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject)?.Item is GroupHeaderRow groupHeaderRow)
		{
			if (FindAncestor<CheckBox>(e.OriginalSource as DependencyObject) != null)
			{
				_vm?.ToggleGroupMarkCommand.Execute(groupHeaderRow);
				e.Handled = true;
			}
			else if (e.StylusDevice != null)
			{
				_pendingHeaderTap = groupHeaderRow;
				_headerTapStart = e.GetPosition(CatalogGrid);
				e.Handled = true;
			}
			else
			{
				_vm?.OnHeaderActivated(groupHeaderRow);
				e.Handled = true;
			}
		}
	}

	private void OnCatalogPreviewLeftUp(object sender, MouseButtonEventArgs e)
	{
		GroupHeaderRow pendingHeaderTap = _pendingHeaderTap;
		_pendingHeaderTap = null;
		if (pendingHeaderTap != null)
		{
			Vector vector = e.GetPosition(CatalogGrid) - _headerTapStart;
			if (!(Math.Abs(vector.X) > 12.0) && !(Math.Abs(vector.Y) > 12.0))
			{
				_vm?.OnHeaderActivated(pendingHeaderTap);
				e.Handled = true;
			}
		}
	}

	private async void OnCatalogDoubleClick(object sender, MouseButtonEventArgs e)
	{
		if (!(e.OriginalSource is DependencyObject dependencyObject) || FindAncestor<DataGridRow>(dependencyObject) == null || (object)_vm?.SelectedBook == null || _paths == null)
		{
			return;
		}
		Book book = _vm.SelectedBook;
		if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
		{
			_vm.PinBook(book);
			return;
		}
		PdfViewerWindow viewer = (PdfViewerWindow)App.Services.GetService(typeof(PdfViewerWindow));
		viewer.Show();
		int result;
		if (string.Equals(book.SourceType, "Text", StringComparison.Ordinal))
		{
			await viewer.OpenTextAsync(book);
		}
		else if (string.Equals(book.SourceType, "Personal", StringComparison.Ordinal))
		{
			string rel = ((!string.IsNullOrEmpty(book.RelativePath)) ? book.RelativePath : book.FileID);
			if (!string.IsNullOrEmpty(rel))
			{
				string personalPath = _paths.PersonalFilePath(rel);
				if (!File.Exists(personalPath))
				{
					_vm.StatusText = SharedStrings.S2359 + personalPath;
					return;
				}
				(string, int) tuple = _vm.OpenTabStateFor(book.FileID ?? rel);
				string pQuery = tuple.Item1;
				int item = tuple.Item2;
				item = await LivePageForActiveBook(book.FileID ?? rel, item);
				await viewer.OpenAsync(book.FileID ?? rel, book.BookName ?? "", personalPath, pQuery, null, item);
			}
		}
		else if (!string.IsNullOrEmpty(book.FileID) && int.TryParse(book.FileID, out result))
		{
			string path = _paths.PdfPath(result, book.Folder);
			if (!(await ((OnDemandBookService)App.Services.GetService(typeof(OnDemandBookService))).EnsureLocalAsync(book, viewer)))
			{
				_vm.StatusText = SharedStrings.S2360 + path;
				return;
			}
			(string, int) tuple = _vm.OpenTabStateFor(book.FileID);
			string catQuery = tuple.Item1;
			int item2 = tuple.Item2;
			item2 = await LivePageForActiveBook(book.FileID, item2);
			await viewer.OpenAsync(book.FileID, book.BookName ?? "", path, catQuery, null, item2);
		}
	}

	private async Task<int> LivePageForActiveBook(string? fileId, int fallback)
	{
		if (_activeHost == null || (_vm?.IsTextMode ?? false))
		{
			return fallback;
		}
		if (!string.Equals(_vm?.ActiveTab?.FileId, fileId, StringComparison.Ordinal))
		{
			return fallback;
		}
		try
		{
			int num = await _activeHost.GetCurrentPageAsync();
			return (num > 0) ? num : fallback;
		}
		catch
		{
			return fallback;
		}
	}

	private async void OnResultDoubleClick(object sender, MouseButtonEventArgs e)
	{
		if ((object)_vm?.SelectedRow?.Book != null)
		{
			if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
			{
				await OpenSelectedInNewWindowAsync();
			}
			else
			{
				await _vm.OpenAndPinSelectedRowAsync();
			}
		}
	}

	private void OnTabScrollChanged(object sender, ScrollChangedEventArgs e)
	{
		if (TabScroll != null)
		{
			bool num = TabScroll.ScrollableWidth > 0.5;
			Visibility visibility = ((!num) ? Visibility.Collapsed : Visibility.Visible);
			TabScrollStartBtn.Visibility = visibility;
			TabScrollEndBtn.Visibility = visibility;
			if (num)
			{
				TabScrollStartBtn.IsEnabled = TabScroll.HorizontalOffset > 0.5;
				TabScrollEndBtn.IsEnabled = TabScroll.HorizontalOffset < TabScroll.ScrollableWidth - 0.5;
			}
		}
	}

	private void OnTabStripScrollClick(object sender, RoutedEventArgs e)
	{
		if (TabScroll != null && sender is FrameworkElement frameworkElement)
		{
			double num = ((frameworkElement.Tag as string == "start") ? (-200.0) : 200.0);
			TabScroll.ScrollToHorizontalOffset(TabScroll.HorizontalOffset + num);
		}
	}

	private void OnAppClosingSaveTabs(object? sender, CancelEventArgs e)
	{
		if (_isSessionOwner)
		{
			SaveSessionNow();
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

	private async Task DoQuickAddTocAsync()
	{
		if ((object)_vm?.SelectedBook == null || _vm.IsTextMode || _activeHost == null)
		{
			return;
		}
		Book book = _vm.SelectedBook;
		int page = await _activeHost.GetCurrentPageAsync();
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
			if (_activeHost != null)
			{
				await _activeHost.SetBookTocAsync(combined);
			}
			_vm.StatusText = $"{SharedStrings.S2361}{page}";
		}
	}

	private async Task DoOpenTocEditorAsync()
	{
		if ((object)_vm?.SelectedBook == null || _vm.IsTextMode)
		{
			return;
		}
		Book book = _vm.SelectedBook;
		TocEditorWindow obj = (TocEditorWindow)App.Services.GetService(typeof(TocEditorWindow));
		obj.Owner = Window.GetWindow(this);
		if (await obj.EditAsync(book.ID, book.BookName ?? "", book.FileID, book.SourceType) == true)
		{
			IReadOnlyList<TocEntry> bookTocAsync = await ((ICatalogRepository)App.Services.GetService(typeof(ICatalogRepository))).GetTocAsync(book.ID);
			if (_activeHost != null)
			{
				await _activeHost.SetBookTocAsync(bookTocAsync);
			}
		}
	}

	private async void OnOpenExternalClick(object sender, RoutedEventArgs e)
	{
		if (_vm == null)
		{
			return;
		}
		if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control)
		{
			await OpenSelectedInNewWindowAsync();
			return;
		}
		if (App.IsProtectMode)
		{
			_vm.StatusText = SharedStrings.S1026;
			return;
		}
		string path = ((!_vm.IsTextMode && !string.IsNullOrEmpty(_vm.CurrentPdfPath)) ? _vm.CurrentPdfPath : _lastOpenedPath);
		if (string.IsNullOrEmpty(path) || !File.Exists(path))
		{
			_vm.StatusText = SharedStrings.S1027;
			return;
		}
		if (!_vm.IsTextMode)
		{
			int page = 0;
			if (_activeHost != null)
			{
				try
				{
					page = await _activeHost.GetCurrentPageAsync();
				}
				catch
				{
				}
			}
			if (page > 0 && ExternalPdfLauncher.TryOpenAtPage(path, page))
			{
				return;
			}
		}
		try
		{
			Process.Start(new ProcessStartInfo
			{
				FileName = path,
				UseShellExecute = true
			});
		}
		catch (Exception ex)
		{
			_vm.StatusText = SharedStrings.S2362 + ex.Message;
		}
	}

	private async void OnCopyLinkClick(object sender, RoutedEventArgs e)
	{
		Book book = _vm?.ActiveTab?.Book ?? _vm?.SelectedBook;
		if (_vm == null || (object)book == null)
		{
			return;
		}
		if (!string.Equals(book.SourceType, "PDF", StringComparison.Ordinal) || !int.TryParse(book.FileID, out var fileId))
		{
			_vm.StatusText = SharedStrings.S1029;
			return;
		}
		int page = 0;
		if (_activeHost != null)
		{
			try
			{
				page = await _activeHost.GetCurrentPageAsync();
			}
			catch
			{
			}
		}
		if (page <= 0)
		{
			page = ((_vm.CurrentPage > 0) ? _vm.CurrentPage : 0);
		}
		string text = DeepLink.Build(fileId, page);
		try
		{
			Clipboard.SetText(text);
			_vm.StatusText = SharedStrings.S9088 + text;
			ShowCopiedToast(sender as UIElement, SharedStrings.S1031);
		}
		catch (Exception exception)
		{
			_vm.StatusText = SharedStrings.S1032;
			Log.Warning(exception, "DeepLink: copy to clipboard failed");
		}
	}

	private static void ShowCopiedToast(UIElement? anchor, string message)
	{
		if (anchor != null)
		{
			Popup popup = new Popup
			{
				PlacementTarget = anchor,
				Placement = PlacementMode.Top,
				StaysOpen = false,
				AllowsTransparency = true,
				PopupAnimation = PopupAnimation.Fade
			};
			popup.Child = new Border
			{
				Background = new SolidColorBrush(Color.FromRgb(50, 49, 48)),
				CornerRadius = new CornerRadius(6.0),
				Padding = new Thickness(12.0, 8.0, 12.0, 8.0),
				Margin = new Thickness(0.0, 0.0, 0.0, 6.0),
				Child = new System.Windows.Controls.TextBlock
				{
					Text = message,
					Foreground = Brushes.White,
					FlowDirection = FlowDirection.RightToLeft,
					TextAlignment = TextAlignment.Center,
					FontSize = 13.0
				}
			};
			popup.IsOpen = true;
			DispatcherTimer timer = new DispatcherTimer
			{
				Interval = TimeSpan.FromSeconds(2.2)
			};
			timer.Tick += delegate
			{
				popup.IsOpen = false;
				timer.Stop();
			};
			timer.Start();
		}
	}

	private async Task OpenSelectedInNewWindowAsync()
	{
		if (_paths == null)
		{
			return;
		}
		Book book = _vm?.ActiveTab?.Book ?? _vm?.SelectedBook;
		if (_vm == null || (object)book == null)
		{
			return;
		}
		int page = 0;
		if (_activeHost != null && !_vm.IsTextMode)
		{
			try
			{
				page = await _activeHost.GetCurrentPageAsync();
			}
			catch
			{
			}
		}
		string query = _vm.ContentQueryForNewWindow();
		if (string.IsNullOrEmpty(query) && !string.IsNullOrWhiteSpace(_vm.ActiveTab?.InBookQuery))
		{
			query = _vm.ActiveTab.InBookQuery;
		}
		PdfViewerWindow viewer = (PdfViewerWindow)App.Services.GetService(typeof(PdfViewerWindow));
		viewer.Show();
		int result;
		if (string.Equals(book.SourceType, "Text", StringComparison.Ordinal))
		{
			await viewer.OpenTextAsync(book, query);
		}
		else if (string.Equals(book.SourceType, "Personal", StringComparison.Ordinal))
		{
			string text = ((!string.IsNullOrEmpty(book.RelativePath)) ? book.RelativePath : book.FileID);
			if (!string.IsNullOrEmpty(text))
			{
				string text2 = _paths.PersonalFilePath(text);
				if (!File.Exists(text2))
				{
					_vm.StatusText = SharedStrings.S2363 + text2;
				}
				else
				{
					await viewer.OpenAsync(book.FileID ?? text, book.BookName ?? "", text2, query, null, page);
				}
			}
		}
		else if (!string.IsNullOrEmpty(book.FileID) && int.TryParse(book.FileID, out result))
		{
			string path = _paths.PdfPath(result, book.Folder);
			if (!(await ((OnDemandBookService)App.Services.GetService(typeof(OnDemandBookService))).EnsureLocalAsync(book, viewer)))
			{
				_vm.StatusText = SharedStrings.S2364 + path;
			}
			else
			{
				await viewer.OpenAsync(book.FileID, book.BookName ?? "", path, query, null, page);
			}
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

	private void OnMarkCheckBoxPreviewClick(object sender, MouseButtonEventArgs e)
	{
		if (sender is CheckBox { Command: { } command } checkBox && command.CanExecute(checkBox.CommandParameter))
		{
			command.Execute(checkBox.CommandParameter);
			e.Handled = true;
		}
	}

	private void OnCorpusFilterPopupOpened(object sender, EventArgs e)
	{
		PopupZOrderHelper.BringToFront(sender as Popup);
	}

	private void OnNewWindowClick(object sender, RoutedEventArgs e)
	{
		new SearchWindow().Show();
	}

	private void OnHistoryEntryClick(object sender, RoutedEventArgs e)
	{
		HistoryToggle.IsChecked = false;
		string entry = (sender as FrameworkElement)?.DataContext as string;
		if (!string.IsNullOrWhiteSpace(entry) && _vm != null)
		{
			base.Dispatcher.BeginInvoke((Action)delegate
			{
				_vm.UseHistoryEntryCommand.Execute(entry);
			}, DispatcherPriority.Background);
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
			CatalogCol.MinWidth = 0.0;
			CatalogCol.Width = new GridLength(0.0);
			LibSplitterCol.Width = new GridLength(0.0);
			CatalogPanel.Visibility = Visibility.Collapsed;
			Splitter.Visibility = Visibility.Collapsed;
			TopSearchCard.Visibility = Visibility.Collapsed;
			FilterCard.Visibility = Visibility.Collapsed;
		}
		else
		{
			CatalogCol.MinWidth = 320.0;
			LibSplitterCol.Width = new GridLength(6.0);
			CatalogPanel.Visibility = Visibility.Visible;
			Splitter.Visibility = Visibility.Visible;
			TopSearchCard.Visibility = Visibility.Visible;
			FilterCard.Visibility = Visibility.Visible;
			ApplyResponsiveLayout();
		}
		ImmersiveIconLib.Symbol = (immersive ? SymbolRegular.FullScreenMinimize24 : SymbolRegular.FullScreenMaximize24);
		ImmersiveBtnLib.ToolTip = (immersive ? SharedStrings.S1033 : SharedStrings.S291);
	}




}
