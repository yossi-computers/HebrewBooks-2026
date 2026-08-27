using System;
using System.Collections.Generic;

namespace HebrewBooks.Search.Incremental;

public static class IncrementalIndexPlanner
{
	public static IndexPlan Plan(IEnumerable<ScannedFile> current, IndexManifest manifest)
	{
		IndexManifest indexManifest = new IndexManifest
		{
			Version = manifest.Version,
			CorpusRootName = manifest.CorpusRootName,
			Entries = new Dictionary<string, ManifestEntry>(manifest.Entries, StringComparer.OrdinalIgnoreCase)
		};
		List<string> list = new List<string>();
		List<string> list2 = new List<string>();
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		int num = 0;
		int num2 = 0;
		int num3 = 0;
		foreach (ScannedFile item in current)
		{
			hashSet.Add(item.Key);
			if (!manifest.Entries.TryGetValue(item.Key, out ManifestEntry value))
			{
				list.Add(item.AbsPath);
				indexManifest.Entries[item.Key] = new ManifestEntry
				{
					Size = item.Size,
					Mtime = item.Mtime,
					IndexedPath = item.AbsPath
				};
				num++;
			}
			else if (value.Size != item.Size)
			{
				if (!string.IsNullOrEmpty(value.IndexedPath))
				{
					list2.Add(value.IndexedPath);
				}
				list.Add(item.AbsPath);
				indexManifest.Entries[item.Key] = new ManifestEntry
				{
					Size = item.Size,
					Mtime = item.Mtime,
					IndexedPath = item.AbsPath
				};
				num2++;
			}
			else
			{
				num3++;
			}
		}
		int num4 = 0;
		foreach (var (text2, manifestEntry2) in manifest.Entries)
		{
			if (!hashSet.Contains(text2))
			{
				if (!string.IsNullOrEmpty(manifestEntry2.IndexedPath))
				{
					list2.Add(manifestEntry2.IndexedPath);
				}
				indexManifest.Entries.Remove(text2);
				num4++;
			}
		}
		return new IndexPlan(list, list2, indexManifest, num, num2, num4, num3);
	}
}
