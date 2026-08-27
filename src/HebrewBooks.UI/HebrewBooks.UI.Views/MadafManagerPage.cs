using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HebrewBooks.Core.Models;
using HebrewBooks.UI.Resources;
using HebrewBooks.UI.ViewModels;
using Wpf.Ui.Controls;

namespace HebrewBooks.UI.Views;

public partial class MadafManagerPage : Page
{
	private MadafManagerViewModel? _vm;

	private bool _initialized;

	private Point _dragStart;

	private ShelfEditNode? _dragCandidate;





	public MadafManagerPage()
	{
		InitializeComponent();
		base.Loaded += OnLoaded;
	}

	private async void OnLoaded(object sender, RoutedEventArgs e)
	{
		if (!_initialized)
		{
			_initialized = true;
			_vm = (MadafManagerViewModel)App.Services.GetService(typeof(MadafManagerViewModel));
			base.DataContext = _vm;
			await _vm.LoadAsync();
		}
		else if (_vm != null)
		{
			await _vm.RefreshAsync();
		}
	}

	private void OnTreeSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
	{
		if (_vm != null)
		{
			_vm.SelectedNode = e.NewValue as ShelfEditNode;
		}
	}

	private void OnAddBookTextChanged(object sender, TextChangedEventArgs e)
	{
		if (_vm != null)
		{
			_vm.BookSearchText = AddBookBox.Text;
		}
	}

	private void OnAddBookSuggestionClick(object sender, MouseButtonEventArgs e)
	{
		if (_vm != null && sender is ItemsControl itemsControl)
		{
			DependencyObject element = e.OriginalSource as DependencyObject;
			if ((itemsControl.ContainerFromElement(element) as ListBoxItem)?.DataContext is Book parameter)
			{
				_vm.AddBookCommand.Execute(parameter);
				AddBookBox.Focus();
				Keyboard.Focus(AddBookBox);
				e.Handled = true;
			}
		}
	}

