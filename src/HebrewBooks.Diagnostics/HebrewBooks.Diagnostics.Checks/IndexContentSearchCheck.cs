using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HebrewBooks.Core.Resources;

namespace HebrewBooks.Diagnostics.Checks;

public sealed class IndexContentSearchCheck : IDiagnosticCheck
{
	private readonly Func<DiagnosticContext, CancellationToken, Task<IReadOnlyList<IndexContentProbe>>>? _probe;

	public string Id => "indexcontent";

	public string Category => CoreStrings.C157;

	public IndexContentSearchCheck(Func<DiagnosticContext, CancellationToken, Task<IReadOnlyList<IndexContentProbe>>>? probe = null)
	{
		_probe = probe;
	}

	public async Task<IReadOnlyList<DiagnosticResult>> RunAsync(DiagnosticContext ctx, CancellationToken ct)
	{
		if (_probe == null || ctx.Paths == null)
		{
			return Array.Empty<DiagnosticResult>();
		}
		if (ctx.Options.EffectiveSearchServiceUrl() != null)
		{
			return Array.Empty<DiagnosticResult>();
		}
		IReadOnlyList<IndexContentProbe> readOnlyList;
		try
		{
			readOnlyList = await _probe(ctx, ct).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex2)
		{
			return new DiagnosticResult[1]
			{
				new DiagnosticResult
				{
					Id = "indexcontent.probefail",
					Category = Category,
					Title = CoreStrings.C158,
					Severity = DiagnosticSeverity.Info,
					Detail = CoreStrings.C159,
					Evidence = ex2.ToString()
				}
			};
		}
		List<DiagnosticResult> list = new List<DiagnosticResult>();
		foreach (IndexContentProbe item in readOnlyList)
		{
			if (item.ProbeHits < 0)
			{
				list.Add(new DiagnosticResult
				{
					Id = item.IdBase + ".content.unknown",
					Category = Category,
					Title = CoreStrings.C144 + item.Corpus,
					Severity = DiagnosticSeverity.Info,
					Detail = CoreStrings.C160,
					Evidence = item.IndexPath + "\n" + (item.Error ?? CoreStrings.C161)
				});
			}
			else if (item.IndexedDocs > 0)
			{
				if (item.ProbeHits == 0)
				{
					list.Add(new DiagnosticResult
					{
						Id = item.IdBase + ".content.empty",
						Category = Category,
						Title = CoreStrings.C145 + item.Corpus + CoreStrings.C146,
						Severity = DiagnosticSeverity.Error,
						Detail = $"{CoreStrings.C147}{item.IndexedDocs:N0}{CoreStrings.C148}" + CoreStrings.C162 + CoreStrings.C163 + CoreStrings.C164 + CoreStrings.C165,
						Evidence = $"{item.IndexPath}{CoreStrings.C149}{item.IndexedDocs}{CoreStrings.C150}{item.ProbeHits}",
						Fix = new DiagnosticFix
						{
							Id = item.IdBase + ".build",
							Label = CoreStrings.C151 + item.Corpus + ")",
							Kind = FixKind.AppOnly,
							Run = null
						}
					});
				}
				else
				{
					list.Add(new DiagnosticResult
					{
						Id = item.IdBase + ".content.ok",
						Category = Category,
						Title = CoreStrings.C152 + item.Corpus + CoreStrings.C153,
						Severity = DiagnosticSeverity.Ok,
						Detail = $"{CoreStrings.C154}{item.ProbeHits:N0}{CoreStrings.C155}{item.IndexedDocs:N0}{CoreStrings.C156}",
						Evidence = item.IndexPath
					});
				}
			}
		}
		return list;
	}
}
