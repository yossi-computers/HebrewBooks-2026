using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HebrewBooks.Core.Resources;
using HebrewBooks.Diagnostics.Checks;

namespace HebrewBooks.Diagnostics;

public sealed class DiagnosticEngine
{
	private readonly IReadOnlyList<IDiagnosticCheck> _checks;

	public DiagnosticEngine(IReadOnlyList<IDiagnosticCheck> checks)
	{
		_checks = checks;
	}

	public static DiagnosticEngine CreateDefault(IEnumerable<IDiagnosticCheck>? extra = null, Func<CancellationToken, Task<EngineProbeResult>>? engineProbe = null, Func<DiagnosticContext, CancellationToken, Task<IReadOnlyList<IndexContentProbe>>>? contentProbe = null)
	{
		List<IDiagnosticCheck> list = new List<IDiagnosticCheck>
		{
			new DataRootCheck(),
			new EnvironmentCheck(),
			new SettingsCheck(),
			new CatalogCheck(),
			new IndexVsCatalogCheck(),
			new DtSearchEngineCheck(engineProbe),
			new IndexContentSearchCheck(contentProbe),
			new BookFilesCheck(),
			new DiskSpaceCheck(),
			new LogsCheck(),
			new NetworkCheck(),
			new DiskVsIndexCheck()
		};
		if (extra != null)
		{
			list.AddRange(extra);
		}
		return new DiagnosticEngine(list);
	}

	public async Task<DiagnosticReport> RunAsync(DiagnosticContext context, CancellationToken ct = default(CancellationToken), Action<DiagnosticResult>? onResult = null)
	{
		List<DiagnosticResult> results = new List<DiagnosticResult>();
		foreach (IDiagnosticCheck check in _checks)
		{
			ct.ThrowIfCancellationRequested();
			IReadOnlyList<DiagnosticResult> readOnlyList;
			try
			{
				readOnlyList = await check.RunAsync(context, ct).ConfigureAwait(continueOnCapturedContext: false);
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception ex2)
			{
				readOnlyList = new DiagnosticResult[1]
				{
					new DiagnosticResult
					{
						Id = check.Id + ".crashed",
						Category = check.Category,
						Title = CoreStrings.C25,
						Severity = DiagnosticSeverity.Error,
						Detail = CoreStrings.C26,
						Evidence = ex2.ToString()
					}
				};
			}
			results.AddRange(readOnlyList);
			if (onResult == null)
			{
				continue;
			}
			foreach (DiagnosticResult item in readOnlyList)
			{
				onResult(item);
			}
		}
		return new DiagnosticReport
		{
			GeneratedAtUtc = DateTime.UtcNow,
			AppVersion = AppVersion(),
			Os = Environment.OSVersion.VersionString + (Environment.Is64BitOperatingSystem ? " (64-bit OS)" : " (32-bit OS)"),
			Results = results
		};
	}

	private static string AppVersion()
	{
		return Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "?";
	}
}
