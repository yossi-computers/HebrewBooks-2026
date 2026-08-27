namespace HebrewBooks.Infrastructure.Settings;

public sealed class PathsOptions
{
	public uint DataVolumeSerial { get; set; }

	public string? LastDataRootPath { get; set; }

	public string DataSubdir { get; set; } = "HebrewBooks";

	public string? BooksDirOverride { get; set; }

	public string? IndexesDirOverride { get; set; }

	public string? FastIndexesDir { get; set; }

	public string? CatalogMasterPath { get; set; }

	public string? SearchServiceUrl { get; set; }

	public string? NetworkBasePath { get; set; }

	public string? SearchServiceHost { get; set; }

	public int SearchServicePort { get; set; } = 8080;

	public string? OnlineServiceUrl { get; set; }

	public string? OnlinePdfBaseUrl { get; set; }

	public bool PreferOnlineWhenNoDrive { get; set; }

	public string InstallType { get; set; } = "Full";

	public bool BuildIndexLocally { get; set; }

	public bool ProvisionPending { get; set; }

	public bool ForceRescan { get; set; }
}
