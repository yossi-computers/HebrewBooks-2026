using System;
using System.Collections.Generic;
using System.IO;
using HebrewBooks.Core.Abstractions;

namespace HebrewBooks.Core;

public sealed class ProtectMode : IProtectMode
{
	private readonly Func<bool>? _isOnlineService;

	public const string MarkerFileName = "protect-mode.flag";

	public bool IsActive { get; }

	public bool AllowsBookFetch
	{
		get
		{
			if (IsActive)
			{
				return _isOnlineService?.Invoke() ?? false;
			}
			return true;
		}
	}

	public ProtectMode(bool isActive, Func<bool>? isOnlineService = null)
	{
		IsActive = isActive;
		_isOnlineService = isOnlineService;
	}

	public static IReadOnlyList<string> MarkerPaths()
	{
		List<string> list = new List<string>(2);
		Environment.SpecialFolder[] array = new Environment.SpecialFolder[2]
		{
			Environment.SpecialFolder.LocalApplicationData,
			Environment.SpecialFolder.ApplicationData
		};
		foreach (Environment.SpecialFolder folder in array)
		{
			try
			{
				string folderPath = Environment.GetFolderPath(folder);
				if (!string.IsNullOrEmpty(folderPath))
				{
					list.Add(Path.Combine(folderPath, "HebrewBooks", "protect-mode.flag"));
				}
			}
			catch
			{
			}
		}
		return list;
	}

	public static bool MarkerPresent()
	{
		foreach (string item in MarkerPaths())
		{
			try
			{
				if (File.Exists(item))
				{
					return true;
				}
			}
			catch
			{
			}
		}
		return false;
	}

	public static bool ArgsRequest(string[]? args)
	{
		if (args == null)
		{
			return false;
		}
		foreach (string text in args)
		{
			if (!string.IsNullOrWhiteSpace(text))
			{
				string a = text.TrimStart('-', '/').Trim();
				if (string.Equals(a, "protect-mode", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "protectmode", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "protected", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "kiosk", StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
			}
		}
		return false;
	}
}
