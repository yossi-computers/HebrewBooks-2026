using HebrewBooks.Core.Models;

namespace HebrewBooks.Services.Catalog;

public sealed record DeletionResult(Book Book, DeletionOutcome Outcome, string? Reason)
{
	public static DeletionResult Ok(Book b)
	{
		return new DeletionResult(b, DeletionOutcome.Ok, null);
	}

	public static DeletionResult Skipped(Book b, string why)
	{
		return new DeletionResult(b, DeletionOutcome.Skipped, why);
	}

	public static DeletionResult Failed(Book b, string why)
	{
		return new DeletionResult(b, DeletionOutcome.Failed, why);
	}
}
