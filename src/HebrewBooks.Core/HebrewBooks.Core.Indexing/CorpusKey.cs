using System;
using System.IO;

namespace HebrewBooks.Core.Indexing;

public static class CorpusKey
{
	public static string Compute(string absolutePath, string? relativeKeyRoot, string? currentDriveRoot)
	{
		if (string.IsNullOrEmpty(absolutePath))
		{
			return "";
		}
		string text = absolutePath;
		if (!string.IsNullOrEmpty(currentDriveRoot))
		{
			try
			{
				string pathRoot = Path.GetPathRoot(absolutePath);
				if (!string.IsNullOrEmpty(pathRoot) && !string.Equals(pathRoot, currentDriveRoot, StringComparison.OrdinalIgnoreCase))
				{
					text = currentDriveRoot + absolutePath.Substring(pathRoot.Length);
				}
			}
			catch
			{
			}
		}
		if (!string.IsNullOrEmpty(relativeKeyRoot) && text.StartsWith(relativeKeyRoot, StringComparison.OrdinalIgnoreCase))
		{
			try
			{
				return Path.GetRelativePath(relativeKeyRoot, text);
			}
			catch
			{
			}
		}
		return Path.GetFileNameWithoutExtension(text);
	}
}
