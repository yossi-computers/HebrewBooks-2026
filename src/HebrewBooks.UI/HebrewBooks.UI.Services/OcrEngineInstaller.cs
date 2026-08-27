using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HebrewBooks.Core.Abstractions;
using HebrewBooks.Services.TextLayer;
using HebrewBooks.UI.Resources;
using Serilog;

namespace HebrewBooks.UI.Services;

public sealed class OcrEngineInstaller
{
	public sealed record OcrRelease(string Tag, string AssetUrl, long SizeBytes);

	public const string LatestReleaseApiUrl = "https://api.github.com/repos/HebrewBooks-2026/win-ocr/releases/latest";

	public const string ReleasesPageUrl = "https://github.com/HebrewBooks-2026/win-ocr/releases/latest";

	private const string AssetName = "WinOcr.zip";

	private const string SetupScript = "Setup.cmd";

	private static readonly string[] BundledZipNames = new string[2] { "WinOcr-Offline.zip", "WinOcr.zip" };

	private readonly IProtectMode? _protect;

	public bool IsInstalled => WinOcrCommand.IsEngineInstalled;

	public string? BundledZipPath
	{
		get
		{
			string[] bundledZipNames = BundledZipNames;
			foreach (string path in bundledZipNames)
			{
				string text = Path.Combine(AppContext.BaseDirectory, path);
				if (File.Exists(text))
				{
					return text;
				}
			}
			return null;
		}
	}

	public bool IsBundleAvailable => BundledZipPath != null;

	public long BundledZipSizeBytes
	{
		get
		{
			try
			{
				string bundledZipPath = BundledZipPath;
				return (bundledZipPath == null) ? 0 : new FileInfo(bundledZipPath).Length;
			}
			catch
			{
				return 0L;
			}
		}
	}

	public string? InstalledVersion
	{
		get
		{
			try
			{
				string versionMarkerPath = VersionMarkerPath;
				return File.Exists(versionMarkerPath) ? File.ReadAllText(versionMarkerPath).Trim() : null;
			}
			catch
			{
				return null;
			}
		}
	}

	private static string VersionMarkerPath => Path.Combine(WinOcrCommand.InstallRoot, "hb-engine-version.txt");

	public OcrEngineInstaller(IProtectMode? protectMode = null)
	{
		_protect = protectMode;
	}

	public async Task<OcrRelease?> CheckLatestAsync(CancellationToken ct = default(CancellationToken))
	{
		if (_protect?.IsActive ?? false)
		{
			return null;
		}
		try
		{
			using (HttpClient http = new HttpClient
			{
				Timeout = TimeSpan.FromSeconds(30.0)
			})
			{
				http.DefaultRequestHeaders.UserAgent.ParseAdd("HebrewBooks-OcrInstaller/1.0");
				http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
				using HttpResponseMessage resp = await http.GetAsync("https://api.github.com/repos/HebrewBooks-2026/win-ocr/releases/latest", ct).ConfigureAwait(continueOnCapturedContext: false);
				if (!resp.IsSuccessStatusCode)
				{
					Log.Information("OcrEngineInstaller: release API returned {Status}", (int)resp.StatusCode);
					return null;
				}
				OcrRelease result;
				await using (Stream stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(continueOnCapturedContext: false))
				{
					using JsonDocument jsonDocument = await JsonDocument.ParseAsync(stream, default(JsonDocumentOptions), ct).ConfigureAwait(continueOnCapturedContext: false);
					JsonElement rootElement = jsonDocument.RootElement;
					JsonElement value;
					string text = (rootElement.TryGetProperty("tag_name", out value) ? (value.GetString() ?? "") : "");
					if (!rootElement.TryGetProperty("assets", out var value2) || value2.ValueKind != JsonValueKind.Array)
					{
						result = null;
					}
					else
					{
						foreach (JsonElement item in value2.EnumerateArray())
						{
							if (!string.Equals(item.TryGetProperty("name", out var value3) ? value3.GetString() : null, "WinOcr.zip", StringComparison.OrdinalIgnoreCase))
							{
								continue;
							}
							JsonElement value4;
							string text2 = (item.TryGetProperty("browser_download_url", out value4) ? value4.GetString() : null);
							if (string.IsNullOrEmpty(text2))
							{
								result = null;
							}
							else
							{
								JsonElement value5;
								long sizeBytes = (item.TryGetProperty("size", out value5) ? value5.GetInt64() : 0);
								result = new OcrRelease(text, text2, sizeBytes);
							}
							goto end_IL_026c;
						}
						Log.Information("OcrEngineInstaller: no {Asset} asset on release {Tag}", "WinOcr.zip", text);
						result = null;
					}
					end_IL_026c:;
				}
				return result;
			}
			IL_04b2:
			OcrRelease result2;
			return result2;
		}
		catch (Exception exception)
		{
			Log.Warning(exception, "OcrEngineInstaller: CheckLatest failed");
			return null;
		}
	}

