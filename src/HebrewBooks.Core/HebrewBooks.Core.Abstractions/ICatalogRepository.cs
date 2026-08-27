using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HebrewBooks.Core.Models;

namespace HebrewBooks.Core.Abstractions;

public interface ICatalogRepository
{
	Task<Book?> GetByIdAsync(int id, CancellationToken ct = default(CancellationToken));

	Task<Book?> GetByFileIdAsync(string fileId, CancellationToken ct = default(CancellationToken));

	Task<IReadOnlyList<Book>> ListAsync(int skip, int take, string? sortBy = null, CancellationToken ct = default(CancellationToken), bool includeDescription = true);

	Task<IReadOnlyList<Book>> FindByIdsAsync(IReadOnlyList<int> ids, CancellationToken ct = default(CancellationToken));

	Task<IReadOnlyList<Book>> FindByFileIdsAsync(IReadOnlyList<string> fileIds, CancellationToken ct = default(CancellationToken));

	Task<int> AddAsync(Book book, CancellationToken ct = default(CancellationToken));

	Task UpdateAsync(Book book, CancellationToken ct = default(CancellationToken));

	Task DeleteAsync(int id, CancellationToken ct = default(CancellationToken));

	Task<int> CountAsync(CancellationToken ct = default(CancellationToken));

	Task<string?> MaxFileIdAsync(CancellationToken ct = default(CancellationToken));

	Task<IReadOnlyList<string>> GetDistinctCategoriesAsync(CancellationToken ct = default(CancellationToken));

	Task<IReadOnlyList<TocEntry>> GetTocAsync(int bookId, CancellationToken ct = default(CancellationToken));

	Task SetTocAsync(int bookId, IReadOnlyList<TocEntry> entries, CancellationToken ct = default(CancellationToken));

	Task<IReadOnlyDictionary<int, IReadOnlyList<TocEntry>>> GetTocsAsync(IReadOnlyList<int> bookIds, CancellationToken ct = default(CancellationToken));

	Task<IReadOnlyList<RawTocRow>> GetRawTocsAsync(CancellationToken ct = default(CancellationToken));
}
