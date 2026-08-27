using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HebrewBooks.Core.Models;

namespace HebrewBooks.Core.Abstractions;

public interface IPdfViewer
{
	int CurrentPage { get; }

	double Zoom { get; set; }

	int RotationDegrees { get; set; }

	event EventHandler<int>? PageChanged;

	event EventHandler<string>? OpenFailed;

	Task OpenAsync(string path, int page = 1, CancellationToken ct = default(CancellationToken));

	Task JumpToPageAsync(int page, CancellationToken ct = default(CancellationToken));

	Task ApplyHighlightsAsync(IEnumerable<HitSpan> hits, CancellationToken ct = default(CancellationToken));

	Task SetKioskAsync(bool restrict, CancellationToken ct = default(CancellationToken));

	Task CloseAsync(CancellationToken ct = default(CancellationToken));
}
