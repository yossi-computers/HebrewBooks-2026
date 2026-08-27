using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HebrewBooks.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace HebrewBooks.Services.Search;

public sealed class PopularitySnapshot
{
	private const string DefaultUrl = "https://hebrewbooks-usage-telemetry.hebrewbooks.workers.dev/popularity";

	private const double Beta = 0.5;

	private static readonly HttpClient Http = new HttpClient
	{
		Timeout = TimeSpan.FromSeconds(30.0)
	};

	private readonly string _path;

	private readonly ILogger<PopularitySnapshot>? _log;

	private readonly ITelemetryConsent? _consent;

	private readonly object _lock = new object();

	private Dictionary<string, double>? _scores;

	private bool Allowed
	{
		get
		{
			if (_consent != null)
			{
				return _consent.IsGranted;
			}
			return true;
		}
	}

	public PopularitySnapshot(IPathResolver paths, ILogger<PopularitySnapshot>? log = null, ITelemetryConsent? consent = null)
	{
		_path = Path.Combine(paths.UserDataRoot, "popularity.json");
		_log = log;
		_consent = consent;
	}

	public double GetScore(string? fileId)
	{
		if (!Allowed || string.IsNullOrEmpty(fileId))
		{
			return 0.0;
		}
		EnsureLoaded();
		lock (_lock)
		{
			double value;
			return _scores.TryGetValue(fileId, out value) ? value : 0.0;
		}
	}

	public double BoostFactor(string? fileId)
	{
		return 1.0 + 0.5 * GetScore(fileId);
	}

	public async Task RefreshAsync(CancellationToken ct = default(CancellationToken))
	{
		if (!Allowed)
		{
			return;
		}
		try
		{
			string requestUri = Environment.GetEnvironmentVariable("HEBREWBOOKS_USAGE_POPULARITY") ?? "https://hebrewbooks-usage-telemetry.hebrewbooks.workers.dev/popularity";
			string text = await Http.GetStringAsync(requestUri, ct).ConfigureAwait(continueOnCapturedContext: false);
			Dictionary<string, double> dictionary = Parse(text);
			if (dictionary == null)
			{
				_log?.LogWarning("Popularity: refresh returned unparseable body");
				return;
			}
			lock (_lock)
			{
				_scores = dictionary;
			}
			_log?.LogInformation("Popularity: refreshed {Count} scores", dictionary.Count);
			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(_path));
				string text2 = _path + ".tmp";
				File.WriteAllText(text2, text);
				if (File.Exists(_path))
				{
					File.Delete(_path);
				}
				File.Move(text2, _path);
			}
			catch (Exception exception)
			{
				_log?.LogWarning(exception, "Popularity: scores applied but caching to {Path} failed", _path);
			}
		}
		catch (Exception exception2)
		{
			_log?.LogWarning(exception2, "Popularity: refresh failed; keeping cached scores");
		}
	}

	private void EnsureLoaded()
	{
		if (_scores != null)
		{
			return;
		}
		lock (_lock)
		{
			if (_scores != null)
			{
				return;
			}
			try
			{
				if (File.Exists(_path))
				{
					_scores = Parse(File.ReadAllText(_path)) ?? new Dictionary<string, double>(StringComparer.Ordinal);
					return;
				}
				string path = Path.Combine(AppContext.BaseDirectory, "popularity.seed.json");
				_scores = (File.Exists(path) ? Parse(File.ReadAllText(path)) : null) ?? new Dictionary<string, double>(StringComparer.Ordinal);
			}
			catch (Exception exception)
			{
				_log?.LogWarning(exception, "Popularity: load failed; starting with no scores");
				_scores = new Dictionary<string, double>(StringComparer.Ordinal);
			}
		}
	}

	private static Dictionary<string, double>? Parse(string raw)
	{
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(raw);
			if (!jsonDocument.RootElement.TryGetProperty("scores", out var value) || value.ValueKind != JsonValueKind.Object)
			{
				return null;
			}
			Dictionary<string, double> dictionary = new Dictionary<string, double>(StringComparer.Ordinal);
			using (JsonElement.ObjectEnumerator objectEnumerator = value.EnumerateObject().GetEnumerator())
			{
				JsonProperty current;
				double num;
				double value3;
				for (; objectEnumerator.MoveNext(); value3 = num, dictionary[current.Name] = value3)
				{
					current = objectEnumerator.Current;
					switch (current.Value.ValueKind)
					{
					case JsonValueKind.Number:
						num = current.Value.GetDouble();
						continue;
					case JsonValueKind.Object:
					{
						if (current.Value.TryGetProperty("score", out var value2) && value2.ValueKind == JsonValueKind.Number)
						{
							num = value2.GetDouble();
							continue;
						}
						break;
					}
					}
					num = 0.0;
				}
			}
			return dictionary;
		}
		catch
		{
			return null;
		}
	}
}
