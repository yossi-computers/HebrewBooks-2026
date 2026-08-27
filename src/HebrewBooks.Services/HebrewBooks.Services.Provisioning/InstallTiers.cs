using System.Collections.Generic;

namespace HebrewBooks.Services.Provisioning;

public static class InstallTiers
{
	public const long ApproxAppBytes = 1503238553L;

	public const long ApproxIndexBytes = 34305552145L;

	public const long ApproxBooksBytes = 688900000000L;

	public static string ToInstallType(InstallTier tier)
	{
		return tier switch
		{
			InstallTier.Empty => "Empty", 
			InstallTier.CatalogOnly => "CatalogOnly", 
			InstallTier.Online => "CatalogOnly", 
			InstallTier.CatalogPlusIndex => "CatalogPlusIndex", 
			_ => "Full", 
		};
	}

	public static InstallTier FromInstallType(string? installType, bool useOnlineService = false)
	{
		return installType switch
		{
			"Empty" => InstallTier.Empty, 
			"CatalogOnly" => (!useOnlineService) ? InstallTier.CatalogOnly : InstallTier.Online, 
			"CatalogPlusIndex" => InstallTier.CatalogPlusIndex, 
			_ => InstallTier.Full, 
		};
	}

	public static IReadOnlyList<TierInfo> Build(long appBytes, long indexBytes, long booksBytes)
	{
		return new TierInfo[5]
		{
			new TierInfo(InstallTier.Online, appBytes),
			new TierInfo(InstallTier.CatalogOnly, appBytes),
			new TierInfo(InstallTier.CatalogPlusIndex, appBytes + indexBytes),
			new TierInfo(InstallTier.Full, appBytes + indexBytes + booksBytes),
			new TierInfo(InstallTier.Empty, 0L)
		};
	}

	public static ProvisionPlan ToPlan(InstallTier tier, bool buildIndexLocally)
	{
		return tier switch
		{
			InstallTier.Empty => new ProvisionPlan(Index: false, Books: false, BuildIndexLocally: false), 
			InstallTier.Online => new ProvisionPlan(Index: false, Books: false, BuildIndexLocally: false), 
			InstallTier.CatalogOnly => new ProvisionPlan(Index: false, Books: false, BuildIndexLocally: false), 
			InstallTier.CatalogPlusIndex => new ProvisionPlan(Index: true, Books: false, BuildIndexLocally: false), 
			_ => buildIndexLocally ? new ProvisionPlan(Index: false, Books: true, BuildIndexLocally: true) : new ProvisionPlan(Index: true, Books: true, BuildIndexLocally: false), 
		};
	}
}
