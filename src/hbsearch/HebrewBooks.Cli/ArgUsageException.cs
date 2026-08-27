using System;

namespace HebrewBooks.Cli;

internal sealed class ArgUsageException : Exception
{
	public ArgUsageException(string message)
		: base(message)
	{
	}
}
