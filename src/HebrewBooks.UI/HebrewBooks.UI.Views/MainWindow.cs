using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Threading;
using HebrewBooks.Infrastructure.Settings;
using HebrewBooks.UI.Dialogs;
using HebrewBooks.UI.Navigation;
using HebrewBooks.UI.Resources;
using HebrewBooks.UI.Services;
using HebrewBooks.UI.ViewModels;
using Serilog;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace HebrewBooks.UI.Views;

public partial class MainWindow : FluentWindow
{
	private LibraryViewModel? _favLibVm;

	private readonly HashSet<FavoriteFolderViewModel> _favHookedFolders = new HashSet<FavoriteFolderViewModel>();

	private Point _favDragStart;

	private FavoriteBookEntry? _favDragSource;

	private readonly HashSet<string> _favExpandedFolders = new HashSet<string>(StringComparer.Ordinal);

	private readonly bool _unifiedSearch;

	private readonly JsonSettingsStore _settings;









	private void InitFavoritesMenu()
	{
		if (_favLibVm != null)
		{
			return;
		}
		try
		{
			LibraryPage libPage = (LibraryPage)App.Services.GetService(typeof(LibraryPage));
			if (libPage.DataContext is LibraryViewModel vm)
			{
				BindFavoritesVm(vm);
				return;
			}
			RoutedEventHandler handler = null;
			handler = delegate
			{
				if (_favLibVm != null)
				{
					libPage.Loaded -= handler;
				}
				else if (libPage.DataContext is LibraryViewModel vm2)
				{
					libPage.Loaded -= handler;
					BindFavoritesVm(vm2);
				}
			};
			libPage.Loaded += handler;
		}
		catch (Exception exception)
		{
			Log.Warning(exception, "Favorites nav: could not resolve LibraryPage / LibraryViewModel");
		}
	}

	private void BindFavoritesVm(LibraryViewModel vm)
	{
		_favLibVm = vm;
		_favLibVm.FavoriteFolderTree.CollectionChanged += OnFavTreeChanged;
		_favLibVm.RootFavorites.CollectionChanged += delegate
		{
			RebuildFavoritesMenu();
		};
		HookFolderBooksHandlers();
		FavoritesNavItem.AllowDrop = true;
		FavoritesNavItem.DragOver += OnFavFolderDragOver;
		FavoritesNavItem.Drop += delegate(object s, DragEventArgs e)
		{
			OnFavFolderDrop(s, e, string.Empty);
		};
		FavoritesNavItem.Click += OnFavoritesNavItemClick;
		RebuildFavoritesMenu();
		base.PreviewMouseMove += OnWindowMouseMoveForFavoritesDnD;
		base.PreviewMouseLeftButtonUp += delegate
		{
			_favDragSource = null;
		};
	}

	private void OnFavoritesNavItemClick(object sender, RoutedEventArgs e)
	{
		if (!Nav.IsPaneOpen)
		{
			Nav.IsPaneOpen = true;
		}
	}

	private void HookFolderBooksHandlers()
	{
		if (_favLibVm == null)
		{
			return;
		}
		foreach (FavoriteFolderViewModel item in _favLibVm.FavoriteFolderTree)
		{
			if (_favHookedFolders.Add(item))
			{
				item.Books.CollectionChanged += delegate
				{
					RebuildFavoritesMenu();
				};
			}
		}
	}

