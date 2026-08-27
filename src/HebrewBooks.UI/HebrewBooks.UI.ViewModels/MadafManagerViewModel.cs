using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using HebrewBooks.Core.Abstractions;
using HebrewBooks.Core.Models;
using HebrewBooks.UI.Dialogs;
using HebrewBooks.UI.Messages;
using HebrewBooks.UI.Resources;

namespace HebrewBooks.UI.ViewModels;

public partial class MadafManagerViewModel : ObservableObject
{
	private readonly IShelfTreeRepository _shelfTree;

	private readonly ICatalogRepository _catalog;

	private readonly List<Book> _allBooks = new List<Book>();

	private readonly Dictionary<string, Book> _byFileId = new Dictionary<string, Book>(StringComparer.Ordinal);

	private readonly Dictionary<int, ShelfEditNode> _flat = new Dictionary<int, ShelfEditNode>();

	[ObservableProperty]
	[NotifyCanExecuteChangedFor("NewSubShelfCommand")]
	[NotifyCanExecuteChangedFor("RenameNodeCommand")]
	[NotifyCanExecuteChangedFor("DeleteNodeCommand")]
	[NotifyCanExecuteChangedFor("TogglePinCommand")]
	[NotifyCanExecuteChangedFor("MoveToRootCommand")]
	private ShelfEditNode? _selectedNode;

	[ObservableProperty]
	private string _bookSearchText = string.Empty;

	[ObservableProperty]
	private string _statusText = string.Empty;








	public ObservableCollection<ShelfEditNode> Tree { get; } = new ObservableCollection<ShelfEditNode>();

	public ObservableCollection<Book> BookSuggestions { get; } = new ObservableCollection<Book>();

	private static Window? Owner => Application.Current?.MainWindow;











	public MadafManagerViewModel(IShelfTreeRepository shelfTree, ICatalogRepository catalog)
	{
		_shelfTree = shelfTree;
		_catalog = catalog;
	}

	public async Task LoadAsync()
	{
		if (_allBooks.Count == 0)
		{
			IReadOnlyList<Book> readOnlyList = await _catalog.ListAsync(0, 200000, "BookName");
			_allBooks.AddRange(readOnlyList);
			_byFileId.Clear();
			foreach (Book item in readOnlyList)
			{
				if (!string.IsNullOrEmpty(item.FileID))
				{
					_byFileId[item.FileID] = item;
				}
			}
		}
		await ReloadTreeAsync(notify: false);
	}

	public Task RefreshAsync()
	{
		return ReloadTreeAsync(notify: false);
	}

	private async Task ReloadTreeAsync(bool notify = true)
	{
		HashSet<int> expanded = new HashSet<int>(from n in _flat.Values
			where n.IsExpanded
			select n.NodeId);
		int? selectedId = SelectedNode?.NodeId;
		IReadOnlyList<ShelfTreeNode> source = await _shelfTree.GetTreeAsync();
		_flat.Clear();
		Tree.Clear();
		foreach (ShelfTreeNode item in source.Where((ShelfTreeNode r) => !r.IsPublisher))
		{
			Tree.Add(BuildNode(item, expanded));
		}
		MadafManagerViewModel madafManagerViewModel = this;
		object selectedNode;
		if (selectedId.HasValue)
		{
			int valueOrDefault = selectedId.GetValueOrDefault();
			if (_flat.TryGetValue(valueOrDefault, out ShelfEditNode value))
			{
				selectedNode = value;
				goto IL_01b3;
			}
		}
		selectedNode = null;
		goto IL_01b3;
		IL_01b3:
		madafManagerViewModel.SelectedNode = (ShelfEditNode?)selectedNode;
		if (SelectedNode != null)
		{
			SelectedNode.IsSelected = true;
		}
		if (notify)
		{
			NotifyShelvesChanged();
		}
	}

	private ShelfEditNode BuildNode(ShelfTreeNode src, HashSet<int> expanded)
	{
		ShelfEditNode shelfEditNode = new ShelfEditNode(src.NodeId, src.ParentId, src.Kind, src.FileId, src.Page, src.Pinned, DisplayFor(src))
		{
			IsExpanded = expanded.Contains(src.NodeId)
		};
		_flat[shelfEditNode.NodeId] = shelfEditNode;
		foreach (ShelfTreeNode child in src.Children)
		{
			shelfEditNode.Children.Add(BuildNode(child, expanded));
		}
		return shelfEditNode;
	}

