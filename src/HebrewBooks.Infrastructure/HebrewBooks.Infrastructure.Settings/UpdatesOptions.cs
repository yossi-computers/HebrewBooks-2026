namespace HebrewBooks.Infrastructure.Settings;

public sealed class UpdatesOptions
{
	public bool CheckOnStartup { get; set; } = true;

	public bool AutoDownload { get; set; }

	public bool IncludeBeta { get; set; }
}
