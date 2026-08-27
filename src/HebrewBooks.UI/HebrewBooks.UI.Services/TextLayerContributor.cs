using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using HebrewBooks.Core.Abstractions;
using HebrewBooks.UI.Resources;
using Serilog;

namespace HebrewBooks.UI.Services;

public sealed class TextLayerContributor
{
	private sealed record RelayRequest([property: JsonPropertyName("fileId")] int FileId, [property: JsonPropertyName("pdfBase64")] string PdfBase64);

	private sealed record RelayResponse([property: JsonPropertyName("ok")] bool Ok, [property: JsonPropertyName("url")] string? Url, [property: JsonPropertyName("error")] string? Error);

	private const string DefaultRelayUrl = "https://hebrewbooks-textlayer-relay.hebrewbooks.workers.dev/contribute";

	private static readonly HttpClient Http = new HttpClient
	{
		Timeout = TimeSpan.FromMinutes(3.0)
	};

	private readonly IProtectMode? _protect;

	public bool IsAvailable
	{
		get
		{
			IProtectMode? protect = _protect;
			if (protect == null)
			{
				return true;
			}
			return !protect.IsActive;
		}
	}

	public TextLayerContributor(IProtectMode? protectMode = null)
	{
		_protect = protectMode;
	}

	public async Task<ContributionResult> ContributeAsync(int fileId, string sidecarPath, CancellationToken ct = default(CancellationToken))
	{
		if (_protect?.IsActive ?? false)
		{
			return new ContributionResult(Success: false, SharedStrings.S599, null);
		}
		if (!File.Exists(sidecarPath))
		{
			return new ContributionResult(Success: false, SharedStrings.S2020 + sidecarPath, null);
		}
		try
		{
			string pdfBase = Convert.ToBase64String(await File.ReadAllBytesAsync(sidecarPath, ct).ConfigureAwait(continueOnCapturedContext: false));
			string requestUri = Environment.GetEnvironmentVariable("HEBREWBOOKS_TEXTLAYER_RELAY") ?? "https://hebrewbooks-textlayer-relay.hebrewbooks.workers.dev/contribute";
			using HttpResponseMessage resp = await Http.PostAsJsonAsync(requestUri, new RelayRequest(fileId, pdfBase), ct).ConfigureAwait(continueOnCapturedContext: false);
			string json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
			RelayResponse relayResponse = null;
			try
			{
				relayResponse = JsonSerializer.Deserialize<RelayResponse>(json);
			}
			catch
			{
			}
			if (resp.IsSuccessStatusCode && (object)relayResponse != null && relayResponse.Ok && !string.IsNullOrEmpty(relayResponse.Url))
			{
				Log.Information("TextLayerContributor: opened PR for fileId={FileId}: {Url}", fileId, relayResponse.Url);
				return new ContributionResult(Success: true, SharedStrings.S601, relayResponse.Url);
			}
			string text = ((!string.IsNullOrWhiteSpace(relayResponse?.Error)) ? relayResponse.Error : $"{SharedStrings.S2021}{resp.StatusCode}).");
			Log.Warning("TextLayerContributor: relay rejected fileId={FileId}: {Status} {Error}", fileId, (int)resp.StatusCode, text);
			return new ContributionResult(Success: false, text, null);
		}
		catch (Exception ex)
		{
			Log.Warning(ex, "TextLayerContributor: contribute failed for fileId={FileId}", fileId);
			return new ContributionResult(Success: false, ex.Message, null);
		}
	}
}
