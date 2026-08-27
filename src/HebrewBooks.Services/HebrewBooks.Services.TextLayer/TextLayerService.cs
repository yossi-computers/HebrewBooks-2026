using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HebrewBooks.Core.Abstractions;
using HebrewBooks.Core.Models;
using HebrewBooks.Core.Resources;
using Microsoft.Extensions.Logging;

namespace HebrewBooks.Services.TextLayer;

public sealed class TextLayerService
{
	private readonly IPathResolver _paths;

	private readonly WinOcrCommand _winOcr;

	private readonly ISearchEngine? _searchEngine;

	private readonly TextlayerStatusStore? _statusStore;

	private readonly ILogger<TextLayerService>? _log;

	public TextLayerService(IPathResolver paths, WinOcrCommand winOcr, ISearchEngine? searchEngine = null, TextlayerStatusStore? statusStore = null, ILogger<TextLayerService>? log = null)
	{
		_paths = paths;
		_winOcr = winOcr;
		_searchEngine = searchEngine;
		_statusStore = statusStore;
		_log = log;
	}

	public bool SourcePdfExists(int fileId, string? folder = null)
	{
		try
		{
			return File.Exists(_paths.PdfPath(fileId, folder));
		}
		catch
		{
			return false;
		}
	}

	public bool IsEligible(Book book)
	{
		if ((object)book == null)
		{
			return false;
		}
		if (!string.Equals(book.SourceType ?? "PDF", "PDF", StringComparison.Ordinal))
		{
			return false;
		}
		int result;
		return int.TryParse(book.FileID, out result);
	}

	public Task<TextLayerExtractResult> ExtractTextLayerAsync(int fileId, string? folder, string textLayerOutputPath, TextLayerExtractOptions? options = null, IProgress<double>? progress = null, CancellationToken ct = default(CancellationToken), string? personalRelativePath = null)
	{
		return ExtractTextLayerAsync(fileId, folder, textLayerOutputPath, options, progress, null, ct, personalRelativePath);
	}

	public async Task<TextLayerExtractResult> ExtractTextLayerAsync(int fileId, string? folder, string textLayerOutputPath, TextLayerExtractOptions? options, IProgress<double>? progress, IProgress<RepairProgress>? richProgress, CancellationToken ct = default(CancellationToken), string? personalRelativePath = null)
	{
		TextLayerExtractOptions opts = options ?? new TextLayerExtractOptions();
		string text = (string.IsNullOrEmpty(personalRelativePath) ? _paths.PdfPath(fileId, folder) : _paths.PersonalFilePath(personalRelativePath));
		if (!File.Exists(text))
		{
			throw new FileNotFoundException("Source PDF not found: " + text, text);
		}
		Directory.CreateDirectory(Path.GetDirectoryName(textLayerOutputPath));
		progress?.Report(0.0);
		List<string> list = new List<string>(_winOcr.BaseArgs) { "--input", text, "--output", textLayerOutputPath, "--engine", opts.Engine, "--mode", opts.Mode, "--progress-json" };
		if (opts.RashiCorrect)
		{
			list.Add("--rashi-correct");
		}
		_log?.LogInformation("TextLayerService.Extract: starting fileId={FileId} engine={Engine} mode={Mode} rashi={Rashi} → {Out}", fileId, opts.Engine, opts.Mode, opts.RashiCorrect, textLayerOutputPath);
		Stopwatch sw = Stopwatch.StartNew();
		richProgress?.Report(new RepairProgress("ocr", 0, 0, CoreStrings.C16 + opts.Engine + CoreStrings.C17));
		JsonElement jsonElement = await RunWinOcrAsync(list, richProgress, ct, textLayerOutputPath).ConfigureAwait(continueOnCapturedContext: false);
		sw.Stop();
		long length = new FileInfo(textLayerOutputPath).Length;
		JsonElement value;
		int num = (jsonElement.TryGetProperty("pages_processed", out value) ? value.GetInt32() : 0);
		JsonElement value2;
		int num2 = (jsonElement.TryGetProperty("words", out value2) ? value2.GetInt32() : 0);
		_log?.LogInformation("TextLayerService.Extract: done fileId={FileId} pages={Pages} words={Words} bytes={Bytes} elapsed={Elapsed}", fileId, num, num2, length, sw.Elapsed);
		progress?.Report(1.0);
		return new TextLayerExtractResult(fileId, textLayerOutputPath, num, num2, length, sw.Elapsed, opts.Engine, opts.Mode);
	}

	public Task<TextLayerApplyResult> ApplyTextLayerAsync(int fileId, string? folder, string sidecarPath, IProgress<double>? progress = null, CancellationToken ct = default(CancellationToken), string? personalRelativePath = null)
	{
		return ApplyTextLayerAsync(fileId, folder, sidecarPath, null, "local-ocr", progress, null, ct, personalRelativePath);
	}

