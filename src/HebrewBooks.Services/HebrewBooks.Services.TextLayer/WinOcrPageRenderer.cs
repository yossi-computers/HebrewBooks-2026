using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace HebrewBooks.Services.TextLayer;

public sealed class WinOcrPageRenderer
{
	private readonly WinOcrCommand _winOcr;

	private readonly ILogger<WinOcrPageRenderer>? _log;

	public WinOcrPageRenderer(WinOcrCommand winOcr, ILogger<WinOcrPageRenderer>? log = null)
	{
		_winOcr = winOcr;
		_log = log;
	}

	public async Task<byte[]?> RenderPagePngAsync(string pdfPath, int pageIndex, double dpi = 96.0, CancellationToken ct = default(CancellationToken))
	{
		_ = 4;
		try
		{
			ProcessStartInfo processStartInfo = new ProcessStartInfo
			{
				FileName = _winOcr.Executable,
				UseShellExecute = false,
				RedirectStandardInput = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				CreateNoWindow = true,
				StandardInputEncoding = Encoding.UTF8,
				StandardOutputEncoding = Encoding.UTF8,
				StandardErrorEncoding = Encoding.UTF8
			};
			foreach (string baseArg in _winOcr.BaseArgs)
			{
				processStartInfo.ArgumentList.Add(baseArg);
			}
			processStartInfo.Environment["PYTHONIOENCODING"] = "utf-8";
			using Process proc = new Process
			{
				StartInfo = processStartInfo
			};
			if (!proc.Start())
			{
				return null;
			}
			string value = JsonSerializer.Serialize(new
			{
				id = "open",
				cmd = "open_pdf",
				args = new
				{
					path = pdfPath
				}
			});
			string renderReq = JsonSerializer.Serialize(new
			{
				id = "render",
				cmd = "render_page",
				args = new
				{
					page = pageIndex,
					dpi = dpi
				}
			});
			await proc.StandardInput.WriteLineAsync(value).ConfigureAwait(continueOnCapturedContext: false);
			await proc.StandardInput.WriteLineAsync(renderReq).ConfigureAwait(continueOnCapturedContext: false);
			proc.StandardInput.Close();
			Task<string> stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
			await proc.WaitForExitAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
			string[] array = (await stdoutTask.ConfigureAwait(continueOnCapturedContext: false)).Split('\n');
			for (int i = 0; i < array.Length; i++)
			{
				string text = array[i].Trim();
				if (string.IsNullOrEmpty(text))
				{
					continue;
				}
				try
				{
					using JsonDocument doc = JsonDocument.Parse(text);
					JsonElement rootElement = doc.RootElement;
					if (!rootElement.TryGetProperty("id", out var value2) || value2.GetString() != "render" || !rootElement.TryGetProperty("result", out var value3))
					{
						continue;
					}
					if (value3.TryGetProperty("png_base64", out var value4))
					{
						return Convert.FromBase64String(value4.GetString() ?? "");
					}
					if (value3.TryGetProperty("image_path", out var value5))
					{
						string text2 = value5.GetString();
						if (!string.IsNullOrEmpty(text2) && File.Exists(text2))
						{
							return await File.ReadAllBytesAsync(text2, ct).ConfigureAwait(continueOnCapturedContext: false);
						}
					}
				}
				catch (JsonException)
				{
				}
			}
			_log?.LogDebug("WinOcrPageRenderer: no render response found in stdout");
			return null;
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception exception)
		{
			_log?.LogDebug(exception, "WinOcrPageRenderer.RenderPagePngAsync failed");
			return null;
		}
	}
}