	private string DisplayFor(ShelfTreeNode n)
	{
		Book value;
		return n.Kind switch
		{
			ShelfNodeKind.Shelf => string.IsNullOrWhiteSpace(n.Title) ? SharedStrings.S794 : n.Title, 
			ShelfNodeKind.Book => (n.FileId != null && _byFileId.TryGetValue(n.FileId, out value)) ? (value.BookName ?? n.FileId) : (n.FileId ?? SharedStrings.S795), 
			ShelfNodeKind.Page => (!string.IsNullOrWhiteSpace(n.Title)) ? n.Title : $"{SharedStrings.S2201}{n.Page}", 
			_ => "?", 
		};
	}

	private static void NotifyShelvesChanged()
	{
		WeakReferenceMessenger.Default.Send(new ShelvesChangedMessage());
	}

	[RelayCommand]
	private async Task NewRootShelfAsync()
	{
		string name = TextPromptDialog.Show(Owner, SharedStrings.S797, SharedStrings.S798, "", SharedStrings.S799);
		if (name != null)
		{
			int id = await _shelfTree.AddShelfAsync(null, name);
			await ReloadTreeAsync();
			Select(id);
			StatusText = SharedStrings.S2202 + name + "'";
		}
	}

	private bool CanNewSubShelf()
	{
		return SelectedNode?.IsShelf ?? false;
	}

	[RelayCommand(CanExecute = "CanNewSubShelf")]
	private async Task NewSubShelfAsync()
	{
		ShelfEditNode parent = SelectedNode;
		if (parent == null || !parent.IsShelf)
		{
			return;
		}
		string name = TextPromptDialog.Show(Owner, SharedStrings.S801, SharedStrings.S2203 + parent.Display + "':", "", "צור");
		if (name != null)
		{
			int id = await _shelfTree.AddShelfAsync(parent.NodeId, name);
			if (_flat.TryGetValue(parent.NodeId, out ShelfEditNode value))
			{
				value.IsExpanded = true;
			}
			await ReloadTreeAsync();
			if (_flat.TryGetValue(parent.NodeId, out ShelfEditNode value2))
			{
				value2.IsExpanded = true;
			}
			Select(id);
			StatusText = SharedStrings.S2204 + name + "'";
		}
	}

	private bool CanRenameNode()
	{
		ShelfEditNode selectedNode = SelectedNode;
		if (selectedNode != null)
		{
			ShelfNodeKind kind = selectedNode.Kind;
			if (kind == ShelfNodeKind.Shelf || kind == ShelfNodeKind.Page)
			{
				return true;
			}
		}
		return false;
	}

	[RelayCommand(CanExecute = "CanRenameNode")]
	private async Task RenameNodeAsync()
	{
		ShelfEditNode selectedNode = SelectedNode;
		if (selectedNode != null && selectedNode.Kind != ShelfNodeKind.Book)
		{
			string title = ((selectedNode.Kind == ShelfNodeKind.Shelf) ? SharedStrings.S804 : SharedStrings.S805);
			string newName = TextPromptDialog.Show(Owner, title, SharedStrings.S806, selectedNode.Display, SharedStrings.S503);
			if (newName != null && !(newName == selectedNode.Display))
			{
				await _shelfTree.RenameAsync(selectedNode.NodeId, newName);
				await ReloadTreeAsync();
				StatusText = SharedStrings.S2205 + newName + "'";
			}
		}
	}

	private bool CanDeleteNode()
	{
		return SelectedNode != null;
	}

