using System;
using System.Threading;
using System.Threading.Tasks;
using HebrewBooks.Services.TextLayer;

namespace HebrewBooks.Services.Background;

public sealed record ApplyTextLayerJob(int FileId, string? Folder, string SidecarPath, TextLayerService Service) : BackgroundProcessorService.Job(Guid.NewGuid(), $"Apply text layer (FileID {FileId})")
{
	public TextLayerApplyResult? Result { get; private set; }

	public override async Task ExecuteAsync(IProgress<double> progress, CancellationToken ct)
	{
		Result = await Service.ApplyTextLayerAsync(FileId, Folder, SidecarPath, progress, ct).ConfigureAwait(continueOnCapturedContext: false);
	}
}
