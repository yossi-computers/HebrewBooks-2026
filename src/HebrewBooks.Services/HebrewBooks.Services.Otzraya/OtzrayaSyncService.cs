using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using HebrewBooks.Core.Abstractions;
using HebrewBooks.Core.Resources;

namespace HebrewBooks.Services.Otzraya;

public sealed class OtzrayaSyncService
{
	public sealed record SyncProgress(int Done, int Total, string CurrentFile);

	public sealed record SyncResult(int Added, int Updated, int Removed, int Skipped, int Errors)
	{
		public IReadOnlyList<string> ChangedPaths { get; init; } = Array.Empty<string>();

		public IReadOnlyList<string> DeletedPaths { get; init; } = Array.Empty<string>();
	}

	private sealed class RawBlockedException : Exception
	{
		public RawBlockedException(string message)
			: base(message)
		{
		}
	}

	internal sealed record SyncManifest([property: JsonPropertyName("format")] string Format, [property: JsonPropertyName("repo")] string Repo, [property: JsonPropertyName("lastSync")] DateTime LastSync, [property: JsonPropertyName("files")] Dictionary<string, string> Files)
	{
		public const string CurrentFormat = "otzraya-sync-v1";

		[CompilerGenerated]
		private SyncManifest(SyncManifest original)
		{
			Format = original.Format;
			Repo = original.Repo;
			LastSync = original.LastSync;
			Files = original.Files;
		}
	}

	private sealed record TreeResponse([property: JsonPropertyName("sha")] string Sha, [property: JsonPropertyName("url")] string Url, [property: JsonPropertyName("tree")] List<TreeEntry> Tree, [property: JsonPropertyName("truncated")] bool Truncated);

	private sealed record TreeEntry([property: JsonPropertyName("path")] string Path, [property: JsonPropertyName("type")] string Type, [property: JsonPropertyName("sha")] string Sha, [property: JsonPropertyName("size")] long? Size);

	public const string DefaultRepoOwner = "HebrewBooks-2026";

	public const string DefaultRepoName = "Otzraya";

	public const string DefaultBranch = "main";

	private const string ManifestFileName = ".sync.json";

	private const int RawBlockSwitchThreshold = 6;

	private const int BulkArchiveThreshold = 300;

	private readonly IPathResolver _paths;

	private readonly HttpClient _http;

	private readonly IProtectMode? _protect;

