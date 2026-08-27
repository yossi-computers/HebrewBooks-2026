using System;
using System.Diagnostics;
using System.IO;

namespace HebrewBooks.Infrastructure.Paths;

public sealed class CatalogSyncService
{
	public enum SyncResult
	{
		Skipped,
		MasterNotFound,
		Copied,
		Failed
	}

	public sealed record SyncInfo(SyncResult Result, long BytesCopied, long ElapsedMs, string? FailureMessage);

	public SyncInfo SyncIfNeeded(string? masterPath, string localPath)
	{
		if (string.IsNullOrWhiteSpace(masterPath) || string.IsNullOrWhiteSpace(localPath))
		{
			return new SyncInfo(SyncResult.Skipped, 0L, 0L, null);
		}
		try
		{
			if (!File.Exists(masterPath))
			{
				return new SyncInfo(SyncResult.MasterNotFound, 0L, 0L, null);
			}
			FileInfo fileInfo = new FileInfo(masterPath);
			bool num = File.Exists(localPath);
			FileInfo fileInfo2 = (num ? new FileInfo(localPath) : null);
			bool flag = num && fileInfo2.Length < 1048576 && fileInfo.Length > fileInfo2.Length;
			if (num && !flag && fileInfo.LastWriteTimeUtc <= fileInfo2.LastWriteTimeUtc)
			{
				return new SyncInfo(SyncResult.Skipped, 0L, 0L, null);
			}
			string directoryName = Path.GetDirectoryName(localPath);
			if (!string.IsNullOrEmpty(directoryName))
			{
				Directory.CreateDirectory(directoryName);
			}
			string text = localPath + ".sync.tmp";
			try
			{
				File.Delete(text);
			}
			catch
			{
			}
			Stopwatch stopwatch = Stopwatch.StartNew();
			File.Copy(masterPath, text, overwrite: true);
			try
			{
				new FileInfo(text).LastWriteTimeUtc = fileInfo.LastWriteTimeUtc;
			}
			catch
			{
			}
			File.Move(text, localPath, overwrite: true);
			TryDelete(localPath + "-wal");
			TryDelete(localPath + "-shm");
			stopwatch.Stop();
			return new SyncInfo(SyncResult.Copied, fileInfo.Length, stopwatch.ElapsedMilliseconds, null);
		}
		catch (Exception ex)
		{
			return new SyncInfo(SyncResult.Failed, 0L, 0L, ex.Message);
		}
	}

	private static void TryDelete(string path)
	{
		try
		{
			if (File.Exists(path))
			{
				File.Delete(path);
			}
		}
		catch
		{
		}
	}
}
