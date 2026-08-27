using System.Collections.Generic;

namespace HebrewBooks.Core.Models;

public sealed record ShelfTreeNode(int NodeId, int? ParentId, ShelfNodeKind Kind, string? Title, string? FileId, int? Page, bool Pinned, int SortOrder, IReadOnlyList<ShelfTreeNode> Children, bool IsPublisher = false);
