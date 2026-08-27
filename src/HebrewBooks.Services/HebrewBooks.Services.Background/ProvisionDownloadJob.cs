using System;
using System.Threading;
using System.Threading.Tasks;
using HebrewBooks.Core.Abstractions;
using HebrewBooks.Core.Models;
using HebrewBooks.Core.Resources;
using HebrewBooks.Services.Provisioning;

namespace HebrewBooks.Services.Background;

public sealed record ProvisionDownloadJob(string Root, ProvisionPlan Plan, ProvisioningService Provisioner, string InstallType, ISearchEngine? Engine = null, IndexSpec? LocalIndexSpec = null) : BackgroundProcessorService.Job(Guid.NewGuid(), CoreStrings.C1)
{
	public override async Task ExecuteAsync(IProgress<double> progress, CancellationToken ct)
	{
		if (Provisioner.LooksAlreadyProvisioned(Root, Plan))
		{
			Provisioner.MarkProvisioned(Root, InstallType, Plan.BuildIndexLocally);
			progress.Report(1.0);
			return;
		}
		Progress<(long, long)> progress2 = new Progress<(long, long)>(delegate((long Bytes, long Total) t)
		{
			progress.Report((t.Total > 0) ? Math.Min(1.0, (double)t.Bytes / (double)t.Total) : 0.0);
		});
		await Provisioner.RunBackgroundAsync(Root, Plan, progress2, ct).ConfigureAwait(continueOnCapturedContext: false);
		if (Plan.BuildIndexLocally && Engine != null && (object)LocalIndexSpec != null)
		{
			await Engine.BuildIndexAsync(LocalIndexSpec, progress, null, ct).ConfigureAwait(continueOnCapturedContext: false);
		}
		Provisioner.MarkProvisioned(Root, InstallType, Plan.BuildIndexLocally);
	}
}
