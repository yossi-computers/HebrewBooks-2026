using System;
using System.IO;

namespace HebrewBooks.Services.Downloader;

internal static class CurlLocator
{
	public static string Path { get; } = Resolve();

	private static string Resolve()
	{
		string text = System.IO.Path.Combine(AppContext.BaseDirectory, "curl", "curl.exe");
		if (!File.Exists(text))
		{
			return "curl";
		}
		return text;
	}
}