	[RelayCommand(CanExecute = "CanDeleteNode")]
	private async Task DeleteNodeAsync()
	{
		ShelfEditNode selectedNode = SelectedNode;
		if (selectedNode != null)
		{
			int num = CountDescendants(selectedNode);
			string what = selectedNode.Kind switch
			{
				ShelfNodeKind.Shelf => SharedStrings.S2206 + selectedNode.Display + "'", 
				ShelfNodeKind.Book => SharedStrings.S2207 + selectedNode.Display + "'", 
				_ => SharedStrings.S2208 + selectedNode.Display + "'", 
			};
			if (HebrewMessageBox.Show((num > 0) ? $"{SharedStrings.S2209}{what}{SharedStrings.S2210}{num}{SharedStrings.S2211}" : (SharedStrings.S2212 + what + "?"), SharedStrings.S813, MessageBoxButton.YesNo, MessageBoxImage.Exclamation, MessageBoxResult.No) == MessageBoxResult.Yes)
			{
				await _shelfTree.DeleteAsync(selectedNode.NodeId);
				SelectedNode = null;
				await ReloadTreeAsync();
				StatusText = what + SharedStrings.S2213;
			}
		}
	}

	private bool CanTogglePin()
	{
		return SelectedNode?.IsShelf ?? false;
	}

	[RelayCommand(CanExecute = "CanTogglePin")]
	private async Task TogglePinAsync()
	{
		ShelfEditNode node = SelectedNode;
		if (node != null && node.IsShelf)
		{
			await _shelfTree.SetPinnedAsync(node.NodeId, !node.Pinned);
			await ReloadTreeAsync();
			StatusText = (node.Pinned ? SharedStrings.S815 : SharedStrings.S816);
		}
	}

	private bool CanMoveToRoot()
	{
		return SelectedNode?.ParentId.HasValue ?? false;
	}

	[RelayCommand(CanExecute = "CanMoveToRoot")]
	private async Task MoveToRootAsync()
	{
		ShelfEditNode node = SelectedNode;
		if (node != null && node.ParentId.HasValue)
		{
			await _shelfTree.MoveAsync(node.NodeId, null);
			await ReloadTreeAsync();
			Select(node.NodeId);
			StatusText = "'" + node.Display + SharedStrings.S2214;
		}
	}

	public async Task MoveNodeAsync(int nodeId, int? targetShelfId)
	{
		if (nodeId == targetShelfId)
		{
			return;
		}
		if (targetShelfId.HasValue)
		{
			int valueOrDefault = targetShelfId.GetValueOrDefault();
			if (IsDescendant(nodeId, valueOrDefault))
			{
				return;
			}
		}
		await _shelfTree.MoveAsync(nodeId, targetShelfId);
		if (targetShelfId.HasValue)
		{
			int valueOrDefault2 = targetShelfId.GetValueOrDefault();
			if (_flat.TryGetValue(valueOrDefault2, out ShelfEditNode value))
			{
				value.IsExpanded = true;
			}
		}
		await ReloadTreeAsync();
		if (targetShelfId.HasValue)
		{
			int valueOrDefault3 = targetShelfId.GetValueOrDefault();
			if (_flat.TryGetValue(valueOrDefault3, out ShelfEditNode value2))
			{
				value2.IsExpanded = true;
			}
		}
		Select(nodeId);
		StatusText = SharedStrings.S818;
	}

	public IReadOnlyList<ShelfEditNode> MoveTargetsFor(ShelfEditNode node)
	{
		HashSet<int> blocked = new HashSet<int> { node.NodeId };
		Collect(node);
		List<ShelfEditNode> result = new List<ShelfEditNode>();
		Walk(Tree);
		return result;
		void Collect(ShelfEditNode n)
		{
			foreach (ShelfEditNode child in n.Children)
			{
				blocked.Add(child.NodeId);
				Collect(child);
			}
		}
		void Walk(IEnumerable<ShelfEditNode> nodes)
		{
			foreach (ShelfEditNode node2 in nodes)
			{
				if (node2.IsShelf && !blocked.Contains(node2.NodeId) && node2.NodeId != node.ParentId)
				{
					result.Add(node2);
				}
				Walk(node2.Children);
			}
		}
	}

