using System.Collections.Generic;

namespace HebrewBooks.Search.Incremental;

public sealed record IndexPlan(IReadOnlyList<string> AddPaths, IReadOnlyList<string> RemovePaths, IndexManifest UpdatedManifest, int NewCount, int ChangedCount, int RemovedCount, int UnchangedCount)
{
	public bool IsNoOp
	{
		get
		{
			if (AddPaths.Count == 0)
			{
				return RemovePaths.Count == 0;
			}
			return false;
		}
	}
}