	public async Task InstallAsync(OcrRelease release, IProgress<OcrInstallProgress>? progress, CancellationToken ct = default(CancellationToken))
	{
		if (_protect?.IsActive ?? false)
		{
			throw new InvalidOperationException(SharedStrings.S577);
		}
		string tempZip = Path.Combine(Path.GetTempPath(), $"WinOcr-{Guid.NewGuid():N}.zip");
		try
		{
			progress?.Report(new OcrInstallProgress(OcrInstallPhase.Downloading, 0, SharedStrings.S578));
			await DownloadAsync(release, tempZip, progress, ct).ConfigureAwait(continueOnCapturedContext: false);
			await ExtractAndRunSetupAsync(tempZip, release.Tag, 86, progress, ct).ConfigureAwait(continueOnCapturedContext: false);
			Log.Information("OcrEngineInstaller: installed {Tag} to {Root} (downloaded)", release.Tag, WinOcrCommand.InstallRoot);
		}
		catch (Exception ex)
		{
			progress?.Report(new OcrInstallProgress(OcrInstallPhase.Failed, 0, ex.Message));
			throw;
		}
		finally
		{
			try
			{
				if (File.Exists(tempZip))
				{
					File.Delete(tempZip);
				}
			}
			catch
			{
			}
		}
	}

	public async Task InstallFromBundleAsync(IProgress<OcrInstallProgress>? progress, CancellationToken ct = default(CancellationToken))
	{
		if (_protect?.IsActive ?? false)
		{
			throw new InvalidOperationException(SharedStrings.S577);
		}
		string zip = BundledZipPath ?? throw new FileNotFoundException(SharedStrings.S579);
		try
		{
			progress?.Report(new OcrInstallProgress(OcrInstallPhase.Extracting, 0, SharedStrings.S580));
			await ExtractAndRunSetupAsync(zip, "bundled", 5, progress, ct).ConfigureAwait(continueOnCapturedContext: false);
			Log.Information("OcrEngineInstaller: installed from bundle {Zip} to {Root}", Path.GetFileName(zip), WinOcrCommand.InstallRoot);
		}
		catch (Exception ex)
		{
			progress?.Report(new OcrInstallProgress(OcrInstallPhase.Failed, 0, ex.Message));
			throw;
		}
	}

	private async Task ExtractAndRunSetupAsync(string zipPath, string versionTag, int extractStartPct, IProgress<OcrInstallProgress>? progress, CancellationToken ct)
	{
		string tempDir = Path.Combine(Path.GetTempPath(), $"WinOcr-extract-{Guid.NewGuid():N}");
		try
		{
			progress?.Report(new OcrInstallProgress(OcrInstallPhase.Extracting, extractStartPct, SharedStrings.S581));
			Directory.CreateDirectory(tempDir);
			ZipFile.ExtractToDirectory(zipPath, tempDir, overwriteFiles: true);
			progress?.Report(new OcrInstallProgress(OcrInstallPhase.Extracting, Math.Min(extractStartPct + 4, 90), SharedStrings.S581));
			string? setupPath = FindSetupScript(tempDir) ?? throw new FileNotFoundException("Setup.cmd not found inside " + Path.GetFileName(zipPath) + ".");
			progress?.Report(new OcrInstallProgress(OcrInstallPhase.Installing, Math.Max(extractStartPct + 6, 92), SharedStrings.S582));
			await RunSetupAsync(setupPath, ct).ConfigureAwait(continueOnCapturedContext: false);
			if (!WinOcrCommand.IsEngineInstalled)
			{
				throw new InvalidOperationException($"{SharedStrings.S2009}{Path.GetFileName(WinOcrCommand.InstalledLauncherPath)}{SharedStrings.S2010}{WinOcrCommand.InstallRoot}.");
			}
			TryWriteVersionMarker(versionTag);
			progress?.Report(new OcrInstallProgress(OcrInstallPhase.Done, 100, SharedStrings.S584));
		}
		finally
		{
			try
			{
				if (Directory.Exists(tempDir))
				{
					Directory.Delete(tempDir, recursive: true);
				}
			}
			catch
			{
			}
		}
	}

