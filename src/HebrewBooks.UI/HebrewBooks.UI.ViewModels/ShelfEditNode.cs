using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;
using HebrewBooks.Core.Models;

namespace HebrewBooks.UI.ViewModels;

public sealed partial class ShelfEditNode : ObservableObject
{
	[ObservableProperty]
	private string _display;

	[ObservableProperty]
	private bool _pinned;

	[ObservableProperty]
	private bool _isExpanded;

	[ObservableProperty]
	private bool _isSelected;

	public int NodeId { get; }

	public int? ParentId { get; }

	public ShelfNodeKind Kind { get; }

	public string? FileId { get; }

	public int? Page { get; }

	public ObservableCollection<ShelfEditNode> Children { get; } = new ObservableCollection<ShelfEditNode>();

	public bool IsShelf => Kind == ShelfNodeKind.Shelf;

	public string Glyph => Kind switch
	{
		ShelfNodeKind.Shelf => "\ud83d\udcc1", 
		ShelfNodeKind.Book => "\ud83d\udcd6", 
		_ => "\ud83d\udd16", 
	};





	public ShelfEditNode(int nodeId, int? parentId, ShelfNodeKind kind, string? fileId, int? page, bool pinned, string display)
	{
		NodeId = nodeId;
		ParentId = parentId;
		Kind = kind;
		FileId = fileId;
		Page = page;
		_pinned = pinned;
		_display = display;
	}
}
