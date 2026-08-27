using System;
using System.Threading;
using System.Threading.Tasks;
using HebrewBooks.Services.TextLayer;

namespace HebrewBooks.Services.Background;

public sealed record RepairBookOcrJob(int FileId, string? Folder, string SidecarOutputPath, TextLayerService Service, TextLayerExtractOptions? Options = null, IProgress<RepairProgress>? RichProgress = null, string? PersonalRelativePath = null) : BackgroundProcessorService.Job(Guid.NewGuid(), $"Repair book OCR (FileID {FileId})")
{
	public TextLayerExtractResult? ExtractResult { get; private set; }

	public TextLayerApplyResult? ApplyResult { get; private set; }

	public override async Task ExecuteAsync(IProgress<double> progress, CancellationToken ct)
	{
		Progress<double> progress2 = new Progress<double>(delegate(double p)
		{
			progress.Report(p * 0.9);
		});
		ExtractResult = await Service.ExtractTextLayerAsync(FileId, Folder, SidecarOutputPath, Options, progress2, RichProgress, ct, PersonalRelativePath).ConfigureAwait(continueOnCapturedContext: false);
		Progress<double> progress3 = new Progress<double>(delegate(double p)
		{
			progress.Report(0.9 + p * 0.1);
		});
		ApplyResult = await Service.ApplyTextLayerAsync(FileId, Folder, SidecarOutputPath, null, "local-ocr", progress3, RichProgress, ct, PersonalRelativePath).ConfigureAwait(continueOnCapturedContext: false);
	}
}
