using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HebrewBooks.Core.Models;

namespace HebrewBooks.Core.Abstractions;

public interface IShelfTreeRepository
{
	Task<IReadOnlyList<ShelfTreeNode>> GetTreeAsync(CancellationToken ct = default(CancellationToken));

	Task<int> AddShelfAsync(int? parentId, string name, CancellationToken ct = default(CancellationToken));

	Task<int> AddBookAsync(int? parentId, string fileId, CancellationToken ct = default(CancellationToken));

	Task<int> AddPageAsync(int parentId, string fileId, int page, string? label, CancellationToken ct = default(CancellationToken));

	Task RenameAsync(int nodeId, string newTitle, CancellationToken ct = default(CancellationToken));

	Task DeleteAsync(int nodeId, CancellationToken ct = default(CancellationToken));

	Task SetPinnedAsync(int nodeId, bool pinned, CancellationToken ct = default(CancellationToken));

	Task MoveAsync(int nodeId, int? newParentId, CancellationToken ct = default(CancellationToken));
}