	private static async Task DownloadAsync(OcrRelease release, string destPath, IProgress<OcrInstallProgress>? progress, CancellationToken ct)
	{
		using HttpClient http = new HttpClient
		{
			Timeout = TimeSpan.FromMinutes(30.0)
		};
		http.DefaultRequestHeaders.UserAgent.ParseAdd("HebrewBooks-OcrInstaller/1.0");
		using HttpResponseMessage resp = await http.GetAsync(release.AssetUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(continueOnCapturedContext: false);
		resp.EnsureSuccessStatusCode();
		long total = resp.Content.Headers.ContentLength ?? release.SizeBytes;
		await using Stream src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
		await using FileStream dst = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, 1048576, useAsync: true);
		byte[] buffer = new byte[1048576];
		long read = 0L;
		int lastPct = -1;
		while (true)
		{
			int num;
			int n = (num = await src.ReadAsync(buffer, ct).ConfigureAwait(continueOnCapturedContext: false));
			if (num <= 0)
			{
				break;
			}
			await dst.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(continueOnCapturedContext: false);
			read += n;
			if (total > 0)
			{
				int num2 = (int)(read * 85 / total);
				if (num2 != lastPct)
				{
					lastPct = num2;
					double value = (double)read / 1024.0 / 1024.0;
					double value2 = (double)total / 1024.0 / 1024.0;
					progress?.Report(new OcrInstallProgress(OcrInstallPhase.Downloading, num2, $"{SharedStrings.S2011}{value:F0}/{value2:F0}MB"));
				}
			}
		}
	}

	private static string? FindSetupScript(string extractRoot)
	{
		string text = Path.Combine(extractRoot, "Setup.cmd");
		if (File.Exists(text))
		{
			return text;
		}
		return (from p in Directory.EnumerateFiles(extractRoot, "Setup.cmd", SearchOption.AllDirectories)
			orderby p.Count((char c) => c == Path.DirectorySeparatorChar)
			select p).FirstOrDefault();
	}

	private static async Task RunSetupAsync(string setupPath, CancellationToken ct)
	{
		ProcessStartInfo startInfo = new ProcessStartInfo
		{
			FileName = "cmd.exe",
			Arguments = "/c \"" + setupPath + "\"",
			WorkingDirectory = Path.GetDirectoryName(setupPath),
			UseShellExecute = false,
			CreateNoWindow = false
		};
		using Process proc = new Process
		{
			StartInfo = startInfo,
			EnableRaisingEvents = true
		};
		if (!proc.Start())
		{
			throw new InvalidOperationException("Failed to start Setup.cmd.");
		}
		await proc.WaitForExitAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
		if (proc.ExitCode != 0)
		{
			throw new InvalidOperationException($"{"Setup.cmd"}{SharedStrings.S2012}{proc.ExitCode}).");
		}
	}

	private static void TryWriteVersionMarker(string tag)
	{
		try
		{
			Directory.CreateDirectory(WinOcrCommand.InstallRoot);
			File.WriteAllText(VersionMarkerPath, tag);
		}
		catch (Exception ex)
		{
			Log.Information("OcrEngineInstaller: could not write version marker: {Message}", ex.Message);
		}
	}
}
