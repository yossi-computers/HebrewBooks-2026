using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace HebrewBooks.Infrastructure.OS;

public sealed class FileAssociationService
{
	private const string ProgIdRoot = "HebrewBooks.PdfFile";

	private const int SHCNE_ASSOCCHANGED = 134217728;

	private const int SHCNF_IDLIST = 0;

	[DllImport("shell32.dll")]
	private static extern void SHChangeNotify(int wEventId, uint uFlags, nint dwItem1, nint dwItem2);

	public void Register(string extension, string exePath, string friendlyName)
	{
		if (!extension.StartsWith('.'))
		{
			extension = "." + extension;
		}
		if (string.IsNullOrEmpty(exePath))
		{
			throw new ArgumentException("EXE path required.", "exePath");
		}
		using (RegistryKey registryKey = Registry.CurrentUser.CreateSubKey("Software\\Classes\\HebrewBooks.PdfFile", writable: true))
		{
			registryKey.SetValue("", friendlyName);
			using RegistryKey registryKey2 = registryKey.CreateSubKey("DefaultIcon", writable: true);
			registryKey2.SetValue("", "\"" + exePath + "\",0");
			using RegistryKey registryKey3 = registryKey.CreateSubKey("shell\\open\\command", writable: true);
			registryKey3.SetValue("", "\"" + exePath + "\" \"%1\"");
		}
		using (RegistryKey registryKey4 = Registry.CurrentUser.CreateSubKey("Software\\Classes\\" + extension, writable: true))
		{
			registryKey4.SetValue("", "HebrewBooks.PdfFile");
		}
		SHChangeNotify(134217728, 0u, IntPtr.Zero, IntPtr.Zero);
	}

	public void Unregister(string extension)
	{
		if (!extension.StartsWith('.'))
		{
			extension = "." + extension;
		}
		try
		{
			Registry.CurrentUser.DeleteSubKeyTree("Software\\Classes\\HebrewBooks.PdfFile", throwOnMissingSubKey: false);
		}
		catch
		{
		}
		try
		{
			Registry.CurrentUser.DeleteSubKey("Software\\Classes\\" + extension, throwOnMissingSubKey: false);
		}
		catch
		{
		}
		SHChangeNotify(134217728, 0u, IntPtr.Zero, IntPtr.Zero);
	}

	public bool IsRegistered(string extension)
	{
		if (!extension.StartsWith('.'))
		{
			extension = "." + extension;
		}
		using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Classes\\" + extension);
		return string.Equals(registryKey?.GetValue("") as string, "HebrewBooks.PdfFile", StringComparison.OrdinalIgnoreCase);
	}

	private static string SchemeKeyPath(string scheme)
	{
		return "Software\\Classes\\" + scheme;
	}

	public void RegisterUrlProtocol(string scheme, string exePath, string description)
	{
		if (string.IsNullOrWhiteSpace(scheme))
		{
			throw new ArgumentException("scheme required", "scheme");
		}
		if (string.IsNullOrEmpty(exePath))
		{
			throw new ArgumentException("EXE path required.", "exePath");
		}
		using RegistryKey registryKey = Registry.CurrentUser.CreateSubKey(SchemeKeyPath(scheme), writable: true);
		registryKey.SetValue("", "URL:" + description);
		registryKey.SetValue("URL Protocol", "");
		using (RegistryKey registryKey2 = registryKey.CreateSubKey("DefaultIcon", writable: true))
		{
			registryKey2.SetValue("", "\"" + exePath + "\",0");
		}
		using RegistryKey registryKey3 = registryKey.CreateSubKey("shell\\open\\command", writable: true);
		registryKey3.SetValue("", "\"" + exePath + "\" \"%1\"");
	}

	public void UnregisterUrlProtocol(string scheme)
	{
		try
		{
			Registry.CurrentUser.DeleteSubKeyTree(SchemeKeyPath(scheme), throwOnMissingSubKey: false);
		}
		catch
		{
		}
	}

	public bool IsUrlProtocolRegisteredFor(string scheme, string exePath)
	{
		using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey(SchemeKeyPath(scheme) + "\\shell\\open\\command");
		if (!(registryKey?.GetValue("") is string text))
		{
			return false;
		}
		return text.Contains("\"" + exePath + "\"", StringComparison.OrdinalIgnoreCase);
	}
}
