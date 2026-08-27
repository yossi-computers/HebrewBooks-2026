using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using HebrewBooks.Core.Abstractions;
using Serilog;

namespace HebrewBooks.UI.Services;

public sealed class TocHarvestService
{
	private sealed record BaselineResponse([property: JsonPropertyName("baseline")] Dictionary<string, string>? Baseline);

	private readonly ICatalogRepository _catalog;

	private readonly TocContributor _contributor;

	private readonly IProtectMode? _protect;

	private readonly string _statePath;

	private static readonly TimeSpan Pace = TimeSpan.FromMilliseconds(250.0);

	private const int MaxConsecutiveFailures = 5;

	private const string BaselineUrl = "https://hebrewbooks-toc-relay.hebrewbooks.workers.dev/baseline";

	private static readonly HttpClient Http = new HttpClient
	{
		Timeout = TimeSpan.FromSeconds(30.0)
	};

	public TocHarvestService(ICatalogRepository catalog, TocContributor contributor, IPathResolver paths, IProtectMode? protectMode = null)
	{
		_catalog = catalog;
		_contributor = contributor;
		_protect = protectMode;
		_statePath = Path.Combine(paths.UserDataRoot, "toc-harvest-state.json");
	}

	public async Task HarvestAsync(CancellationToken ct = default(CancellationToken))
	{
		if ((_protect?.IsActive ?? false) || !_contributor.IsAvailable)
		{
			return;
		}
		try
		{
			Dictionary<string, string> sent = LoadState();
			Dictionary<int, string> baseline = await FetchBaselineAsync(ct);
			if (baseline == null)
			{
				Log.Information("TocHarvest: skipped this run — baseline unavailable (relay unreachable). Without it every TOC would look new and the whole catalog would be re-posted.");
				return;
			}
			int uploaded = 0;
			int skippedBaseline = 0;
			int consecutiveFailures = 0;
			foreach (RawTocRow row in await _catalog.GetRawTocsAsync(ct))
			{
				ct.ThrowIfCancellationRequested();
				if (!string.Equals(row.SourceType, "PDF", StringComparison.Ordinal) || !int.TryParse(row.FileId, out var result) || result <= 0)
				{
					continue;
				}
				string hash = Sha256(row.TocJson);
				if (baseline.TryGetValue(result, out string value) && value == hash)
				{
					skippedBaseline++;
				}
				else
				{
					if (sent.TryGetValue(row.FileId, out string value2) && value2 == hash)
					{
						continue;
					}
					ContributionResult contributionResult = await _contributor.ContributeAsync(result, row.TocJson, ct).ConfigureAwait(continueOnCapturedContext: false);
					if (contributionResult.Success)
					{
						sent[row.FileId] = hash;
						uploaded++;
						consecutiveFailures = 0;
						SaveState(sent);
					}
					else
					{
						int num = consecutiveFailures + 1;
						consecutiveFailures = num;
						if (num >= 5)
						{
							Log.Warning("TocHarvest: aborting after {Failures} consecutive relay failures (last: {Message}). The relay looks unreachable from this machine; will retry on a future run.", consecutiveFailures, contributionResult.Message);
							break;
						}
					}
					await Task.Delay(Pace, ct).ConfigureAwait(continueOnCapturedContext: false);
				}
			}
			Log.Information("TocHarvest: uploaded {Uploaded} user-authored TOCs, skipped {Skipped} unchanged-from-baseline", uploaded, skippedBaseline);
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception exception)
		{
			Log.Warning(exception, "TocHarvest failed");
		}
	}

	private Dictionary<string, string> LoadState()
	{
		try
		{
			if (File.Exists(_statePath))
			{
				return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_statePath)) ?? new Dictionary<string, string>();
			}
		}
		catch (Exception exception)
		{
			Log.Warning(exception, "TocHarvest: load state failed");
		}
		return new Dictionary<string, string>();
	}

	private void SaveState(Dictionary<string, string> state)
	{
		try
		{
			string text = _statePath + ".tmp";
			File.WriteAllText(text, JsonSerializer.Serialize(state));
			if (File.Exists(_statePath))
			{
				File.Replace(text, _statePath, null);
			}
			else
			{
				File.Move(text, _statePath);
			}
		}
		catch (Exception exception)
		{
			Log.Warning(exception, "TocHarvest: save state failed");
		}
	}

	private static string Sha256(string s)
	{
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();
	}

	private async Task<Dictionary<int, string>?> FetchBaselineAsync(CancellationToken ct)
	{
		Dictionary<int, string> map = new Dictionary<int, string>();
		try
		{
			string requestUri = Environment.GetEnvironmentVariable("HEBREWBOOKS_TOC_BASELINE") ?? "https://hebrewbooks-toc-relay.hebrewbooks.workers.dev/baseline";
			Dictionary<string, string> dictionary = (await Http.GetFromJsonAsync<BaselineResponse>(requestUri, ct).ConfigureAwait(continueOnCapturedContext: false))?.Baseline;
			if (dictionary != null)
			{
				foreach (KeyValuePair<string, string> item in dictionary)
				{
					if (int.TryParse(item.Key, out var result))
					{
						map[result] = item.Value;
					}
				}
			}
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception exception)
		{
			Log.Warning(exception, "TocHarvest: baseline fetch failed — harvest skipped this run");
			return null;
		}
		return map;
	}
}
