using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HebrewBooks.Core.Resources;
using dtSearch.Engine;

namespace HebrewBooks.Diagnostics.Checks;

public sealed class DtSearchEngineCheck : IDiagnosticCheck
{
	private readonly Func<CancellationToken, Task<EngineProbeResult>>? _probe;

	public string Id => "engine";

	public string Category => CoreStrings.C126;

	public DtSearchEngineCheck(Func<CancellationToken, Task<EngineProbeResult>>? probe = null)
	{
		_probe = probe;
	}

	public async Task<IReadOnlyList<DiagnosticResult>> RunAsync(DiagnosticContext ctx, CancellationToken ct)
	{
		EngineProbeResult engineProbeResult = ((_probe == null) ? (await ProbeInProcessAsync(ctx, ct).ConfigureAwait(continueOnCapturedContext: false)) : (await _probe(ct).ConfigureAwait(continueOnCapturedContext: false)));
		EngineProbeResult engineProbeResult2 = engineProbeResult;
		if (engineProbeResult2.Loaded)
		{
			return new DiagnosticResult[1]
			{
				new DiagnosticResult
				{
					Id = "engine.ok",
					Category = Category,
					Title = CoreStrings.C127,
					Severity = DiagnosticSeverity.Ok,
					Detail = (engineProbeResult2.Detail ?? CoreStrings.C128)
				}
			};
		}
		if (ctx.Options.EffectiveSearchServiceUrl() != null)
		{
			return new DiagnosticResult[1]
			{
				new DiagnosticResult
				{
					Id = "engine.loadfail.remote",
					Category = Category,
					Title = CoreStrings.C129,
					Severity = DiagnosticSeverity.Info,
					Detail = CoreStrings.C130,
					Evidence = engineProbeResult2.Error
				}
			};
		}
		return new DiagnosticResult[1]
		{
			new DiagnosticResult
			{
				Id = "engine.loadfail",
				Category = Category,
				Title = CoreStrings.C131,
				Severity = DiagnosticSeverity.Error,
				Detail = CoreStrings.C132 + CoreStrings.C133 + CoreStrings.C134,
				Evidence = engineProbeResult2.Error
			}
		};
	}

	private static async Task<EngineProbeResult> ProbeInProcessAsync(DiagnosticContext ctx, CancellationToken ct)
	{
		try
		{
			using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
			cts.CancelAfter(TimeSpan.FromSeconds(20.0));
			await Task.Run(delegate
			{
				((IDisposable)new Server())?.Dispose();
			}, cts.Token).ConfigureAwait(continueOnCapturedContext: false);
			return new EngineProbeResult(Loaded: true, CoreStrings.C135, null);
		}
		catch (Exception ex)
		{
			return new EngineProbeResult(Loaded: false, null, ex.ToString());
		}
	}
}
