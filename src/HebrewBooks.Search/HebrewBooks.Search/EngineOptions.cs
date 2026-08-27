namespace HebrewBooks.Search;

public sealed class EngineOptions
{
	public string AlphabetFile { get; init; } = "Alphabet.abc";

	public string PhonicChar { get; init; } = ";";

	public bool IndexNumbers { get; init; } = true;

	public string? PrivateDir { get; init; }

	public string? DebugLogPath { get; init; }
}
