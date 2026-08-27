namespace HebrewBooks.Infrastructure.Settings;

public sealed class IndexingOptions
{
	public int AutoCommitIntervalMb { get; set; } = 32768;
}
