using System;
using System.IO;

namespace HebrewBooks.Infrastructure.Settings;

public sealed class BookshelfOptions
{
	public const string DefaultOnlineServiceUrl = "https://hebrewbooks.pages.dev";

	public const string DefaultOnlinePdfBaseUrl = "https://files.hebrewbooksoffline.dpdns.org/HebrewBooks/books";

	public SearchOptions Search { get; set; } = new SearchOptions();

	public ViewOptions View { get; set; } = new ViewOptions();

	public PathsOptions Paths { get; set; } = new PathsOptions();

	public IndexingOptions Indexing { get; set; } = new IndexingOptions();

	public UpdatesOptions Updates { get; set; } = new UpdatesOptions();

	public string Language { get; set; } = "auto";

	public bool ForceProtectMode { get; set; }

	public bool NetworkInstall { get; set; }

	public bool UseOnlineService { get; set; }

	public bool? UsageTelemetryConsent { get; set; }

	public string? UsageTelemetryConsentAskedVersion { get; set; }

	public string? AnonymousInstallId { get; set; }

	public string? EffectiveOnlineServiceUrl()
	{
		if (!UseOnlineService)
		{
			return null;
		}
		string text = Paths.OnlineServiceUrl?.Trim();
		if (string.IsNullOrWhiteSpace(text))
		{
			text = "https://hebrewbooks.pages.dev";
		}
		return text.TrimEnd('/');
	}

	public string? EffectiveOnlinePdfBaseUrl()
	{
		if (!UseOnlineService)
		{
			return null;
		}
		string text = Paths.OnlinePdfBaseUrl?.Trim();
		if (string.IsNullOrWhiteSpace(text))
		{
			text = "https://files.hebrewbooksoffline.dpdns.org/HebrewBooks/books";
		}
		return text.TrimEnd('/');
	}

	public string? EffectiveSearchServiceUrl()
	{
		if (!NetworkInstall)
		{
			return null;
		}
		string text = Paths.SearchServiceUrl?.Trim();
		if (!string.IsNullOrWhiteSpace(text))
		{
			return text;
		}
		string text2 = Paths.SearchServiceHost?.Trim();
		if (string.IsNullOrWhiteSpace(text2))
		{
			return null;
		}
		if (!text2.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !text2.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
		{
			text2 = "http://" + text2;
		}
		int value = ((Paths.SearchServicePort > 0) ? Paths.SearchServicePort : 8080);
		return $"{text2.TrimEnd('/')}:{value}";
	}

	public string? EffectiveNetworkBase()
	{
		if (!NetworkInstall)
		{
			return null;
		}
		string text = Paths.NetworkBasePath?.Trim();
		if (!string.IsNullOrWhiteSpace(text))
		{
			return text;
		}
		string text2 = Paths.BooksDirOverride?.Trim();
		if (string.IsNullOrWhiteSpace(text2))
		{
			return null;
		}
		try
		{
			return Path.GetDirectoryName(Path.GetFullPath(text2.TrimEnd('\\', '/')));
		}
		catch
		{
			return null;
		}
	}

	public string? EffectiveBooksDir()
	{
		if (!NetworkInstall)
		{
			return null;
		}
		string text = Paths.BooksDirOverride?.Trim();
		if (!string.IsNullOrWhiteSpace(text))
		{
			return text;
		}
		string text2 = Paths.NetworkBasePath?.Trim();
		if (!string.IsNullOrWhiteSpace(text2))
		{
			return Path.Combine(text2, "books");
		}
		return null;
	}

	public string? EffectiveCatalogMaster()
	{
		if (!NetworkInstall)
		{
			return null;
		}
		string text = Paths.CatalogMasterPath?.Trim();
		if (!string.IsNullOrWhiteSpace(text))
		{
			return text;
		}
		string text2 = Paths.NetworkBasePath?.Trim();
		if (!string.IsNullOrWhiteSpace(text2))
		{
			return Path.Combine(text2, "App", "Katalog.db");
		}
		return null;
	}
}