	private void OnFavTreeChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		HookFolderBooksHandlers();
		RebuildFavoritesMenu();
	}

	private void RebuildFavoritesMenu()
	{
		if (_favLibVm == null)
		{
			return;
		}
		FavoritesNavItem.MenuItems.Clear();
		NavigationViewItem navigationViewItem = new NavigationViewItem
		{
			Content = SharedStrings.S1039,
			Icon = new SymbolIcon
			{
				Symbol = SymbolRegular.FolderAdd24
			}
		};
		navigationViewItem.Click += delegate
		{
			PromptAndCreateFolder();
		};
		FavoritesNavItem.MenuItems.Add(navigationViewItem);
		foreach (FavoriteBookEntry rootFavorite in _favLibVm.RootFavorites)
		{
			FavoritesNavItem.MenuItems.Add(CreateBookNavItem(rootFavorite, indented: false));
		}
		foreach (FavoriteFolderViewModel item in _favLibVm.FavoriteFolderTree)
		{
			string name = item.Name;
			bool flag = _favExpandedFolders.Contains(name);
			NavigationViewItem navigationViewItem2 = new NavigationViewItem
			{
				Content = (flag ? "▼ " : "▶ ") + name,
				Tag = item,
				AllowDrop = !App.IsProtectMode,
				Icon = new SymbolIcon
				{
					Symbol = SymbolRegular.Folder24
				},
				ContextMenu = (App.IsProtectMode ? null : BuildFolderContextMenu(name))
			};
			string capturedName = name;
			navigationViewItem2.Click += delegate
			{
				if (!_favExpandedFolders.Add(capturedName))
				{
					_favExpandedFolders.Remove(capturedName);
				}
				RebuildFavoritesMenu();
			};
			if (!App.IsProtectMode)
			{
				navigationViewItem2.DragOver += OnFavFolderDragOver;
				navigationViewItem2.Drop += delegate(object s, DragEventArgs e)
				{
					OnFavFolderDrop(s, e, capturedName);
				};
			}
			FavoritesNavItem.MenuItems.Add(navigationViewItem2);
			if (!flag)
			{
				continue;
			}
			foreach (FavoriteBookEntry book in item.Books)
			{
				FavoritesNavItem.MenuItems.Add(CreateBookNavItem(book, indented: true));
			}
		}
	}

	private NavigationViewItem CreateBookNavItem(FavoriteBookEntry book, bool indented)
	{
		NavigationViewItem navigationViewItem = new NavigationViewItem
		{
			Content = (indented ? "    " : "") + book.BookName,
			Tag = book,
			Icon = new SymbolIcon
			{
				Symbol = SymbolRegular.Book24
			},
			ToolTip = book.BookName
		};
		navigationViewItem.Click += delegate
		{
			_favLibVm?.OpenFavoriteCommand.Execute(book);
		};
		if (!App.IsProtectMode)
		{
			navigationViewItem.PreviewMouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
			{
				_favDragStart = e.GetPosition(this);
				_favDragSource = book;
			};
			navigationViewItem.ContextMenu = BuildBookContextMenu(book);
		}
		return navigationViewItem;
	}

	private ContextMenu BuildFolderContextMenu(string folderName)
	{
		ContextMenu contextMenu = new ContextMenu();
		System.Windows.Controls.MenuItem menuItem = new System.Windows.Controls.MenuItem
		{
			Header = SharedStrings.S1040
		};
		menuItem.Click += delegate
		{
			if (HebrewMessageBox.Show(SharedStrings.S2365 + folderName + SharedStrings.S2366, SharedStrings.S1042, System.Windows.MessageBoxButton.YesNo, MessageBoxImage.Question, System.Windows.MessageBoxResult.No) == System.Windows.MessageBoxResult.Yes)
			{
				_favLibVm?.DeleteFolderCommand.Execute(folderName);
			}
		};
		contextMenu.Items.Add(menuItem);
		return contextMenu;
	}

	private ContextMenu BuildBookContextMenu(FavoriteBookEntry entry)
	{
		ContextMenu contextMenu = new ContextMenu();
		System.Windows.Controls.MenuItem menuItem = new System.Windows.Controls.MenuItem
		{
			Header = SharedStrings.S1043
		};
		menuItem.Click += delegate
		{
			_favLibVm?.OpenFavoriteCommand.Execute(entry);
		};
		contextMenu.Items.Add(menuItem);
		System.Windows.Controls.MenuItem menuItem2 = new System.Windows.Controls.MenuItem
		{
			Header = SharedStrings.S1044
		};
		if (!string.IsNullOrEmpty(entry.FolderName))
		{
			System.Windows.Controls.MenuItem menuItem3 = new System.Windows.Controls.MenuItem
			{
				Header = SharedStrings.S1045
			};
			menuItem3.Click += delegate
			{
				_favLibVm?.MoveBookToFolderCommand.Execute(new MoveFavoriteRequest(entry, ""));
			};
			menuItem2.Items.Add(menuItem3);
		}
		if (_favLibVm != null)
		{
			foreach (string favoriteFolderName in _favLibVm.FavoriteFolderNames)
			{
				if (!string.Equals(favoriteFolderName, entry.FolderName, StringComparison.Ordinal))
				{
					string target = favoriteFolderName;
					System.Windows.Controls.MenuItem menuItem4 = new System.Windows.Controls.MenuItem
					{
						Header = "\ud83d\udcc1 " + favoriteFolderName
					};
					menuItem4.Click += delegate
					{
						_favLibVm.MoveBookToFolderCommand.Execute(new MoveFavoriteRequest(entry, target));
					};
					menuItem2.Items.Add(menuItem4);
				}
			}
		}
		if (menuItem2.Items.Count == 0)
		{
			menuItem2.Items.Add(new System.Windows.Controls.MenuItem
			{
				Header = SharedStrings.S1046,
				IsEnabled = false
			});
		}
		contextMenu.Items.Add(menuItem2);
		contextMenu.Items.Add(new Separator());
		System.Windows.Controls.MenuItem menuItem5 = new System.Windows.Controls.MenuItem
		{
			Header = SharedStrings.S1006
		};
		menuItem5.Click += delegate
		{
			_favLibVm?.RemoveFavoriteCommand.Execute(entry);
		};
		contextMenu.Items.Add(menuItem5);
		return contextMenu;
	}

	private void OnFavFolderDragOver(object? sender, DragEventArgs e)
	{
		e.Effects = ((e.Data.GetData(typeof(FavoriteBookEntry)) is FavoriteBookEntry) ? DragDropEffects.Move : DragDropEffects.None);
		e.Handled = true;
	}

	private void OnFavFolderDrop(object? sender, DragEventArgs e, string targetFolderName)
	{
		if (_favLibVm != null && e.Data.GetData(typeof(FavoriteBookEntry)) is FavoriteBookEntry entry)
		{
			_favLibVm.MoveBookToFolderCommand.Execute(new MoveFavoriteRequest(entry, targetFolderName ?? string.Empty));
		}
		e.Handled = true;
	}

	private void OnWindowMouseMoveForFavoritesDnD(object sender, MouseEventArgs e)
	{
		if (_favDragSource == null || e.LeftButton != MouseButtonState.Pressed)
		{
			return;
		}
		Point position = e.GetPosition(this);
		if (Math.Abs(position.X - _favDragStart.X) <= SystemParameters.MinimumHorizontalDragDistance && Math.Abs(position.Y - _favDragStart.Y) <= SystemParameters.MinimumVerticalDragDistance)
		{
			return;
		}
		FavoriteBookEntry favDragSource = _favDragSource;
		_favDragSource = null;
		try
		{
			DragDrop.DoDragDrop(this, favDragSource, DragDropEffects.Move);
		}
		catch (Exception exception)
		{
			Log.Warning(exception, "Favorites DnD start failed");
		}
	}

	private void PromptAndCreateFolder()
	{
		if (_favLibVm == null)
		{
			return;
		}
		Window dialog = new Window
		{
			Title = SharedStrings.S1047,
			Width = 340.0,
			Height = 150.0,
			WindowStartupLocation = WindowStartupLocation.CenterOwner,
			Owner = this,
			FlowDirection = FlowDirection.RightToLeft,
			ResizeMode = ResizeMode.NoResize,
			ShowInTaskbar = false
		};
		StackPanel stackPanel = new StackPanel
		{
			Margin = new Thickness(14.0)
		};
		System.Windows.Controls.TextBlock element = new System.Windows.Controls.TextBlock
		{
			Text = SharedStrings.S1048,
			Margin = new Thickness(0.0, 0.0, 0.0, 6.0)
		};
		System.Windows.Controls.TextBox input = new System.Windows.Controls.TextBox
		{
			Margin = new Thickness(0.0, 0.0, 0.0, 10.0)
		};
		StackPanel stackPanel2 = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			HorizontalAlignment = HorizontalAlignment.Left
		};
		System.Windows.Controls.Button button = new System.Windows.Controls.Button
		{
			Content = SharedStrings.S799,
			Width = 80.0,
			IsDefault = true
		};
		System.Windows.Controls.Button element2 = new System.Windows.Controls.Button
		{
			Content = SharedStrings.S359,
			Width = 80.0,
			IsCancel = true,
			Margin = new Thickness(8.0, 0.0, 0.0, 0.0)
		};
		stackPanel2.Children.Add(button);
		stackPanel2.Children.Add(element2);
		stackPanel.Children.Add(element);
		stackPanel.Children.Add(input);
		stackPanel.Children.Add(stackPanel2);
		dialog.Content = stackPanel;
		button.Click += delegate
		{
			string text = input.Text?.Trim();
			if (string.IsNullOrEmpty(text))
			{
				dialog.DialogResult = false;
			}
			else
			{
				_favLibVm.CreateFolderCommand.Execute(text);
				dialog.DialogResult = true;
			}
		};
		dialog.Loaded += delegate
		{
			input.Focus();
		};
		dialog.ShowDialog();
	}

	public MainWindow(MainViewModel vm, IPageService pageService)
	{
		MainWindow mainWindow = this;
		InitializeComponent();
		_settings = (JsonSettingsStore)App.Services.GetService(typeof(JsonSettingsStore));
		ViewOptions view = _settings.Load().View;
		this.OpenAtScaledSize(1280.0, 800.0);
		if (App.IsProtectMode || ResolveStartupPlacement(view.MainWindowPlacement))
		{
			base.SourceInitialized += delegate
			{
				mainWindow.WindowState = WindowState.Maximized;
			};
		}
		if (!App.IsProtectMode)
		{
			base.Closing += delegate
			{
				mainWindow.SaveWindowPlacement();
			};
		}
		AutoHideTaskbarFix.Apply(this);
		base.DataContext = vm;
		ApplyNavTabletSizing();
		App.TabletModeChanged += delegate
		{
			mainWindow.Dispatcher.Invoke(ApplyNavTabletSizing);
		};
		if (App.IsProtectMode)
		{
			base.ResizeMode = ResizeMode.NoResize;
			base.WindowState = WindowState.Maximized;
			base.PreviewKeyDown += OnKioskKeyGuard;
		}
		Nav.SetPageService(pageService);
		_unifiedSearch = view.UnifiedSearchLayout;
		if (_unifiedSearch)
		{
			Nav.MenuItems.Remove(SearchNavItem);
			LibraryNavItem.Content = SharedStrings.S1049;
		}
		else
		{
			LibraryNavItem.Content = SharedStrings.S320;
		}
		double navPaneLength = Nav.OpenPaneLength;
		ApplyImmersive();
		vm.PropertyChanged += delegate(object? _, PropertyChangedEventArgs e)
		{
			if (e.PropertyName == "ImmersiveReading")
			{
				ApplyImmersive();
			}
		};
		Nav.IsPaneOpen = view.NavPaneOpen;
		Nav.PaneOpened += delegate
		{
			mainWindow._settings.Update(delegate(BookshelfOptions o)
			{
				o.View.NavPaneOpen = true;
			});
		};
		Nav.PaneClosed += delegate
		{
			mainWindow._settings.Update(delegate(BookshelfOptions o)
			{
				o.View.NavPaneOpen = false;
			});
		};
		Nav.Navigated += delegate
		{
			vm.ImmersiveReading = false;
		};
		base.PreviewKeyDown += delegate(object _, KeyEventArgs e)
		{
			if (e.Key == Key.F11)
			{
				vm.ImmersiveReading = !vm.ImmersiveReading;
				e.Handled = true;
			}
			else if (e.Key == Key.F9)
			{
				vm.ChromeAutoHide = !vm.ChromeAutoHide;
				e.Handled = true;
			}
			else if (e.Key == Key.Escape && vm.ImmersiveReading)
			{
				vm.ImmersiveReading = false;
				e.Handled = true;
			}
			else
			{
				bool focusInTextBox = Keyboard.FocusedElement is TextBoxBase;
				ShortcutAction? shortcutAction = ShortcutKeyMap.FromKey(e, focusInTextBox);
				if (shortcutAction == ShortcutAction.GoToContentSearch)
				{
					mainWindow.GoSection(mainWindow._unifiedSearch ? typeof(LibraryPage) : typeof(SearchPage));
					e.Handled = true;
				}
				else if (shortcutAction == ShortcutAction.GoToCatalog)
				{
					mainWindow.GoSection(typeof(LibraryPage));
					e.Handled = true;
				}
			}
		};
		base.Loaded += delegate
		{
			mainWindow.Nav.Navigate(typeof(LibraryPage));
			mainWindow.InitFavoritesMenu();
		};
		void ApplyImmersive()
		{
			mainWindow.Nav.OpenPaneLength = (vm.ImmersiveReading ? 0.0 : navPaneLength);
			mainWindow.Nav.IsPaneToggleVisible = !vm.ImmersiveReading;
			mainWindow.ImmersiveRailCol.Width = (vm.ImmersiveReading ? new GridLength(52.0) : new GridLength(0.0));
			Visibility visibility = ((!vm.ImmersiveReading) ? Visibility.Collapsed : Visibility.Visible);
			mainWindow.ImmersiveHint.Visibility = visibility;
			mainWindow.ImmersiveNavStrip.Visibility = visibility;
		}
		void ApplyNavTabletSizing()
		{
			if (App.IsTabletMode)
			{
				mainWindow.Nav.FontSize = 16.0;
			}
			else
			{
				mainWindow.Nav.ClearValue(Control.FontSizeProperty);
			}
		}
	}

	private bool ResolveStartupPlacement(WindowPlacementOptions wp)
	{
		if (wp == null || !wp.Saved)
		{
			return true;
		}
		if (wp.Width < base.MinWidth || wp.Height < base.MinHeight)
		{
			return wp.Maximized;
		}
		Rect rect = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
		double num = Math.Min(wp.Width, rect.Width);
		double num2 = Math.Min(wp.Height, rect.Height);
		base.MaxWidth = rect.Width;
		base.MaxHeight = rect.Height;
		double num3 = wp.Left;
		double num4 = wp.Top;
		if (!rect.IntersectsWith(new Rect(num3, num4, num, num2)))
		{
			Rect workArea = SystemParameters.WorkArea;
			num3 = workArea.Left + (workArea.Width - num) / 2.0;
			num4 = workArea.Top + (workArea.Height - num2) / 2.0;
		}
		base.WindowStartupLocation = WindowStartupLocation.Manual;
		base.Left = num3;
		base.Top = num4;
		base.Width = num;
		base.Height = num2;
		return wp.Maximized;
	}

	private void SaveWindowPlacement()
	{
		try
		{
			Rect rb = base.RestoreBounds;
			if (!rb.IsEmpty && !(rb.Width <= 0.0) && !(rb.Height <= 0.0))
			{
				_settings.Update(delegate(BookshelfOptions o)
				{
					WindowPlacementOptions mainWindowPlacement = o.View.MainWindowPlacement;
					mainWindowPlacement.Saved = true;
					mainWindowPlacement.Maximized = base.WindowState == WindowState.Maximized;
					mainWindowPlacement.Left = rb.Left;
					mainWindowPlacement.Top = rb.Top;
					mainWindowPlacement.Width = rb.Width;
					mainWindowPlacement.Height = rb.Height;
				});
			}
		}
		catch (Exception exception)
		{
			Log.Warning(exception, "Saving main-window placement failed");
		}
	}

	private void OnKioskKeyGuard(object? sender, KeyEventArgs e)
	{
		if (App.IsProtectMode)
		{
			Key key = e.Key;
			ModifierKeys modifiers = Keyboard.Modifiers;
			bool flag = (modifiers & ModifierKeys.Control) != 0;
			bool flag2 = (modifiers & ModifierKeys.Alt) != 0;
			bool flag3 = (modifiers & ModifierKeys.Shift) != 0;
			if (flag2 && key == Key.F4)
			{
				e.Handled = true;
			}
			else if (flag && (key == Key.W || key == Key.F4))
			{
				e.Handled = true;
			}
			else if (flag3 && key == Key.F10)
			{
				e.Handled = true;
			}
			else if (key == Key.Apps)
			{
				e.Handled = true;
			}
		}
	}

	private void OnImmersiveNavLibrary(object sender, RoutedEventArgs e)
	{
		GoSection(typeof(LibraryPage));
	}

	private void OnImmersiveNavSearch(object sender, RoutedEventArgs e)
	{
		GoSection(_unifiedSearch ? typeof(LibraryPage) : typeof(SearchPage));
	}

	private void OnImmersiveExit(object sender, RoutedEventArgs e)
	{
		if (base.DataContext is MainViewModel mainViewModel)
		{
			mainViewModel.ImmersiveReading = false;
		}
	}

	public void NavigateToSection(ShortcutAction action)
	{
		switch (action)
		{
		case ShortcutAction.GoToContentSearch:
			GoSection(_unifiedSearch ? typeof(LibraryPage) : typeof(SearchPage));
			break;
		case ShortcutAction.GoToCatalog:
			GoSection(typeof(LibraryPage));
			break;
		}
	}

	public void NavigateTo(Type page)
	{
		Nav.Navigate(page);
	}

	public void RunSearchDeepLink()
	{
		GoSection(_unifiedSearch ? typeof(LibraryPage) : typeof(SearchPage));
		base.Dispatcher.BeginInvoke((Action)delegate
		{
			string pendingExternalSearch = App.PendingExternalSearch;
			if (pendingExternalSearch != null)
			{
				LibraryViewModel mainLibraryViewModel = App.MainLibraryViewModel;
				if (mainLibraryViewModel != null)
				{
					App.PendingExternalSearch = null;
					mainLibraryViewModel.RunSearchFromExternal(pendingExternalSearch);
				}
			}
		}, DispatcherPriority.Loaded);
	}

	private void GoSection(Type page)
	{
		if (base.DataContext is MainViewModel mainViewModel)
		{
			mainViewModel.ImmersiveReading = false;
		}
		Nav.Navigate(page);
	}



}
