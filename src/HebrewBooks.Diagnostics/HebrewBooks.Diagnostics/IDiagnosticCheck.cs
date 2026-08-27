using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HebrewBooks.Diagnostics;

public interface IDiagnosticCheck
{
	string Id { get; }

	string Category { get; }

	Task<IReadOnlyList<DiagnosticResult>> RunAsync(DiagnosticContext context, CancellationToken ct);
}
