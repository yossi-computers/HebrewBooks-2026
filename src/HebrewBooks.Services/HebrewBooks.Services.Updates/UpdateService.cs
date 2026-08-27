using System;
using System.Net.Http;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HebrewBooks.Core.Abstractions;

namespace HebrewBooks.Services.Updates;

public sealed class UpdateService
{
	public const string DefaultEndpoint = "https://hebrewbooks.org/latest";

	private readonly HttpClient _http;

	private readonly string _endpoint;

	private readonly IProtectMode? _protect;

	public Version CurrentVersion => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);

	public UpdateService(HttpClient http, string endpoint = "https://hebrewbooks.org/latest", IProtectMode? protectMode = null)
	{
		_http = http;
		_endpoint = endpoint;
		_protect = protectMode;
	}

	public async Task<UpdateCheckResult> CheckAsync(CancellationToken ct = default(CancellationToken))
	{
		if (_protect?.IsActive ?? false)
		{
			return new UpdateCheckResult(IsAvailable: false, null, null, null);
		}
		try
		{
			using HttpResponseMessage resp = await _http.GetAsync(_endpoint, ct);
			resp.EnsureSuccessStatusCode();
			return Parse(await resp.Content.ReadAsStringAsync(ct));
		}
		catch (Exception ex)
		{
			return new UpdateCheckResult(IsAvailable: false, null, null, ex.Message);
		}
	}

	public UpdateCheckResult Parse(string body)
	{
		Version version = ExtractVersion(body);
		string downloadUrl = ExtractUrl(body);
		if ((object)version == null)
		{
			return new UpdateCheckResult(IsAvailable: false, null, null, "No version field in response");
		}
		Version currentVersion = CurrentVersion;
		return new UpdateCheckResult(version.CompareTo(currentVersion) > 0, version, downloadUrl, null);
	}

	private static Version? ExtractVersion(string body)
	{
		Match match = Regex.Match(body, "version\\s*[:=]\\s*(?<v>\\d+(?:\\.\\d+){1,3})", RegexOptions.IgnoreCase);
		if (!match.Success || !Version.TryParse(match.Groups["v"].Value, out Version result))
		{
			return null;
		}
		return result;
	}

	private static string? ExtractUrl(string body)
	{
		Match match = Regex.Match(body, "url\\s*[:=]\\s*(?<u>https?://\\S+)", RegexOptions.IgnoreCase);
		if (!match.Success)
		{
			return null;
		}
		return match.Groups["u"].Value;
	}
}
