using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using Serilog;

namespace HebrewBooks.UI.Services;

public static class WebViewEnvironment
{
	private static Task<CoreWebView2Environment?>? _envTask;

	private static readonly object _lock = new object();

	public static Task<CoreWebView2Environment?> GetAsync()
	{
		lock (_lock)
		{
			if (_envTask == null)
			{
				_envTask = CreateAsync();
			}
			return _envTask;
		}
	}

	private static async Task<CoreWebView2Environment?> CreateAsync()
	{
		try
		{
			string text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HebrewBooks", "WebView2Data");
			Directory.CreateDirectory(text);
			CoreWebView2EnvironmentOptions options = new CoreWebView2EnvironmentOptions
			{
				AdditionalBrowserArguments = "--js-flags=\"--max-old-space-size=384 --expose-gc\" --renderer-process-limit=2 --disable-features=RendererCodeIntegrity"
			};
			return await CoreWebView2Environment.CreateAsync(null, text, options).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception exception)
		{
			Log.Warning(exception, "WebViewEnvironment.CreateAsync failed; falling back to default");
			return null;
		}
	}
}
