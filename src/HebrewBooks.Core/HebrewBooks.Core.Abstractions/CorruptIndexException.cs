using System;

namespace HebrewBooks.Core.Abstractions;

public sealed class CorruptIndexException : Exception
{
	public string IndexPath { get; }

	public string Detail { get; }

	public CorruptIndexException(string indexPath, string detail)
		: base("dtSearch index is corrupt/truncated at '" + indexPath + "'.")
	{
		IndexPath = indexPath;
		Detail = detail;
	}
}
