namespace HebrewBooks.UI.Services;

public sealed record DedicationSubmitResult(bool Ok, string? ErrorCode)
{
	public static DedicationSubmitResult Success()
	{
		return new DedicationSubmitResult(Ok: true, null);
	}

	public static DedicationSubmitResult Fail(string code)
	{
		return new DedicationSubmitResult(Ok: false, code);
	}
}
