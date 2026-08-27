using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HebrewBooks.Core.Models;

namespace HebrewBooks.Core.Abstractions;

public interface IWorkAreaService
{
	Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default(CancellationToken));

	Task<WorkArea?> LoadAsync(string name, CancellationToken ct = default(CancellationToken));

	Task SaveAsync(WorkArea area, CancellationToken ct = default(CancellationToken));

	Task DeleteAsync(string name, CancellationToken ct = default(CancellationToken));
}