	private void RefreshBookSuggestions()
	{
		BookSuggestions.Clear();
		string text = (BookSearchText ?? "").Trim();
		if (text.Length < 2)
		{
			return;
		}
		int? num = TargetShelfId();
		ObservableCollection<ShelfEditNode> source;
		if (num.HasValue)
		{
			int valueOrDefault = num.GetValueOrDefault();
			if (_flat.TryGetValue(valueOrDefault, out ShelfEditNode value))
			{
				source = value.Children;
				goto IL_0061;
			}
		}
		source = Tree;
		goto IL_0061;
		IL_0061:
		HashSet<string> hashSet = new HashSet<string>(from c in source
			where c.Kind == ShelfNodeKind.Book && c.FileId != null
			select c.FileId, StringComparer.Ordinal);
		int num2 = 0;
		foreach (Book allBook in _allBooks)
		{
			if (allBook.FileID != null && hashSet.Contains(allBook.FileID))
			{
				continue;
			}
			string? bookName = allBook.BookName;
			if (bookName == null || !bookName.Contains(text, StringComparison.OrdinalIgnoreCase))
			{
				string? authorName = allBook.AuthorName;
				if (authorName == null || !authorName.Contains(text, StringComparison.OrdinalIgnoreCase))
				{
					string? fileID = allBook.FileID;
					if (fileID == null || !fileID.Contains(text, StringComparison.OrdinalIgnoreCase))
					{
						continue;
					}
				}
			}
			BookSuggestions.Add(allBook);
			if (++num2 >= 50)
			{
				break;
			}
		}
	}

	private int? TargetShelfId()
	{
		ShelfEditNode selectedNode = SelectedNode;
		if (selectedNode != null)
		{
			if (selectedNode.IsShelf)
			{
				return selectedNode.NodeId;
			}
			int? parentId = selectedNode.ParentId;
			if (parentId.HasValue)
			{
				return parentId.GetValueOrDefault();
			}
		}
		return null;
	}

	[RelayCommand]
	private async Task AddBookAsync(Book? book)
	{
		if ((object)book == null || string.IsNullOrEmpty(book.FileID))
		{
			return;
		}
		int? target = TargetShelfId();
		if (target.HasValue)
		{
			int valueOrDefault = target.GetValueOrDefault();
			if (_flat.TryGetValue(valueOrDefault, out ShelfEditNode value) && value.Children.Any((ShelfEditNode c) => c.Kind == ShelfNodeKind.Book && c.FileId == book.FileID))
			{
				StatusText = "'" + book.BookName + SharedStrings.S2215;
				BookSearchText = string.Empty;
				return;
			}
		}
		await _shelfTree.AddBookAsync(target, book.FileID);
		BookSearchText = string.Empty;
		if (target.HasValue)
		{
			int valueOrDefault2 = target.GetValueOrDefault();
			if (_flat.TryGetValue(valueOrDefault2, out ShelfEditNode value2))
			{
				value2.IsExpanded = true;
			}
		}
		await ReloadTreeAsync();
		if (target.HasValue)
		{
			int valueOrDefault3 = target.GetValueOrDefault();
			if (_flat.TryGetValue(valueOrDefault3, out ShelfEditNode value3))
			{
				value3.IsExpanded = true;
			}
		}
		string text;
		if (target.HasValue)
		{
			int valueOrDefault4 = target.GetValueOrDefault();
			if (_flat.TryGetValue(valueOrDefault4, out ShelfEditNode value4))
			{
				text = value4.Display;
				goto IL_0275;
			}
		}
		text = SharedStrings.S820;
		goto IL_0275;
		IL_0275:
		string value5 = text;
		StatusText = $"{SharedStrings.S2216}{book.BookName}{SharedStrings.S2217}{value5}'";
	}

	private void Select(int nodeId)
	{
		if (_flat.TryGetValue(nodeId, out ShelfEditNode value))
		{
			value.IsSelected = true;
			SelectedNode = value;
		}
	}

	private static int CountDescendants(ShelfEditNode node)
	{
		int num = 0;
		foreach (ShelfEditNode child in node.Children)
		{
			num += 1 + CountDescendants(child);
		}
		return num;
	}

	private bool IsDescendant(int nodeId, int candidateDescendantId)
	{
		if (!_flat.TryGetValue(nodeId, out ShelfEditNode value))
		{
			return false;
		}
		return Search(value);
		bool Search(ShelfEditNode n)
		{
			return n.Children.Any((ShelfEditNode c) => c.NodeId == candidateDescendantId || Search(c));
		}
	}

}
