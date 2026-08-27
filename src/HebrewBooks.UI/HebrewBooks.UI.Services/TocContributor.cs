using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using HebrewBooks.Core.Abstractions;
using HebrewBooks.Infrastructure.Settings;
using HebrewBooks.UI.Resources;
using Serilog;

namespace HebrewBooks.UI.Services;

public sealed class TocContributor
{
	private sealed record RelayRequest([property: JsonPropertyName("installId")] string InstallId, [property: JsonPropertyName("fileId")] int FileId, [property: JsonPropertyName("tocJson")] string TocJson);

	private sealed record RelayResponse([property: JsonPropertyName("ok")] bool Ok, [property: JsonPropertyName("url")] string? Url, [property: JsonPropertyName("error")] string? Error);

	private const string DefaultRelayUrl = "https://hebrewbooks-toc-relay.hebrewbooks.workers.dev/contribute";

	private static readonly HttpClient Http = new HttpClient
	{
		Timeout = TimeSpan.FromSeconds(30.0)
	};

	private readonly IProtectMode? _protect;

	private readonly JsonSettingsStore? _settings;

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

	public TocContributor(IProtectMode? protectMode = null, JsonSettingsStore? settings = null)
	{
		_protect = protectMode;
		_settings = settings;
	}

	private string InstallId()
	{
		if (_settings == null)
		{
			return "anon";
		}
		string id = _settings.Load().AnonymousInstallId;
		if (string.IsNullOrEmpty(id))
		{
			id = Guid.NewGuid().ToString();
			_settings.Update(delegate(BookshelfOptions o)
			{
				o.AnonymousInstallId = id;
			});
		}
		return id;
	}

	public async Task<ContributionResult> ContributeAsync(int fileId, string tocJson, CancellationToken ct = default(CancellationToken))
	{
		if (_protect?.IsActive ?? false)
		{
			return new ContributionResult(Success: false, SharedStrings.S599, null);
		}
		if (fileId <= 0 || string.IsNullOrWhiteSpace(tocJson))
		{
			return new ContributionResult(Success: false, SharedStrings.S603, null);
		}
		try
		{
			string requestUri = Environment.GetEnvironmentVariable("HEBREWBOOKS_TOC_RELAY") ?? "https://hebrewbooks-toc-relay.hebrewbooks.workers.dev/contribute";
			using HttpResponseMessage resp = await Http.PostAsJsonAsync(requestUri, new RelayRequest(InstallId(), fileId, tocJson), ct).ConfigureAwait(continueOnCapturedContext: false);
			string json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
			RelayResponse relayResponse = null;
			try
			{
				relayResponse = JsonSerializer.Deserialize<RelayResponse>(json);
			}
			catch
			{
			}
			if (resp.IsSuccessStatusCode && (object)relayResponse != null && relayResponse.Ok)
			{
				return new ContributionResult(Success: true, SharedStrings.S604, null);
			}
			string text = ((!string.IsNullOrWhiteSpace(relayResponse?.Error)) ? relayResponse.Error : $"{SharedStrings.S2022}{resp.StatusCode}).");
			Log.Warning("TocContributor: relay rejected fileId={FileId}: {Status} {Error}", fileId, (int)resp.StatusCode, text);
			return new ContributionResult(Success: false, text, null);
		}
		catch (Exception ex)
		{
			Log.Warning(ex, "TocContributor: contribute failed for fileId={FileId}", fileId);
			return new ContributionResult(Success: false, ex.Message, null);
		}
	}
}
