using System;
using System.Collections.Generic;

namespace HebrewBooks.Core.Abstractions;

public interface ISearchScopeContext
{
	IReadOnlyCollection<string> MarkedFileIds { get; }

	int MarkedCount { get; }

	IReadOnlyCollection<string> DisplayedFileIds { get; }

	int DisplayedCount { get; }

	event EventHandler? Changed;

	bool IsMarked(string fileId);

	void SetMarked(string fileId, bool marked);

	void MarkAll(IEnumerable<string> fileIds);

	void ClearMarks();

	void SetDisplayedFileIds(IEnumerable<string> fileIds);
}
