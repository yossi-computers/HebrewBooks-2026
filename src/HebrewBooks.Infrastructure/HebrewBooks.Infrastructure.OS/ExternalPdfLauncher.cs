using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace HebrewBooks.Infrastructure.OS;

public static class ExternalPdfLauncher
{
	public static bool TryOpenAtPage(string pdfPath, int page)
	{
		if (page <= 0 || string.IsNullOrEmpty(pdfPath) || !File.Exists(pdfPath))
		{
			return false;
		}
		try
		{
			string text = ResolveDefaultPdfExe();
			if (string.IsNullOrEmpty(text) || !File.Exists(text))
			{
				return false;
			}
			string text2;
			switch (Path.GetFileNameWithoutExtension(text).ToLowerInvariant())
			{
			case "acrord32":
			case "pdfxedit":
			case "acrobat":
			case "foxitpdfeditor":
			case "foxitpdfreader":
			case "foxitreader":
			case "foxitphantompdf":
			case "pdfxcview":
				text2 = $"/A \"page={page}\" \"{pdfPath}\"";
				break;
			case "sumatrapdf":
				text2 = $"-page {page} \"{pdfPath}\"";
				break;
			case "chromium":
			case "vivaldi":
			case "chrome":
			case "msedge":
			case "brave":
				text2 = $"\"{new Uri(pdfPath).AbsoluteUri}#page={page}\"";
				break;
			default:
				text2 = null;
				break;
			}
			string text3 = text2;
			if (text3 == null)
			{
				return false;
			}
			Process.Start(new ProcessStartInfo
			{
				FileName = text,
				Arguments = text3,
				UseShellExecute = false
			});
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static string? ResolveDefaultPdfExe()
	{
		string text;
		using (RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\FileExts\\.pdf\\UserChoice"))
		{
			text = registryKey?.GetValue("ProgId") as string;
		}
		string text2 = null;
		if (!string.IsNullOrEmpty(text))
		{
			using RegistryKey registryKey2 = Registry.ClassesRoot.OpenSubKey(text + "\\shell\\open\\command");
			text2 = registryKey2?.GetValue(null) as string;
		}
		if (string.IsNullOrEmpty(text2))
		{
			using RegistryKey registryKey3 = Registry.ClassesRoot.OpenSubKey(".pdf\\shell\\open\\command");
			text2 = registryKey3?.GetValue(null) as string;
		}
		if (!string.IsNullOrEmpty(text2))
		{
			return ExtractExePath(text2);
		}
		return null;
	}

	private static string? ExtractExePath(string command)
	{
		command = command.Trim();
		if (command.StartsWith("\"", StringComparison.Ordinal))
		{
			int num = command.IndexOf('"', 1);
			if (num <= 1)
			{
				return null;
			}
			return command.Substring(1, num - 1);
		}
		int num2 = command.IndexOf(' ');
		if (num2 <= 0)
		{
			return command;
		}
		return command.Substring(0, num2);
	}
}
