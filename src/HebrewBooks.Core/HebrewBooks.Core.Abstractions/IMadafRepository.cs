using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HebrewBooks.Core.Models;

namespace HebrewBooks.Core.Abstractions;

public interface IMadafRepository
{
	Task<IReadOnlyList<MadafNode>> GetTreeAsync(CancellationToken ct = default(CancellationToken));

	Task<IReadOnlyList<int>> GetBookIdsAsync(int madafId, CancellationToken ct = default(CancellationToken));

	Task<int> AddAsync(string name, CancellationToken ct = default(CancellationToken));

	Task RenameAsync(int madafId, string newName, CancellationToken ct = default(CancellationToken));

	Task DeleteAsync(int madafId, CancellationToken ct = default(CancellationToken));

	Task AddBookAsync(int madafId, int bookId, CancellationToken ct = default(CancellationToken));

	Task RemoveBookAsync(int madafId, int bookId, CancellationToken ct = default(CancellationToken));
}
