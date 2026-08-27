using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using HebrewBooks.Core.Abstractions;
using Serilog;

namespace HebrewBooks.UI.Services;

public sealed class UsageTelemetryService
{
	private sealed class State
	{
		public string InstallId { get; set; } = "";

		public DateTime? LastSentUtc { get; set; }

		public Dictionary<string, Counter> Pending { get; set; } = new Dictionary<string, Counter>();
	}

	private sealed class Counter
	{
		public int Opens { get; set; }

		public int SearchClicks { get; set; }
	}

	private sealed record CollectRequest([property: JsonPropertyName("installId")] string InstallId, [property: JsonPropertyName("events")] List<EventDto> Events);

	private sealed record EventDto([property: JsonPropertyName("fileId")] string FileId, [property: JsonPropertyName("opens")] int Opens, [property: JsonPropertyName("searchClicks")] int SearchClicks);

	private sealed record CollectResponse([property: JsonPropertyName("ok")] bool Ok, [property: JsonPropertyName("accepted")] int Accepted, [property: JsonPropertyName("error")] string? Error);

	private const string DefaultCollectUrl = "https://hebrewbooks-usage-telemetry.hebrewbooks.workers.dev/collect";

	private const string AppKey = "39764b80d11d420e2ae853c72de3d4d5f6416fb874658080";

	private static readonly TimeSpan MinDwell = TimeSpan.FromSeconds(3.0);

	private static readonly TimeSpan SendInterval = TimeSpan.FromDays(1.0);

	private static readonly HttpClient Http = new HttpClient
	{
		Timeout = TimeSpan.FromSeconds(30.0)
	};

	private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
	{
		WriteIndented = true,
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};

	private readonly string _path;

	private readonly ITelemetryConsent? _consent;

	private readonly object _lock = new object();

	private State? _state;

	private string? _pendingFileId;

	private bool _pendingFromSearch;

	private DateTime _pendingOpenedUtc;

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

	public UsageTelemetryService(IPathResolver paths, ITelemetryConsent? consent = null)
	{
		_path = Path.Combine(paths.UserDataRoot, "usage-telemetry.json");
		_consent = consent;
	}

	public void NoteBookOpened(string? fileId, bool fromSearch)
	{
		if (!Allowed || string.IsNullOrWhiteSpace(fileId))
		{
			return;
		}
		try
		{
			lock (_lock)
			{
				FinalizePendingLocked();
				_pendingFileId = fileId;
				_pendingFromSearch = fromSearch;
				_pendingOpenedUtc = DateTime.UtcNow;
			}
		}
		catch (Exception exception)
		{
			Log.Warning(exception, "UsageTelemetry: NoteBookOpened failed");
		}
	}

	public void FinalizeCurrent()
	{
		if (!Allowed)
		{
			return;
		}
		try
		{
			lock (_lock)
			{
				FinalizePendingLocked();
			}
		}
		catch (Exception exception)
		{
			Log.Warning(exception, "UsageTelemetry: FinalizeCurrent failed");
		}
	}

	private void FinalizePendingLocked()
	{
		if (_pendingFileId == null)
		{
			return;
		}
		string pendingFileId = _pendingFileId;
		bool pendingFromSearch = _pendingFromSearch;
		TimeSpan timeSpan = DateTime.UtcNow - _pendingOpenedUtc;
		_pendingFileId = null;
		if (!(timeSpan < MinDwell))
		{
			EnsureLoadedLocked();
			Dictionary<string, Counter> pending = _state.Pending;
			if (!pending.TryGetValue(pendingFileId, out Counter value))
			{
				value = new Counter();
			}
			value.Opens++;
			if (pendingFromSearch)
			{
				value.SearchClicks++;
			}
			pending[pendingFileId] = value;
			SaveLocked();
		}
	}

	public async Task<bool> SendIfDueAsync(CancellationToken ct = default(CancellationToken))
	{
		if (!Allowed)
		{
			return false;
		}
		string installId;
		List<EventDto> batch;
		lock (_lock)
		{
			EnsureLoadedLocked();
			FinalizePendingLocked();
			DateTime? lastSentUtc = _state.LastSentUtc;
			if (lastSentUtc.HasValue)
			{
				DateTime valueOrDefault = lastSentUtc.GetValueOrDefault();
				if (DateTime.UtcNow - valueOrDefault < SendInterval)
				{
					return false;
				}
			}
			if (_state.Pending.Count == 0)
			{
				return false;
			}
			installId = _state.InstallId;
			batch = _state.Pending.Select<KeyValuePair<string, Counter>, EventDto>((KeyValuePair<string, Counter> kv) => new EventDto(kv.Key, kv.Value.Opens, kv.Value.SearchClicks)).ToList();
		}
		try
		{
			string requestUri = Environment.GetEnvironmentVariable("HEBREWBOOKS_USAGE_COLLECT") ?? "https://hebrewbooks-usage-telemetry.hebrewbooks.workers.dev/collect";
			using HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, requestUri)
			{
				Content = JsonContent.Create(new CollectRequest(installId, batch))
			};
			req.Headers.Add("x-app-key", "39764b80d11d420e2ae853c72de3d4d5f6416fb874658080");
			using HttpResponseMessage resp = await Http.SendAsync(req, ct).ConfigureAwait(continueOnCapturedContext: false);
			string text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
			CollectResponse collectResponse = null;
			try
			{
				collectResponse = JsonSerializer.Deserialize<CollectResponse>(text);
			}
			catch
			{
			}
			if (!resp.IsSuccessStatusCode || (object)collectResponse == null || !collectResponse.Ok)
			{
				Log.Warning("UsageTelemetry: collect rejected ({Status}): {Error}", (int)resp.StatusCode, collectResponse?.Error ?? text);
				return false;
			}
			lock (_lock)
			{
				Dictionary<string, Counter> pending = _state.Pending;
				foreach (EventDto item in batch)
				{
					if (pending.TryGetValue(item.FileId, out var value))
					{
						value.Opens -= item.Opens;
						value.SearchClicks -= item.SearchClicks;
						if (value.Opens <= 0 && value.SearchClicks <= 0)
						{
							pending.Remove(item.FileId);
						}
						else
						{
							pending[item.FileId] = value;
						}
					}
				}
				_state.LastSentUtc = DateTime.UtcNow;
				SaveLocked();
			}
			Log.Information("UsageTelemetry: sent {Count} book deltas", batch.Count);
			return true;
		}
		catch (Exception exception)
		{
			Log.Warning(exception, "UsageTelemetry: send failed");
			return false;
		}
	}

	private void EnsureLoadedLocked()
	{
		if (_state == null)
		{
			_state = Load();
			if (string.IsNullOrEmpty(_state.InstallId))
			{
				_state.InstallId = Guid.NewGuid().ToString();
				SaveLocked();
			}
		}
	}

	private State Load()
	{
		if (!File.Exists(_path))
		{
			return new State();
		}
		try
		{
			return JsonSerializer.Deserialize<State>(File.ReadAllText(_path), Json) ?? new State();
		}
		catch (Exception exception)
		{
			Log.Warning(exception, "UsageTelemetry: failed to load {Path}; starting fresh", _path);
			return new State();
		}
	}

	private void SaveLocked()
	{
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(_path));
			string text = _path + ".tmp";
			File.WriteAllText(text, JsonSerializer.Serialize(_state, Json));
			if (File.Exists(_path))
			{
				File.Delete(_path);
			}
			File.Move(text, _path);
		}
		catch (Exception exception)
		{
			Log.Error(exception, "UsageTelemetry: failed to save {Path}", _path);
		}
	}
}
