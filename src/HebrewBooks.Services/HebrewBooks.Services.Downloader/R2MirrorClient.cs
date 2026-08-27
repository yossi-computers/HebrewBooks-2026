using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace HebrewBooks.Services.Downloader;

public sealed class R2MirrorClient
{
	private const string Endpoint = "https://8967109e6a6ebbd655b57a01a4952686.r2.cloudflarestorage.com";

	private const string Bucket = "hebrewbooks-2026";

	private const string AccessKey = "13a954df5a0cf255b9894a45d84bf2db";

	private const string SecretKey = "97b35f0cc3830cda6488a86c80a686487549b4625db733db8008f18fd1020886";

	public const string AppPrefix = "HebrewBooks/App";

	public const string IndexPrefix = "HebrewBooks/Bookshelf_IDX";

	public const string BooksPrefix = "HebrewBooks/books";

	public const int DefaultParallelism = 16;

	private static readonly Regex ContentsPattern = new Regex("<Contents>.*?</Contents>", RegexOptions.Compiled | RegexOptions.Singleline);

	public async Task DownloadKeyAsync(string key, string destPath, string? expectedETag = null, CancellationToken ct = default(CancellationToken))
	{
		Directory.CreateDirectory(Path.GetDirectoryName(destPath));
		ProcessStartInfo processStartInfo = NewSignedPsi($"{"https://8967109e6a6ebbd655b57a01a4952686.r2.cloudflarestorage.com"}/{"hebrewbooks-2026"}/{key}", redirectStdout: false);
		processStartInfo.ArgumentList.Add("-o");
		processStartInfo.ArgumentList.Add(destPath);
		try
		{
			using Process proc = Process.Start(processStartInfo) ?? throw new InvalidOperationException("Failed to start curl.");
			string stderr = await proc.StandardError.ReadToEndAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
			await proc.WaitForExitAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
			if (proc.ExitCode != 0)
			{
				string text = stderr.Trim();
				if (text.Length > 200)
				{
					text = text.Substring(0, 200) + "...";
				}
				throw new InvalidOperationException($"curl exit {proc.ExitCode}: {text}");
			}
			string md5 = SinglePartMd5(expectedETag);
			if (md5 != null)
			{
				string text2 = await ComputeMd5HexAsync(destPath, ct).ConfigureAwait(continueOnCapturedContext: false);
				if (!string.Equals(text2, md5, StringComparison.OrdinalIgnoreCase))
				{
					throw new InvalidOperationException($"integrity check failed for '{key}': expected MD5 {md5}, got {text2} " + "(download corrupted in transit — often a filtering proxy).");
				}
			}
		}
		catch
		{
			try
			{
				if (File.Exists(destPath))
				{
					File.Delete(destPath);
				}
			}
			catch
			{
			}
			throw;
		}
	}

	public async Task<IReadOnlyList<(string Key, long Size, string ETag)>> ListPrefixAsync(string prefix, IProgress<int>? progress = null, CancellationToken ct = default(CancellationToken))
	{
		List<(string, long, string)> items = new List<(string, long, string)>();
		string text = null;
		do
		{
			ct.ThrowIfCancellationRequested();
			string text2 = $"{"https://8967109e6a6ebbd655b57a01a4952686.r2.cloudflarestorage.com"}/{"hebrewbooks-2026"}?list-type=2&prefix={Uri.EscapeDataString(prefix)}&max-keys=1000";
			if (!string.IsNullOrEmpty(text))
			{
				text2 = text2 + "&continuation-token=" + Uri.EscapeDataString(text);
			}
			string text3 = await GetStringAsync(text2, ct).ConfigureAwait(continueOnCapturedContext: false);
			foreach (Match item2 in ContentsPattern.Matches(text3))
			{
				string value = item2.Value;
				string text4 = ExtractXmlTag(value, "Key");
				if (!string.IsNullOrEmpty(text4))
				{
					long.TryParse(ExtractXmlTag(value, "Size") ?? "0", out var result);
					string item = (ExtractXmlTag(value, "ETag") ?? "").Replace("&quot;", "").Replace("\"", "").Trim();
					items.Add((text4, result, item));
				}
			}
			progress?.Report(items.Count);
			text = ExtractXmlTag(text3, "NextContinuationToken");
		}
		while (!string.IsNullOrEmpty(text));
		return items;
	}

	public async Task<long> SumPrefixBytesAsync(string prefix, CancellationToken ct = default(CancellationToken))
	{
		IReadOnlyList<(string, long, string)> obj = await ListPrefixAsync(prefix, null, ct).ConfigureAwait(continueOnCapturedContext: false);
		long num = 0L;
		foreach (var item2 in obj)
		{
			long item = item2.Item2;
			num += item;
		}
		return num;
	}

	public async Task DownloadPrefixAsync(string prefix, string destRoot, string stripPrefix, int parallelism, bool verifyHash = false, IProgress<(long Bytes, long Total, int Files, int TotalFiles)>? progress = null, CancellationToken ct = default(CancellationToken))
	{
		List<(string, long, string)> list = (await ListPrefixAsync(prefix, null, ct).ConfigureAwait(continueOnCapturedContext: false)).Where<(string, long, string)>(((string Key, long Size, string ETag) i) => !i.Key.EndsWith('/')).ToList();
		long total = list.Sum<(string, long, string)>(((string Key, long Size, string ETag) i) => i.Size);
		int totalFiles = list.Count;
		long bytes = 0L;
		int files = 0;
		SemaphoreSlim gate = new SemaphoreSlim((parallelism <= 0) ? 16 : parallelism);
		try
		{
			await Task.WhenAll(list.Select<(string, long, string), Task>(async delegate((string Key, long Size, string ETag) item)
			{
				await gate.WaitAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
				try
				{
					ct.ThrowIfCancellationRequested();
					string text;
					(text, _, _) = item;
					if (!string.IsNullOrEmpty(stripPrefix) && text.StartsWith(stripPrefix, StringComparison.Ordinal))
					{
						string text2 = text;
						int length = stripPrefix.Length;
						text = text2.Substring(length, text2.Length - length);
					}
					text = text.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
					string dest = Path.Combine(destRoot, text);
					string expected = (verifyHash ? item.ETag : null);
					bool haveGood = File.Exists(dest) && new FileInfo(dest).Length == item.Size;
					string md5 = default(string);
					int num;
					if (haveGood)
					{
						md5 = SinglePartMd5(expected);
						num = ((md5 != null) ? 1 : 0);
					}
					else
					{
						num = 0;
					}
					bool flag = (byte)num != 0;
					if (flag)
					{
						flag = !string.Equals(await ComputeMd5HexAsync(dest, ct).ConfigureAwait(continueOnCapturedContext: false), md5, StringComparison.OrdinalIgnoreCase);
					}
					if (flag)
					{
						haveGood = false;
					}
					if (!haveGood)
					{
						await DownloadWithRetryAsync(item.Key, dest, expected, (!verifyHash) ? 1 : 3, ct).ConfigureAwait(continueOnCapturedContext: false);
					}
				}
				finally
				{
					gate.Release();
				}
				long item2 = Interlocked.Add(ref bytes, item.Size);
				int item3 = Interlocked.Increment(ref files);
				progress?.Report((item2, total, item3, totalFiles));
			})).ConfigureAwait(continueOnCapturedContext: false);
		}
		finally
		{
			if (gate != null)
			{
				((IDisposable)gate).Dispose();
			}
		}
	}

	private async Task DownloadWithRetryAsync(string key, string dest, string? expectedETag, int attempts, CancellationToken ct)
	{
		int attempt = 1;
		while (true)
		{
			try
			{
				await DownloadKeyAsync(key, dest, expectedETag, ct).ConfigureAwait(continueOnCapturedContext: false);
				break;
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch when (attempt < attempts)
			{
			}
			attempt++;
		}
	}

	private static async Task<string> GetStringAsync(string url, CancellationToken ct)
	{
		ProcessStartInfo startInfo = NewSignedPsi(url, redirectStdout: true);
		using Process proc = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start curl.");
		using MemoryStream ms = new MemoryStream();
		Task copyTask = proc.StandardOutput.BaseStream.CopyToAsync(ms, ct);
		string stderr = await proc.StandardError.ReadToEndAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
		await copyTask.ConfigureAwait(continueOnCapturedContext: false);
		await proc.WaitForExitAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
		if (proc.ExitCode != 0)
		{
			throw new InvalidOperationException($"curl exit {proc.ExitCode}: {stderr.Trim()}");
		}
		return Encoding.UTF8.GetString(ms.ToArray());
	}

	private static ProcessStartInfo NewSignedPsi(string url, bool redirectStdout)
	{
		return new ProcessStartInfo
		{
			FileName = CurlLocator.Path,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true,
			StandardOutputEncoding = Encoding.UTF8,
			StandardErrorEncoding = Encoding.UTF8,
			ArgumentList = 
			{
				"--fail", "-sL", "--ssl-no-revoke", "--connect-timeout", "30", "--max-time", "3600", "--aws-sigv4", "aws:amz:auto:s3", "--user",
				"13a954df5a0cf255b9894a45d84bf2db:97b35f0cc3830cda6488a86c80a686487549b4625db733db8008f18fd1020886", url
			}
		};
	}

	private static string? SinglePartMd5(string? etag)
	{
		if (string.IsNullOrEmpty(etag) || etag.Contains('-'))
		{
			return null;
		}
		if (etag.Length != 32 || !etag.All(Uri.IsHexDigit))
		{
			return null;
		}
		return etag;
	}

	private static async Task<string> ComputeMd5HexAsync(string path, CancellationToken ct)
	{
		string result;
		await using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1048576, useAsync: true))
		{
			using MD5 md5 = MD5.Create();
			result = Convert.ToHexString(await md5.ComputeHashAsync(fs, ct).ConfigureAwait(continueOnCapturedContext: false)).ToLowerInvariant();
		}
		return result;
	}

	private static string? ExtractXmlTag(string xml, string tag)
	{
		string text = "<" + tag + ">";
		int num = xml.IndexOf(text, StringComparison.Ordinal);
		if (num < 0)
		{
			return null;
		}
		num += text.Length;
		int num2 = xml.IndexOf("</" + tag + ">", num, StringComparison.Ordinal);
		if (num2 >= 0)
		{
			return xml.Substring(num, num2 - num);
		}
		return null;
	}
}