	private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
		WriteIndented = false
	};

	public OtzrayaSyncService(IPathResolver paths, HttpClient http, IProtectMode? protectMode = null)
	{
		_paths = paths;
		_http = http;
		_protect = protectMode;
		if (!_http.DefaultRequestHeaders.UserAgent.Any())
		{
			_http.DefaultRequestHeaders.UserAgent.ParseAdd("HebrewBooks-Sync/1.0");
		}
		if (!_http.DefaultRequestHeaders.Accept.Any())
		{
			_http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
		}
		string text = Environment.GetEnvironmentVariable("GH_TOKEN") ?? Environment.GetEnvironmentVariable("HEBREWBOOKS_PUBLISH_TOKEN");
		if (!string.IsNullOrWhiteSpace(text) && _http.DefaultRequestHeaders.Authorization == null)
		{
			_http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", text);
		}
	}

	public async Task<SyncResult> SyncAsync(IProgress<SyncProgress>? progress = null, int parallelism = 8, CancellationToken ct = default(CancellationToken), IProgress<string>? status = null)
	{
		if (_protect?.IsActive ?? false)
		{
			return new SyncResult(0, 0, 0, 0, 0);
		}
		string root = _paths.OtzrayaRoot;
		Directory.CreateDirectory(root);
		string url = "https://api.github.com/repos/HebrewBooks-2026/Otzraya/git/trees/main?recursive=1";
		TreeResponse treeResponse;
		using (HttpResponseMessage resp = await GetWithAuthFallbackAsync(url, ct).ConfigureAwait(continueOnCapturedContext: false))
		{
			resp.EnsureSuccessStatusCode();
			treeResponse = (await resp.Content.ReadFromJsonAsync<TreeResponse>(JsonOpts, ct).ConfigureAwait(continueOnCapturedContext: false)) ?? throw new InvalidOperationException("Empty tree response from GitHub.");
		}
		if (treeResponse.Truncated)
		{
			throw new InvalidOperationException("GitHub Tree API returned truncated=true — repo too large for a single recursive fetch. Implement per-folder pagination (not yet supported).");
		}
		SyncManifest manifest = LoadManifest(root);
		Dictionary<string, TreeEntry> remoteBlobs = treeResponse.Tree.Where((TreeEntry e) => e.Type == "blob").ToDictionary<TreeEntry, string, TreeEntry>((TreeEntry e) => NormalisePath(e.Path), (TreeEntry e) => e, StringComparer.Ordinal);
		Dictionary<string, string> manifestFiles = new Dictionary<string, string>(manifest.Files, StringComparer.Ordinal);
		List<TreeEntry> unvouched = remoteBlobs.Values.Where((TreeEntry e) => !ShaMatches(NormalisePath(e.Path), e)).ToList();
		if (unvouched.Count > 0)
		{
			HashSet<string> localSet = new HashSet<string>(StringComparer.Ordinal);
			if (Directory.Exists(root))
			{
				foreach (string item in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
				{
					localSet.Add(NormalisePath(Path.GetRelativePath(root, item)));
				}
			}
			List<TreeEntry> list = unvouched.Where((TreeEntry e) => localSet.Contains(NormalisePath(e.Path))).ToList();
			if (list.Count > 0)
			{
				object reconcileLock = new object();
				await Parallel.ForEachAsync(list, new ParallelOptions
				{
					MaxDegreeOfParallelism = parallelism,
					CancellationToken = ct
				}, delegate(TreeEntry e, CancellationToken _)
				{
					string text2 = NormalisePath(e.Path);
					if (TryGitBlobSha(Path.Combine(root, ToOsPath(text2)), out string sha) && sha == e.Sha)
					{
						lock (reconcileLock)
						{
							manifestFiles[text2] = sha;
						}
					}
					return ValueTask.CompletedTask;
				}).ConfigureAwait(continueOnCapturedContext: false);
			}
		}
		List<TreeEntry> toDownload = new List<TreeEntry>();
		foreach (TreeEntry item2 in unvouched)
		{
			ct.ThrowIfCancellationRequested();
			if (!ShaMatches(NormalisePath(item2.Path), item2))
			{
				toDownload.Add(item2);
			}
		}
		List<string> toDelete = manifest.Files.Keys.Where((string p) => !remoteBlobs.ContainsKey(p)).ToList();
		int totalWork = toDownload.Count + toDelete.Count;
		int done = 0;
		int added = 0;
		int updated = 0;
		int removed = 0;
		List<string> changedAbs = new List<string>();
		List<string> deletedAbs = new List<string>();
		Dictionary<string, string> newManifest = new Dictionary<string, string>(manifestFiles, StringComparer.Ordinal);
		object lockObj = new object();
		int sinceSave = 0;
		CancellationTokenSource rawPhaseCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
		try
		{
			int rawBlockCount = 0;
			bool netfreeDetected = false;
			try
			{
				await Parallel.ForEachAsync(toDownload, new ParallelOptions
				{
					MaxDegreeOfParallelism = parallelism,
					CancellationToken = rawPhaseCts.Token
				}, async delegate(TreeEntry entry, CancellationToken innerCt)
				{
					string path = NormalisePath(entry.Path);
					string localPath = Path.Combine(root, ToOsPath(path));
					try
					{
						string directoryName = Path.GetDirectoryName(localPath);
						if (!string.IsNullOrEmpty(directoryName))
						{
							Directory.CreateDirectory(directoryName);
						}
						await WriteAtomicAsync(localPath, await DownloadRawWithRetryAsync(path, innerCt).ConfigureAwait(continueOnCapturedContext: false), innerCt).ConfigureAwait(continueOnCapturedContext: false);
						RecordWritten(path, entry.Sha);
					}
					catch (RawBlockedException)
					{
						if (Interlocked.Increment(ref rawBlockCount) >= 6)
						{
							netfreeDetected = true;
							try
							{
								rawPhaseCts.Cancel();
								return;
							}
							catch
							{
								return;
							}
						}
					}
					catch (OperationCanceledException)
					{
						throw;
					}
					catch (Exception)
					{
					}
				}).ConfigureAwait(continueOnCapturedContext: false);
			}
			catch (OperationCanceledException) when (!ct.IsCancellationRequested)
			{
			}
			List<TreeEntry> list2 = toDownload.Where((TreeEntry e) => !newManifest.ContainsKey(NormalisePath(e.Path))).ToList();
			if (netfreeDetected && list2.Count > 0)
			{
				if (list2.Count >= 300)
				{
					status?.Report(CoreStrings.C11 + CoreStrings.C12);
					try
					{
						await BulkArchiveAsync(list2).ConfigureAwait(continueOnCapturedContext: false);
					}
					catch (OperationCanceledException)
					{
						throw;
					}
					catch
					{
					}
					list2 = toDownload.Where((TreeEntry e) => !newManifest.ContainsKey(NormalisePath(e.Path))).ToList();
				}
				if (list2.Count > 0)
				{
					if (list2.Count < 300)
					{
						status?.Report(CoreStrings.C13);
					}
					await Parallel.ForEachAsync(list2, new ParallelOptions
					{
						MaxDegreeOfParallelism = Math.Min(parallelism, 4),
						CancellationToken = ct
					}, async delegate(TreeEntry entry, CancellationToken innerCt)
					{
						string path = NormalisePath(entry.Path);
						string localPath = Path.Combine(root, ToOsPath(path));
						try
						{
							string directoryName = Path.GetDirectoryName(localPath);
							if (!string.IsNullOrEmpty(directoryName))
							{
								Directory.CreateDirectory(directoryName);
							}
							await WriteAtomicAsync(localPath, await DownloadBlobRawWithRetryAsync(entry.Sha, innerCt).ConfigureAwait(continueOnCapturedContext: false), innerCt).ConfigureAwait(continueOnCapturedContext: false);
							RecordWritten(path, entry.Sha);
						}
						catch (OperationCanceledException)
						{
							throw;
						}
						catch (Exception)
						{
						}
					}).ConfigureAwait(continueOnCapturedContext: false);
				}
			}
			List<TreeEntry> list3 = toDownload.Where((TreeEntry e) => !newManifest.ContainsKey(NormalisePath(e.Path))).ToList();
			int num = list3.Count;
			foreach (TreeEntry item3 in list3)
			{
				int done2 = Interlocked.Increment(ref done);
				progress?.Report(new SyncProgress(done2, totalWork, NormalisePath(item3.Path)));
			}
			foreach (string item4 in toDelete)
			{
				ct.ThrowIfCancellationRequested();
				string text = Path.Combine(root, ToOsPath(item4));
				try
				{
					if (File.Exists(text))
					{
						File.Delete(text);
					}
					newManifest.Remove(item4);
					deletedAbs.Add(text);
					removed++;
				}
				catch
				{
					num++;
				}
				done++;
				progress?.Report(new SyncProgress(done, totalWork, item4));
			}
			SaveManifest(root, new SyncManifest("otzraya-sync-v1", "HebrewBooks-2026/Otzraya", DateTime.UtcNow, newManifest));
			return new SyncResult(added, updated, removed, manifest.Files.Count - removed, num)
			{
				ChangedPaths = changedAbs,
				DeletedPaths = deletedAbs
			};
		}
		finally
		{
			if (rawPhaseCts != null)
			{
				((IDisposable)rawPhaseCts).Dispose();
			}
		}
		async Task BulkArchiveAsync(IReadOnlyList<TreeEntry> wanted)
		{
			string requestUri = "https://api.github.com/repos/HebrewBooks-2026/Otzraya/zipball/main";
			string tmpZip = Path.Combine(Path.GetTempPath(), $"otzraya-sync-{Guid.NewGuid():N}.zip");
			try
			{
				using (CancellationTokenSource zipCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
				{
					zipCts.CancelAfter(TimeSpan.FromMinutes(60.0));
					using HttpResponseMessage resp2 = await _http.GetAsync(requestUri, HttpCompletionOption.ResponseHeadersRead, zipCts.Token).ConfigureAwait(continueOnCapturedContext: false);
					resp2.EnsureSuccessStatusCode();
					await using Stream netStream = await resp2.Content.ReadAsStreamAsync(zipCts.Token).ConfigureAwait(continueOnCapturedContext: false);
					await using FileStream fileStream = File.Create(tmpZip);
					await netStream.CopyToAsync(fileStream, zipCts.Token).ConfigureAwait(continueOnCapturedContext: false);
				}
				Dictionary<string, TreeEntry> wantedByPath = wanted.ToDictionary<TreeEntry, string, TreeEntry>((TreeEntry e) => NormalisePath(e.Path), (TreeEntry e) => e, StringComparer.Ordinal);
				using ZipArchive archive = ZipFile.OpenRead(tmpZip);
				foreach (ZipArchiveEntry entry in archive.Entries)
				{
					ct.ThrowIfCancellationRequested();
					if (!entry.FullName.EndsWith('/'))
					{
						string rel = StripTopFolder(entry.FullName);
						if (rel.Length != 0 && wantedByPath.TryGetValue(rel, out var te))
						{
							string localPath = Path.Combine(root, ToOsPath(rel));
							string directoryName = Path.GetDirectoryName(localPath);
							if (!string.IsNullOrEmpty(directoryName))
							{
								Directory.CreateDirectory(directoryName);
							}
							string tmp = localPath + ".sync.tmp";
							await using (Stream netStream = entry.Open())
							{
								await using FileStream fileStream = File.Create(tmp);
								await netStream.CopyToAsync(fileStream, ct).ConfigureAwait(continueOnCapturedContext: false);
							}
							if (File.Exists(localPath))
							{
								File.Replace(tmp, localPath, null);
							}
							else
							{
								File.Move(tmp, localPath);
							}
							RecordWritten(rel, te.Sha);
							te = null;
						}
					}
				}
			}
			finally
			{
				try
				{
					if (File.Exists(tmpZip))
					{
						File.Delete(tmpZip);
					}
				}
				catch
				{
				}
			}
		}
		void RecordWritten(string normPath, string sha)
		{
			Dictionary<string, string> dictionary = null;
			lock (lockObj)
			{
				bool num2 = newManifest.ContainsKey(normPath);
				newManifest[normPath] = sha;
				if (num2)
				{
					updated++;
				}
				else
				{
					added++;
				}
				changedAbs.Add(Path.Combine(root, ToOsPath(normPath)));
				if (++sinceSave >= 200)
				{
					sinceSave = 0;
					dictionary = new Dictionary<string, string>(newManifest, StringComparer.Ordinal);
				}
			}
			if (dictionary != null)
			{
				TrySaveManifest(root, dictionary);
			}
			int done3 = Interlocked.Increment(ref done);
			progress?.Report(new SyncProgress(done3, totalWork, normPath));
		}
		bool ShaMatches(string path, TreeEntry entry)
		{
			if (manifestFiles.TryGetValue(path, out string value))
			{
				return value == entry.Sha;
			}
			return false;
		}
	}

	private async Task<HttpResponseMessage> GetWithAuthFallbackAsync(string url, CancellationToken ct)
	{
		HttpResponseMessage httpResponseMessage = await _http.GetAsync(url, ct).ConfigureAwait(continueOnCapturedContext: false);
		if (httpResponseMessage.StatusCode == HttpStatusCode.Unauthorized && _http.DefaultRequestHeaders.Authorization != null)
		{
			httpResponseMessage.Dispose();
			_http.DefaultRequestHeaders.Authorization = null;
			httpResponseMessage = await _http.GetAsync(url, ct).ConfigureAwait(continueOnCapturedContext: false);
		}
		return httpResponseMessage;
	}

	private static bool IsBlockStatus(HttpStatusCode s)
	{
		if (s != (HttpStatusCode)418 && s != HttpStatusCode.Forbidden)
		{
			return s == HttpStatusCode.UnavailableForLegalReasons;
		}
		return true;
	}

	private async Task<byte[]> DownloadRawWithRetryAsync(string normPath, CancellationToken ct)
	{
		string rawUrl = $"https://raw.githubusercontent.com/{"HebrewBooks-2026"}/{"Otzraya"}/{"main"}/{EncodePath(normPath)}";
		int attempt = 1;
		while (true)
		{
			using (CancellationTokenSource attemptCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
			{
				attemptCts.CancelAfter(TimeSpan.FromSeconds(45.0));
				try
				{
					using HttpResponseMessage resp = await _http.GetAsync(rawUrl, attemptCts.Token).ConfigureAwait(continueOnCapturedContext: false);
					if (IsBlockStatus(resp.StatusCode))
					{
						throw new RawBlockedException($"raw blocked HTTP {resp.StatusCode}");
					}
					resp.EnsureSuccessStatusCode();
					return await resp.Content.ReadAsByteArrayAsync(attemptCts.Token).ConfigureAwait(continueOnCapturedContext: false);
				}
				catch (RawBlockedException)
				{
					throw;
				}
				catch (OperationCanceledException) when (ct.IsCancellationRequested)
				{
					throw;
				}
				catch (OperationCanceledException) when (attempt >= 4)
				{
					throw new TimeoutException($"raw download timed out after {4} attempts: {normPath}");
				}
				catch (Exception) when (attempt < 4)
				{
					await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt * attempt), ct).ConfigureAwait(continueOnCapturedContext: false);
				}
			}
			attempt++;
		}
	}

	private static async Task WriteAtomicAsync(string localPath, byte[] bytes, CancellationToken ct)
	{
		string tmp = localPath + ".sync.tmp";
		await File.WriteAllBytesAsync(tmp, bytes, ct).ConfigureAwait(continueOnCapturedContext: false);
		if (File.Exists(localPath))
		{
			File.Replace(tmp, localPath, null);
		}
		else
		{
			File.Move(tmp, localPath);
		}
	}

	private async Task<byte[]> DownloadBlobRawWithRetryAsync(string sha, CancellationToken ct)
	{
		string url = $"https://api.github.com/repos/{"HebrewBooks-2026"}/{"Otzraya"}/git/blobs/{sha}";
		int attempt = 1;
		while (true)
		{
			using (CancellationTokenSource attemptCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
			{
				attemptCts.CancelAfter(TimeSpan.FromSeconds(45.0));
				using HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, url);
				req.Headers.Accept.ParseAdd("application/vnd.github.raw");
				try
				{
					using HttpResponseMessage resp = await _http.SendAsync(req, attemptCts.Token).ConfigureAwait(continueOnCapturedContext: false);
					resp.EnsureSuccessStatusCode();
					return await resp.Content.ReadAsByteArrayAsync(attemptCts.Token).ConfigureAwait(continueOnCapturedContext: false);
				}
				catch (OperationCanceledException) when (ct.IsCancellationRequested)
				{
					throw;
				}
				catch (OperationCanceledException) when (attempt >= 4)
				{
					throw new TimeoutException($"blob download timed out after {4} attempts: {sha}");
				}
				catch (Exception) when (attempt < 4)
				{
					await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt * attempt), ct).ConfigureAwait(continueOnCapturedContext: false);
				}
			}
			attempt++;
		}
	}

	private static bool TryGitBlobSha(string path, out string sha)
	{
		sha = "";
		try
		{
			byte[] array = File.ReadAllBytes(path);
			byte[] bytes = Encoding.ASCII.GetBytes($"blob {array.Length}\0");
			using SHA1 sHA = SHA1.Create();
			sHA.TransformBlock(bytes, 0, bytes.Length, null, 0);
			sHA.TransformFinalBlock(array, 0, array.Length);
			sha = Convert.ToHexString(sHA.Hash).ToLowerInvariant();
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static void TrySaveManifest(string root, Dictionary<string, string> files)
	{
		try
		{
			SaveManifest(root, new SyncManifest("otzraya-sync-v1", "HebrewBooks-2026/Otzraya", DateTime.UtcNow, files));
		}
		catch
		{
		}
	}

	internal static IReadOnlySet<string> LoadManifestPaths(string root)
	{
		return new HashSet<string>(LoadManifest(root).Files.Keys, StringComparer.Ordinal);
	}

	private static SyncManifest LoadManifest(string root)
	{
		string path = Path.Combine(root, ".sync.json");
		if (!File.Exists(path))
		{
			return new SyncManifest("otzraya-sync-v1", "", DateTime.MinValue, new Dictionary<string, string>(StringComparer.Ordinal));
		}
		try
		{
			using FileStream utf8Json = File.OpenRead(path);
			SyncManifest syncManifest = JsonSerializer.Deserialize<SyncManifest>(utf8Json, JsonOpts);
			if ((object)syncManifest == null)
			{
				return new SyncManifest("otzraya-sync-v1", "", DateTime.MinValue, new Dictionary<string, string>(StringComparer.Ordinal));
			}
			return syncManifest with
			{
				Files = new Dictionary<string, string>(syncManifest.Files, StringComparer.Ordinal)
			};
		}
		catch
		{
			return new SyncManifest("otzraya-sync-v1", "", DateTime.MinValue, new Dictionary<string, string>(StringComparer.Ordinal));
		}
	}

	private static void SaveManifest(string root, SyncManifest manifest)
	{
		string text = Path.Combine(root, ".sync.json");
		string text2 = text + ".tmp";
		using (FileStream utf8Json = File.Create(text2))
		{
			JsonSerializer.Serialize(utf8Json, manifest, JsonOpts);
		}
		if (File.Exists(text))
		{
			File.Replace(text2, text, null);
		}
		else
		{
			File.Move(text2, text);
		}
	}

	private static string NormalisePath(string path)
	{
		return path.Replace('\\', '/');
	}

	private static string ToOsPath(string path)
	{
		return path.Replace('/', Path.DirectorySeparatorChar);
	}

	private static string EncodePath(string path)
	{
		return string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
	}

	private static string StripTopFolder(string entryFullName)
	{
		string text = entryFullName.Replace('\\', '/');
		int num = text.IndexOf('/');
		if (num >= 0)
		{
			string text2 = text;
			int num2 = num + 1;
			return text2.Substring(num2, text2.Length - num2);
		}
		return string.Empty;
	}
}
