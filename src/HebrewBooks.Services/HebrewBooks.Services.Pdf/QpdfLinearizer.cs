using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HebrewBooks.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HebrewBooks.Services.Pdf;

public sealed class QpdfLinearizer : IPdfLinearizer
{
	private readonly ILogger _log;

	private readonly string? _qpdfPath;

	private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(3.0);

	public bool IsAvailable => _qpdfPath != null;

	public QpdfLinearizer(ILogger<QpdfLinearizer>? log = null)
	{
		_log = log ?? NullLogger<QpdfLinearizer>.Instance;
		_qpdfPath = ResolveQpdf();
		if (_qpdfPath == null)
		{
			_log.LogInformation("qpdf not found next to the app — downloaded PDFs will not be linearized.");
		}
	}

	private static string? ResolveQpdf()
	{
		string baseDirectory = AppContext.BaseDirectory;
		string[] array = new string[2] { "qpdf\\qpdf.exe", "qpdf\\bin\\qpdf.exe" };
		foreach (string path in array)
		{
			string text = Path.Combine(baseDirectory, path);
			if (File.Exists(text))
			{
				return text;
			}
		}
		return null;
	}

	public async Task<bool> LinearizeInPlaceAsync(string pdfPath, CancellationToken ct = default(CancellationToken))
	{
		if (_qpdfPath == null || string.IsNullOrEmpty(pdfPath) || !File.Exists(pdfPath))
		{
			return false;
		}
		try
		{
			ProcessStartInfo processStartInfo = new ProcessStartInfo(_qpdfPath)
			{
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardError = true,
				RedirectStandardOutput = true
			};
			processStartInfo.ArgumentList.Add("--linearize");
			processStartInfo.ArgumentList.Add("--warning-exit-0");
			processStartInfo.ArgumentList.Add("--replace-input");
			processStartInfo.ArgumentList.Add(pdfPath);
			using Process proc = Process.Start(processStartInfo);
			if (proc == null)
			{
				return false;
			}
			using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
			timeoutCts.CancelAfter(Timeout);
			try
			{
				await proc.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(continueOnCapturedContext: false);
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
				_log.LogWarning("qpdf linearize {Reason} for {Path}", ct.IsCancellationRequested ? "cancelled" : "timed out", pdfPath);
				return false;
			}
			if (proc.ExitCode == 0)
			{
				return true;
			}
			string text = await proc.StandardError.ReadToEndAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
			_log.LogWarning("qpdf linearize failed (exit {Code}) for {Path}: {Err}", proc.ExitCode, pdfPath, text.Trim());
			return false;
		}
		catch (Exception exception)
		{
			_log.LogWarning(exception, "qpdf linearize error for {Path}", pdfPath);
			return false;
		}
	}
}