	public async Task<TextLayerApplyResult> ApplyTextLayerAsync(int fileId, string? folder, string sidecarPath, string? sidecarSha256, string sourceLabel, IProgress<double>? progress = null, IProgress<RepairProgress>? richProgress = null, CancellationToken ct = default(CancellationToken), string? personalRelativePath = null)
	{
		if (!File.Exists(sidecarPath))
		{
			throw new FileNotFoundException("Sidecar text-only PDF not found: " + sidecarPath, sidecarPath);
		}
		bool isPersonal = !string.IsNullOrEmpty(personalRelativePath);
		string usbPath = (isPersonal ? _paths.PersonalFilePath(personalRelativePath) : _paths.PdfPath(fileId, folder));
		if (!File.Exists(usbPath))
		{
			throw new FileNotFoundException("Source PDF not found: " + usbPath, usbPath);
		}
		string indexPath = (isPersonal ? _paths.PersonalIndexPath : _paths.IndexesRoot);
		string backupPath = (isPersonal ? _paths.BookBackupPath(personalRelativePath) : _paths.BookBackupPath(fileId));
		Directory.CreateDirectory(Path.GetDirectoryName(backupPath));
		progress?.Report(0.0);
		Stopwatch sw = Stopwatch.StartNew();
		bool backupCreated = false;
		if (!File.Exists(backupPath))
		{
			Step("backup", CoreStrings.C18);
			File.Copy(usbPath, backupPath, overwrite: false);
			backupCreated = true;
			_log?.LogInformation("TextLayerService.Apply: backed up {Usb} → {Backup}", usbPath, backupPath);
		}
		else
		{
			_log?.LogInformation("TextLayerService.Apply: backup already exists for fileId={FileId}, skipping", fileId);
		}
		progress?.Report(0.1);
		string tempOut = Path.Combine(Path.GetTempPath(), $"hb-textlayer-apply-{fileId}-{Guid.NewGuid():N}.pdf");
		List<string> args = new List<string>(_winOcr.BaseArgs) { "--input", usbPath, "--output", tempOut, "--inject-text-layer", sidecarPath, "--progress-json" };
		_log?.LogInformation("TextLayerService.Apply: inject-text-layer fileId={FileId} sidecar={Sidecar} → temp={Temp}", fileId, sidecarPath, tempOut);
		try
		{
			Step("inject", CoreStrings.C19);
			await RunWinOcrAsync(args, richProgress, ct, tempOut).ConfigureAwait(continueOnCapturedContext: false);
			progress?.Report(0.7);
			Step("save", CoreStrings.C20);
			File.Delete(usbPath);
			File.Move(tempOut, usbPath);
			tempOut = null;
			_log?.LogInformation("TextLayerService.Apply: overwrote {Usb}", usbPath);
			progress?.Report(0.85);
		}
		finally
		{
			if (tempOut != null && File.Exists(tempOut))
			{
				try
				{
					File.Delete(tempOut);
				}
				catch
				{
				}
			}
		}
		if (_searchEngine != null)
		{
			Step("reindex", CoreStrings.C21);
			await Task.Run(delegate
			{
				Step("reindex", CoreStrings.C22);
				_searchEngine.RemoveDocumentsFromIndex(indexPath, new string[1] { usbPath });
				Step("reindex", CoreStrings.C23);
				_searchEngine.AddDocumentsToIndex(indexPath, new string[1] { usbPath });
			}, ct).ConfigureAwait(continueOnCapturedContext: false);
			Step("reindex", CoreStrings.C24);
			_log?.LogInformation("TextLayerService.Apply: reindexed {Id} in {IndexPath}", isPersonal ? personalRelativePath : fileId.ToString(), indexPath);
		}
		if (_statusStore != null && !isPersonal)
		{
			string sidecarSha257 = sidecarSha256 ?? TextlayerStatusStore.ComputeSha256(sidecarPath);
			int version = (_statusStore.Get(fileId)?.Version ?? 0) + 1;
			_statusStore.Set(new TextlayerStatus(fileId, sidecarSha257, version, sourceLabel, DateTime.UtcNow));
		}
		sw.Stop();
		progress?.Report(1.0);
		return new TextLayerApplyResult(fileId, usbPath, backupPath, backupCreated, new FileInfo(usbPath).Length, sw.Elapsed);
		void Step(string stage, string message)
		{
			_log?.LogInformation("TextLayerService.Apply: {Step}", message);
			richProgress?.Report(new RepairProgress(stage, 0, 0, message));
		}
	}

