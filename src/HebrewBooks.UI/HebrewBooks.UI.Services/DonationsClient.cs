using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using HebrewBooks.Core.Abstractions;
using HebrewBooks.Infrastructure.Settings;
using Serilog;

namespace HebrewBooks.UI.Services;

public sealed class DonationsClient
{
	private sealed class ConfigDto
	{
		[JsonPropertyName("donateUrl")]
		public string? DonateUrl { get; set; }

		[JsonPropertyName("dedicationPrice")]
		public double DedicationPrice { get; set; }

		[JsonPropertyName("dedicationCurrency")]
		public string? DedicationCurrency { get; set; }

		[JsonPropertyName("dedicationDays")]
		public int DedicationDays { get; set; }
	}

	private sealed class DedicationDto
	{
		[JsonPropertyName("kind")]
		public string? Kind { get; set; }

		[JsonPropertyName("text")]
		public string? Text { get; set; }

		[JsonPropertyName("donorName")]
		public string? DonorName { get; set; }
	}

	private sealed class ProgressDto
	{
		[JsonPropertyName("configured")]
		public bool Configured { get; set; }

		[JsonPropertyName("goal")]
		public double Goal { get; set; }

		[JsonPropertyName("raised")]
		public double Raised { get; set; }

		[JsonPropertyName("currency")]
		public string? Currency { get; set; }
	}

	private sealed record SubmitDto([property: JsonPropertyName("kind")] string Kind, [property: JsonPropertyName("text")] string Text, [property: JsonPropertyName("donorName")] string? DonorName, [property: JsonPropertyName("contactEmail")] string? ContactEmail, [property: JsonPropertyName("clientId")] string ClientId);

	private static readonly HttpClient Http = new HttpClient
	{
		Timeout = TimeSpan.FromSeconds(20.0)
	};

	private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

	private readonly JsonSettingsStore _settings;

	private readonly IProtectMode? _protect;

	public string? SiteBase => _settings.Load().EffectiveOnlineServiceUrl();

	public DonationsClient(JsonSettingsStore settings, IProtectMode? protectMode = null)
	{
		_settings = settings;
		_protect = protectMode;
	}

	public async Task<DonationConfig?> GetConfigAsync(CancellationToken ct = default(CancellationToken))
	{
		string siteBase = SiteBase;
		if (siteBase == null)
		{
			return null;
		}
		try
		{
			ConfigDto configDto = await Http.GetFromJsonAsync<ConfigDto>(Api(siteBase) + "/config", Json, ct).ConfigureAwait(continueOnCapturedContext: false);
			if (configDto == null)
			{
				return null;
			}
			return new DonationConfig(configDto.DonateUrl ?? string.Empty, configDto.DedicationPrice, string.IsNullOrWhiteSpace(configDto.DedicationCurrency) ? "₪" : configDto.DedicationCurrency, configDto.DedicationDays);
		}
		catch (Exception exception)
		{
			Log.Debug(exception, "DonationsClient: /api/config unavailable");
			return null;
		}
	}

	public async Task<IReadOnlyList<PublicDedication>> GetActiveDedicationsAsync(CancellationToken ct = default(CancellationToken))
	{
		string siteBase = SiteBase;
		if (siteBase == null)
		{
			return Array.Empty<PublicDedication>();
		}
		try
		{
			List<DedicationDto> list = await Http.GetFromJsonAsync<List<DedicationDto>>(Api(siteBase) + "/dedications/active", Json, ct).ConfigureAwait(continueOnCapturedContext: false);
			IReadOnlyList<PublicDedication> result;
			if (list != null)
			{
				IReadOnlyList<PublicDedication> readOnlyList = (from r in list
					where !string.IsNullOrWhiteSpace(r.Text)
					select new PublicDedication(r.Kind ?? "memory", r.Text.Trim(), r.DonorName)).ToList();
				result = readOnlyList;
			}
			else
			{
				IReadOnlyList<PublicDedication> readOnlyList = Array.Empty<PublicDedication>();
				result = readOnlyList;
			}
			return result;
		}
		catch (Exception exception)
		{
			Log.Debug(exception, "DonationsClient: /api/dedications/active unavailable");
			return Array.Empty<PublicDedication>();
		}
	}

	public async Task<DonationProgress?> GetProgressAsync(CancellationToken ct = default(CancellationToken))
	{
		string siteBase = SiteBase;
		if (siteBase == null)
		{
			return null;
		}
		try
		{
			ProgressDto progressDto = await Http.GetFromJsonAsync<ProgressDto>(Api(siteBase) + "/donations/progress", Json, ct).ConfigureAwait(continueOnCapturedContext: false);
			if (progressDto == null || !progressDto.Configured || progressDto.Goal <= 0.0)
			{
				return null;
			}
			return new DonationProgress(progressDto.Goal, progressDto.Raised, string.IsNullOrWhiteSpace(progressDto.Currency) ? "₪" : progressDto.Currency);
		}
		catch (Exception exception)
		{
			Log.Debug(exception, "DonationsClient: /api/donations/progress unavailable");
			return null;
		}
	}

	public async Task<DedicationSubmitResult> SubmitDedicationAsync(string kind, string text, string? donorName, string? contactEmail, CancellationToken ct = default(CancellationToken))
	{
		if (_protect?.IsActive ?? false)
		{
			return DedicationSubmitResult.Fail("kiosk");
		}
		string siteBase = SiteBase;
		if (siteBase == null)
		{
			return DedicationSubmitResult.Fail("offline");
		}
		if (string.IsNullOrWhiteSpace(text))
		{
			return DedicationSubmitResult.Fail("text-required");
		}
		try
		{
			using HttpResponseMessage httpResponseMessage = await Http.PostAsJsonAsync(Api(siteBase) + "/dedications", new SubmitDto(kind, text.Trim(), Trim(donorName), Trim(contactEmail), InstallId()), Json, ct).ConfigureAwait(continueOnCapturedContext: false);
			if (httpResponseMessage.IsSuccessStatusCode)
			{
				return DedicationSubmitResult.Success();
			}
			if (httpResponseMessage.StatusCode == HttpStatusCode.TooManyRequests)
			{
				return DedicationSubmitResult.Fail("rate-limited");
			}
			Log.Warning("DonationsClient: dedication rejected with {Status}", (int)httpResponseMessage.StatusCode);
			return DedicationSubmitResult.Fail($"http-{httpResponseMessage.StatusCode}");
		}
		catch (Exception ex)
		{
			Log.Warning(ex, "DonationsClient: submitting a dedication failed");
			return DedicationSubmitResult.Fail(ex.Message);
		}
	}

	private string InstallId()
	{
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

	private static string Api(string siteBase)
	{
		return siteBase.TrimEnd('/') + "/api";
	}

	private static string? Trim(string? s)
	{
		if (!string.IsNullOrWhiteSpace(s))
		{
			return s.Trim();
		}
		return null;
	}
}
