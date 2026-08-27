using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HebrewBooks.Core.Abstractions;
using HebrewBooks.Core.Models;

namespace HebrewBooks.Services.Background;

public sealed record TargetedIndexBuildJob(IndexSpec Spec, IReadOnlyList<string> ChangedPaths, IReadOnlyList<string> DeletedPaths, ISearchEngine Engine) : BackgroundProcessorService.Job(Guid.NewGuid(), "Indexing " + Spec.IndexPath)
{
	public override Task ExecuteAsync(IProgress<double> progress, CancellationToken ct)
	{
		return ExecuteAsync(progress, null, ct);
	}

	public override Task ExecuteAsync(IProgress<double> progress, IProgress<IndexProgressReport>? detail, CancellationToken ct)
	{
		return Engine.UpdateIndexForFilesAsync(Spec, ChangedPaths, DeletedPaths, progress, detail, ct);
	}
}
