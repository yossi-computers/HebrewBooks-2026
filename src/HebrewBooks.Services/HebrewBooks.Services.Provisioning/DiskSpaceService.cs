using System;
using System.Diagnostics;
using System.IO;

namespace HebrewBooks.Services.Provisioning;

public sealed class DiskSpaceService
{
	public const long DefaultHeadroomBytes = 5368709120L;

	private static readonly string[] DataSubdirs = new string[4] { "App", "Books", "Bookshelf_IDX", "Bookshelf_IDX.staging" };

	public long AvailableFreeBytes(string path)
	{
		try
		{
			string pathRoot = Path.GetPathRoot(Path.GetFullPath(path));
			if (string.IsNullOrEmpty(pathRoot))
			{
				return 0L;
			}
			return new DriveInfo(pathRoot).AvailableFreeSpace;
		}
		catch
		{
			return 0L;
		}
	}

	public bool Fits(long requiredBytes, string targetPath, long headroomBytes = 5368709120L, long alreadyPresentBytes = 0L)
	{
		long num = Math.Clamp(alreadyPresentBytes, 0L, requiredBytes);
		return AvailableFreeBytes(targetPath) >= requiredBytes - num + headroomBytes;
	}

	public long UsedByExistingData(string dataRoot, TimeSpan? budget = null)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		TimeSpan timeSpan = budget ?? TimeSpan.FromSeconds(5.0);
		long num = 0L;
		string[] dataSubdirs = DataSubdirs;
		foreach (string path in dataSubdirs)
		{
			try
			{
				DirectoryInfo directoryInfo = new DirectoryInfo(Path.Combine(dataRoot, path));
				if (!directoryInfo.Exists)
				{
					continue;
				}
				foreach (FileInfo item in directoryInfo.EnumerateFiles("*", SearchOption.AllDirectories))
				{
					num += item.Length;
					if (stopwatch.Elapsed > timeSpan)
					{
						return num;
					}
				}
			}
			catch
			{
			}
		}
		return num;
	}
}
