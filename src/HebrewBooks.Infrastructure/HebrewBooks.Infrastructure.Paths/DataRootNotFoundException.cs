using System;

namespace HebrewBooks.Infrastructure.Paths;

public sealed class DataRootNotFoundException : Exception
{
	public DataRootNotFoundException(string message)
		: base(message)
	{
	}
}
