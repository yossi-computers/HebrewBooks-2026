namespace HebrewBooks.Core;

public readonly record struct Result<T>(bool IsSuccess, T? Value, string? Error)
{
	public static Result<T> Ok(T value)
	{
		return new Result<T>(IsSuccess: true, value, null);
	}

	public static Result<T> Fail(string error)
	{
		return new Result<T>(IsSuccess: false, default(T), error);
	}
}