	private void OnTreeMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		_dragStart = e.GetPosition(null);
		_dragCandidate = FindAncestor<System.Windows.Controls.TreeViewItem>(e.OriginalSource as DependencyObject)?.DataContext as ShelfEditNode;
	}

	private void OnTreeMouseMove(object sender, MouseEventArgs e)
	{
		if (e.LeftButton == MouseButtonState.Pressed && _dragCandidate != null)
		{
			Point position = e.GetPosition(null);
			if (!(Math.Abs(position.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance) || !(Math.Abs(position.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance))
			{
				ShelfEditNode dragCandidate = _dragCandidate;
				_dragCandidate = null;
				DragDrop.DoDragDrop(ShelfTreeView, dragCandidate, DragDropEffects.Move);
			}
		}
	}

	private void OnTreeDragOver(object sender, DragEventArgs e)
	{
		e.Effects = (ResolveDropTarget(e, out var _) ? DragDropEffects.Move : DragDropEffects.None);
		e.Handled = true;
	}

	private async void OnTreeDrop(object sender, DragEventArgs e)
	{
		if (_vm != null && e.Data.GetData(typeof(ShelfEditNode)) is ShelfEditNode shelfEditNode && ResolveDropTarget(e, out var targetShelfId))
		{
			await _vm.MoveNodeAsync(shelfEditNode.NodeId, targetShelfId);
		}
	}

	private bool ResolveDropTarget(DragEventArgs e, out int? targetShelfId)
	{
		targetShelfId = null;
		if (!(e.Data.GetData(typeof(ShelfEditNode)) is ShelfEditNode shelfEditNode))
		{
			return false;
		}
		int? num = ((!(FindAncestor<System.Windows.Controls.TreeViewItem>(e.OriginalSource as DependencyObject)?.DataContext is ShelfEditNode shelfEditNode2)) ? ((int?)null) : ((!shelfEditNode2.IsShelf) ? shelfEditNode2.ParentId : new int?(shelfEditNode2.NodeId)));
		targetShelfId = num;
		if (targetShelfId == shelfEditNode.NodeId)
		{
			return false;
		}
		if (targetShelfId == shelfEditNode.ParentId)
		{
			return false;
		}
		int? num2 = targetShelfId;
		if (num2.HasValue)
		{
			int valueOrDefault = num2.GetValueOrDefault();
			if (IsSelfOrDescendant(shelfEditNode, valueOrDefault))
			{
				return false;
			}
		}
		return true;
	}

	private static bool IsSelfOrDescendant(ShelfEditNode node, int candidateId)
	{
		if (node.NodeId == candidateId)
		{
			return true;
		}
		foreach (ShelfEditNode child in node.Children)
		{
			if (IsSelfOrDescendant(child, candidateId))
			{
				return true;
			}
		}
		return false;
	}

	private void OnTreeRightButtonDown(object sender, MouseButtonEventArgs e)
	{
		if (_vm == null)
		{
			return;
		}
		System.Windows.Controls.TreeViewItem treeViewItem = FindAncestor<System.Windows.Controls.TreeViewItem>(e.OriginalSource as DependencyObject);
		if (treeViewItem?.DataContext is ShelfEditNode shelfEditNode)
		{
			treeViewItem.IsSelected = true;
			treeViewItem.Focus();
			_vm.SelectedNode = shelfEditNode;
			ContextMenu contextMenu = new ContextMenu
			{
				FlowDirection = FlowDirection.RightToLeft,
				PlacementTarget = treeViewItem
			};
			if (shelfEditNode.IsShelf)
			{
				contextMenu.Items.Add(CommandItem(SharedStrings.S304, _vm.NewSubShelfCommand));
				contextMenu.Items.Add(CommandItem(SharedStrings.S306, _vm.RenameNodeCommand));
				contextMenu.Items.Add(CommandItem(shelfEditNode.Pinned ? SharedStrings.S1034 : SharedStrings.S1035, _vm.TogglePinCommand));
			}
			else if (shelfEditNode.Kind == ShelfNodeKind.Page)
			{
				contextMenu.Items.Add(CommandItem(SharedStrings.S1036, _vm.RenameNodeCommand));
			}
			contextMenu.Items.Add(BuildMoveSubmenu(shelfEditNode));
			contextMenu.Items.Add(new Separator());
			contextMenu.Items.Add(CommandItem(SharedStrings.S312, _vm.DeleteNodeCommand));
			contextMenu.IsOpen = true;
			e.Handled = true;
		}
	}

	private System.Windows.Controls.MenuItem BuildMoveSubmenu(ShelfEditNode node)
	{
		System.Windows.Controls.MenuItem menuItem = new System.Windows.Controls.MenuItem
		{
			Header = SharedStrings.S1037
		};
		if (node.ParentId.HasValue)
		{
			System.Windows.Controls.MenuItem menuItem2 = new System.Windows.Controls.MenuItem
			{
				Header = SharedStrings.S1038
			};
			menuItem2.Click += async delegate
			{
				if (_vm != null)
				{
					await _vm.MoveNodeAsync(node.NodeId, null);
				}
			};
			menuItem.Items.Add(menuItem2);
			menuItem.Items.Add(new Separator());
		}
		foreach (ShelfEditNode item in _vm.MoveTargetsFor(node))
		{
			int capturedId = item.NodeId;
			System.Windows.Controls.MenuItem menuItem3 = new System.Windows.Controls.MenuItem
			{
				Header = "\ud83d\udcc1 " + item.Display
			};
			menuItem3.Click += async delegate
			{
				if (_vm != null)
				{
					await _vm.MoveNodeAsync(node.NodeId, capturedId);
				}
			};
			menuItem.Items.Add(menuItem3);
		}
		menuItem.IsEnabled = menuItem.Items.Count > 0;
		return menuItem;
	}

	private static System.Windows.Controls.MenuItem CommandItem(string header, ICommand command)
	{
		return new System.Windows.Controls.MenuItem
		{
			Header = header,
			Command = command
		};
	}

	private static T? FindAncestor<T>(DependencyObject? d) where T : DependencyObject
	{
		while (d != null && !(d is T))
		{
			bool flag = ((d is Visual || d is Visual3D) ? true : false);
			d = (flag ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d));
		}
		return d as T;
	}


}
