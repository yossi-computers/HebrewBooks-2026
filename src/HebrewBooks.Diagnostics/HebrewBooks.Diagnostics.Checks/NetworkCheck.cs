using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HebrewBooks.Core.Resources;

namespace HebrewBooks.Diagnostics.Checks;

public sealed class NetworkCheck : IDiagnosticCheck
{
	public string Id => "network";

	public string Category => CoreStrings.C203;

	public async Task<IReadOnlyList<DiagnosticResult>> RunAsync(DiagnosticContext ctx, CancellationToken ct)
	{
		List<DiagnosticResult> results = new List<DiagnosticResult>();
		if (!ctx.Options.NetworkInstall)
		{
			return results;
		}
		string text = ctx.Options.EffectiveNetworkBase();
		if (!string.IsNullOrWhiteSpace(text))
		{
			bool flag = SafeDirExists(text);
			results.Add(new DiagnosticResult
			{
				Id = "network.base",
				Category = Category,
				Title = (flag ? CoreStrings.C204 : CoreStrings.C205),
				Severity = ((!flag) ? DiagnosticSeverity.Error : DiagnosticSeverity.Ok),
				Detail = (flag ? CoreStrings.C206 : CoreStrings.C207),
				Evidence = text
			});
		}
		string text2 = ctx.Options.EffectiveCatalogMaster();
		if (!string.IsNullOrWhiteSpace(text2))
		{
			bool flag2 = SafeFileExists(text2);
			results.Add(new DiagnosticResult
			{
				Id = "network.master",
				Category = Category,
				Title = (flag2 ? CoreStrings.C208 : CoreStrings.C209),
				Severity = ((!flag2) ? DiagnosticSeverity.Warning : DiagnosticSeverity.Ok),
				Detail = (flag2 ? CoreStrings.C210 : CoreStrings.C211),
				Evidence = text2
			});
		}
		string text3 = ctx.Options.EffectiveSearchServiceUrl();
		if (!string.IsNullOrWhiteSpace(text3))
		{
			List<DiagnosticResult> list = results;
			list.Add(await ProbeServiceAsync(text3, ct).ConfigureAwait(continueOnCapturedContext: false));
		}
		return results;
	}

	private async Task<DiagnosticResult> ProbeServiceAsync(string url, CancellationToken ct)
	{
		try
		{
			using HttpClient http = new HttpClient
			{
				Timeout = TimeSpan.FromSeconds(5.0)
			};
			using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
			cts.CancelAfter(TimeSpan.FromSeconds(6.0));
			using HttpResponseMessage httpResponseMessage = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(continueOnCapturedContext: false);
			return new DiagnosticResult
			{
				Id = "network.service",
				Category = Category,
				Title = CoreStrings.C212,
				Severity = DiagnosticSeverity.Ok,
				Detail = $"{CoreStrings.C201}{httpResponseMessage.StatusCode}{CoreStrings.C202}",
				Evidence = url
			};
		}
		catch (Exception ex)
		{
			return new DiagnosticResult
			{
				Id = "network.service",
				Category = Category,
				Title = CoreStrings.C213,
				Severity = DiagnosticSeverity.Error,
				Detail = CoreStrings.C214 + CoreStrings.C215,
				Evidence = url + "\n" + ex.Message
			};
		}
	}

	private static bool SafeDirExists(string p)
	{
		try
		{
			return Directory.Exists(p);
		}
		catch
		{
			return false;
		}
	}

	private static bool SafeFileExists(string p)
	{
		try
		{
			return File.Exists(p);
		}
		catch
		{
			return false;
		}
	}
}
