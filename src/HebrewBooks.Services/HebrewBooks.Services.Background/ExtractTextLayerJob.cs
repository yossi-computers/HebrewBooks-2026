using System;
using System.Threading;
using System.Threading.Tasks;
using HebrewBooks.Services.TextLayer;

namespace HebrewBooks.Services.Background;

public sealed record ExtractTextLayerJob(int FileId, string? Folder, string OutputPath, TextLayerService Service, TextLayerExtractOptions? Options = null) : BackgroundProcessorService.Job(Guid.NewGuid(), $"Extract text layer (FileID {FileId})")
{
	public TextLayerExtractResult? Result { get; private set; }

	public override async Task ExecuteAsync(IProgress<double> progress, CancellationToken ct)
	{
		Result = await Service.ExtractTextLayerAsync(FileId, Folder, OutputPath, Options, progress, ct).ConfigureAwait(continueOnCapturedContext: false);
	}
}
