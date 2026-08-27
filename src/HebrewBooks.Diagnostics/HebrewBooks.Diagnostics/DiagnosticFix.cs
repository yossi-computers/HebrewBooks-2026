using System;
using System.Threading;
using System.Threading.Tasks;

namespace HebrewBooks.Diagnostics;

public sealed class DiagnosticFix
{
	public required string Id { get; init; }

	public required string Label { get; init; }

	public FixKind Kind { get; init; }

	public Func<CancellationToken, Task<FixOutcome>>? Run { get; init; }
}
