namespace HebrewBooks.Diagnostics;

public sealed record FixOutcome(bool Success, string Message)
{
	public static FixOutcome Ok(string message)
	{
		return new FixOutcome(Success: true, message);
	}

	public static FixOutcome Fail(string message)
	{
		return new FixOutcome(Success: false, message);
	}
}