	private async Task<JsonElement> RunWinOcrAsync(IReadOnlyList<string> args, IProgress<RepairProgress>? richProgress, CancellationToken ct, string? expectedOutput = null)
	{
		ProcessStartInfo processStartInfo = new ProcessStartInfo
		{
			FileName = _winOcr.Executable,
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = true,
			StandardOutputEncoding = Encoding.UTF8,
			StandardErrorEncoding = Encoding.UTF8
		};
		foreach (string arg in args)
		{
			processStartInfo.ArgumentList.Add(arg);
		}
		processStartInfo.Environment["PYTHONIOENCODING"] = "utf-8";
		using Process proc = new Process
		{
			StartInfo = processStartInfo,
			EnableRaisingEvents = true
		};
		StringBuilder stdoutBuf = new StringBuilder();
		StringBuilder stderrBuf = new StringBuilder();
		string lastLogLine = null;
		proc.OutputDataReceived += delegate(object _, DataReceivedEventArgs e)
		{
			if (e.Data != null)
			{
				stdoutBuf.AppendLine(e.Data);
			}
		};
		proc.ErrorDataReceived += delegate(object _, DataReceivedEventArgs e)
		{
			if (e.Data != null)
			{
				if (e.Data.StartsWith("__PROGRESS__ ", StringComparison.Ordinal))
				{
					if (richProgress != null)
					{
						try
						{
							using JsonDocument jsonDocument = JsonDocument.Parse(e.Data.AsSpan("__PROGRESS__ ".Length).ToString());
							JsonElement rootElement = jsonDocument.RootElement;
							JsonElement value;
							string stage = (rootElement.TryGetProperty("stage", out value) ? (value.GetString() ?? "") : "");
							JsonElement value2;
							int current2 = (rootElement.TryGetProperty("current", out value2) ? value2.GetInt32() : 0);
							JsonElement value3;
							int total = (rootElement.TryGetProperty("total", out value3) ? value3.GetInt32() : 0);
							richProgress.Report(new RepairProgress(stage, current2, total, lastLogLine));
						}
						catch (Exception exception)
						{
							_log?.LogDebug(exception, "winocr progress parse failed: {Line}", e.Data);
						}
					}
				}
				else
				{
					stderrBuf.AppendLine(e.Data);
					lastLogLine = e.Data;
					_log?.LogDebug("winocr stderr: {Line}", e.Data);
				}
			}
		};
		if (!proc.Start())
		{
			throw new InvalidOperationException("Failed to start: " + _winOcr.Executable);
		}
		proc.BeginOutputReadLine();
		proc.BeginErrorReadLine();
		try
		{
			await proc.WaitForExitAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (OperationCanceledException)
		{
			try
			{
				proc.Kill(entireProcessTree: true);
			}
			catch
			{
			}
			throw;
		}
		string text = TailLines(stderrBuf.ToString(), 12);
		bool flag = expectedOutput != null && OutputLooksCompletePdf(expectedOutput);
		if (proc.ExitCode != 0)
		{
			if (!flag)
			{
				_log?.LogError("winocr exited {ExitCode}. stderr tail: {Stderr}", proc.ExitCode, text);
				throw new InvalidOperationException($"Win-OCR exited with code {proc.ExitCode}. stderr tail: {text}");
			}
			_log?.LogWarning("winocr exited {ExitCode} but produced a complete PDF at {Out} ({Bytes} bytes) — treating as success. stderr tail: {Stderr}", proc.ExitCode, expectedOutput, new FileInfo(expectedOutput).Length, text);
		}
		string text2 = stdoutBuf.ToString().Trim();
		int num = text2.IndexOf('{');
		if (num < 0)
		{
			if (flag)
			{
				return JsonDocument.Parse("{}").RootElement.Clone();
			}
			throw new InvalidOperationException("Win-OCR produced no JSON summary on stdout. Full output:\n" + text2);
		}
		string text3 = text2;
		int num2 = num;
		return JsonDocument.Parse(text3.Substring(num2, text3.Length - num2)).RootElement.Clone();
	}

	private static bool OutputLooksCompletePdf(string path)
	{
		try
		{
			FileInfo fileInfo = new FileInfo(path);
			if (!fileInfo.Exists || fileInfo.Length < 1024)
			{
				return false;
			}
			using FileStream fileStream = File.OpenRead(path);
			int num = (int)Math.Min(1024L, fileStream.Length);
			fileStream.Seek(-num, SeekOrigin.End);
			byte[] array = new byte[num];
			fileStream.ReadExactly(array, 0, num);
			return Encoding.ASCII.GetString(array).Contains("%%EOF");
		}
		catch
		{
			return false;
		}
	}

	private static string TailLines(string s, int n)
	{
		string[] array = s.Split('\n');
		if (array.Length <= n)
		{
			return s;
		}
		return string.Join("\n", array.Skip(array.Length - n));
	}
}
