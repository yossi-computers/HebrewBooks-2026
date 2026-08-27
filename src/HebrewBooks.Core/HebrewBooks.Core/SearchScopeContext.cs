using System;
using System.Collections.Generic;
using System.Linq;
using HebrewBooks.Core.Abstractions;

namespace HebrewBooks.Core;

public sealed class SearchScopeContext : ISearchScopeContext
{
	private readonly HashSet<string> _marked = new HashSet<string>(StringComparer.Ordinal);

	private string[] _displayed = Array.Empty<string>();

	public IReadOnlyCollection<string> MarkedFileIds => (IReadOnlyCollection<string>)(object)_marked.ToArray();

	public int MarkedCount => _marked.Count;

	public IReadOnlyCollection<string> DisplayedFileIds => (IReadOnlyCollection<string>)(object)_displayed;

	public int DisplayedCount => _displayed.Length;

	public event EventHandler? Changed;

	public bool IsMarked(string fileId)
	{
		if (!string.IsNullOrEmpty(fileId))
		{
			return _marked.Contains(fileId);
		}
		return false;
	}

	public void SetMarked(string fileId, bool marked)
	{
		if (!string.IsNullOrEmpty(fileId) && (marked ? _marked.Add(fileId) : _marked.Remove(fileId)))
		{
			Raise();
		}
	}

	public void MarkAll(IEnumerable<string> fileIds)
	{
		bool flag = false;
		foreach (string fileId in fileIds)
		{
			if (!string.IsNullOrEmpty(fileId) && _marked.Add(fileId))
			{
				flag = true;
			}
		}
		if (flag)
		{
			Raise();
		}
	}

	public void ClearMarks()
	{
		if (_marked.Count != 0)
		{
			_marked.Clear();
			Raise();
		}
	}

	public void SetDisplayedFileIds(IEnumerable<string> fileIds)
	{
		_displayed = fileIds.Where((string s) => !string.IsNullOrEmpty(s)).Distinct<string>(StringComparer.Ordinal).ToArray();
		Raise();
	}

	private void Raise()
	{
		this.Changed?.Invoke(this, EventArgs.Empty);
	}
}
