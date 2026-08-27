using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HebrewBooks.Core.Abstractions;
using HebrewBooks.Core.Models;
using HebrewBooks.Services.Background;
using HebrewBooks.Services.TextLayer;
using Microsoft.Extensions.Logging;

namespace HebrewBooks.Services.Updates;

public sealed class TextLayerUpdateService
{
	public const string DefaultManifestUrl = "https://raw.githubusercontent.com/HebrewBooks-2026/Hebrewbooks-TextLayers/main/index.json";

	private readonly HttpClient _http;

	private readonly TextlayerStatusStore _statusStore;

	private readonly BackgroundProcessorService _bg;

	private readonly TextLayerService _textLayer;

	private readonly ICatalogRepository _catalog;

	private readonly IProtectMode? _protect;

	private readonly string _manifestUrl;

	private readonly ILogger<TextLayerUpdateService>? _log;

	public TextLayerUpdateService(HttpClient http, TextlayerStatusStore statusStore, BackgroundProcessorService bg, TextLayerService textLayer, ICatalogRepository catalog, IProtectMode? protectMode = null, string manifestUrl = "https://raw.githubusercontent.com/HebrewBooks-2026/Hebrewbooks-TextLayers/main/index.json", ILogger<TextLayerUpdateService>? log = null)
	{
		_http = http;
		_statusStore = statusStore;
		_bg = bg;
		_textLayer = textLayer;
		_catalog = catalog;
		_protect = protectMode;
		_manifestUrl = manifestUrl;
		_log = log;
	}

	public async Task<IReadOnlyList<TextLayerManifestEntry>> CheckForUpdatesAsync(CancellationToken ct = default(CancellationToken))
	{
		if (_protect?.IsActive ?? false)
		{
			_log?.LogDebug("TextLayerUpdate: protect-mode active, skipping check");
			return Array.Empty<TextLayerManifestEntry>();
		}
		IReadOnlyList<TextLayerManifestEntry> readOnlyList;
		try
		{
			readOnlyList = await FetchManifestAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception exception)
		{
			_log?.LogWarning(exception, "TextLayerUpdate: failed to fetch manifest from {Url}", _manifestUrl);
			return Array.Empty<TextLayerManifestEntry>();
		}
		List<TextLayerManifestEntry> list = new List<TextLayerManifestEntry>();
		foreach (TextLayerManifestEntry item in readOnlyList)
		{
			TextlayerStatus textlayerStatus = _statusStore.Get(item.FileId);
			if ((object)textlayerStatus == null || !string.Equals(textlayerStatus.SidecarSha256, item.Sha256, StringComparison.OrdinalIgnoreCase))
			{
				list.Add(item);
			}
		}
		_log?.LogInformation("TextLayerUpdate: {Outdated}/{Total} books need update", list.Count, readOnlyList.Count);
		return list;
	}

	public async Task<bool> ApplyOneAsync(TextLayerManifestEntry entry, CancellationToken ct = default(CancellationToken))
	{
		Book book = await _catalog.GetByFileIdAsync(entry.FileId.ToString(), ct).ConfigureAwait(continueOnCapturedContext: false);
		if ((object)book == null)
		{
			_log?.LogWarning("TextLayerUpdate: skipping fileId={FileId} — not in local catalog", entry.FileId);
			return false;
		}
		string text = book.SourceType ?? "PDF";
		if (!string.Equals(text, "PDF", StringComparison.Ordinal))
		{
			_log?.LogWarning("TextLayerUpdate: REFUSING to apply textlayer to fileId={FileId} — SourceType is {Source} (not PDF). This should never happen unless the manifest is corrupt.", entry.FileId, text);
			return false;
		}
		if (!_textLayer.SourcePdfExists(entry.FileId))
		{
			_log?.LogDebug("TextLayerUpdate: skipping fileId={FileId} — source PDF not downloaded yet", entry.FileId);
			return false;
		}
		string tempPath = Path.Combine(Path.GetTempPath(), $"hb-textlayer-dl-{entry.FileId}-{Guid.NewGuid():N}.pdf");
		try
		{
			using (HttpResponseMessage resp = await _http.GetAsync(entry.DownloadUrl, ct).ConfigureAwait(continueOnCapturedContext: false))
			{
				resp.EnsureSuccessStatusCode();
				await using Stream src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
				await using FileStream dst = File.Create(tempPath);
				await src.CopyToAsync(dst, ct).ConfigureAwait(continueOnCapturedContext: false);
			}
			string text2 = TextlayerStatusStore.ComputeSha256(tempPath);
			if (!string.Equals(text2, entry.Sha256, StringComparison.OrdinalIgnoreCase))
			{
				_log?.LogError("TextLayerUpdate: hash mismatch for fileId={FileId}: expected {Expected}, got {Actual}", entry.FileId, entry.Sha256, text2);
				return false;
			}
			await _textLayer.ApplyTextLayerAsync(entry.FileId, null, tempPath, entry.Sha256, "downloaded", null, null, ct).ConfigureAwait(continueOnCapturedContext: false);
			return true;
		}
		catch (Exception exception)
		{
			_log?.LogError(exception, "TextLayerUpdate: ApplyOne failed for fileId={FileId}", entry.FileId);
			return false;
		}
		finally
		{
			if (File.Exists(tempPath))
			{
				try
				{
					File.Delete(tempPath);
				}
				catch
				{
				}
			}
		}
	}

	public async Task<int> RunFullCycleAsync(CancellationToken ct = default(CancellationToken))
	{
		IReadOnlyList<TextLayerManifestEntry> outdated = await CheckForUpdatesAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
		if (outdated.Count == 0)
		{
			return 0;
		}
		int applied = 0;
		foreach (TextLayerManifestEntry item in outdated)
		{
			ct.ThrowIfCancellationRequested();
			if (await ApplyOneAsync(item, ct).ConfigureAwait(continueOnCapturedContext: false))
			{
				applied++;
			}
		}
		_log?.LogInformation("TextLayerUpdate: cycle done, applied {Applied}/{Outdated}", applied, outdated.Count);
		return applied;
	}

	private async Task<IReadOnlyList<TextLayerManifestEntry>> FetchManifestAsync(CancellationToken ct)
	{
		using HttpResponseMessage resp = await _http.GetAsync(_manifestUrl, ct).ConfigureAwait(continueOnCapturedContext: false);
		resp.EnsureSuccessStatusCode();
		IReadOnlyList<TextLayerManifestEntry> result;
		await using (Stream stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(continueOnCapturedContext: false))
		{
			result = (await JsonSerializer.DeserializeAsync<List<TextLayerManifestEntry>>(stream, (JsonSerializerOptions?)null, ct).ConfigureAwait(continueOnCapturedContext: false)) ?? new List<TextLayerManifestEntry>();
		}
		return result;
	}
}
