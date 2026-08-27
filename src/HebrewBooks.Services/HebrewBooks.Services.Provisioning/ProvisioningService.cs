using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HebrewBooks.Services.Downloader;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace HebrewBooks.Services.Provisioning;

public sealed class ProvisioningService
{
	public sealed record LibraryDownloadStatus(ProvisionPlan Pending, bool IndexPresent, int BooksOnDisk, bool Marked)
	{
		public bool IsComplete => !Pending.HasWork;
	}

	private readonly R2MirrorClient _r2;

	private readonly ILogger<ProvisioningService>? _log;

	public const string AppSub = "App";

	public const string BooksSub = "Books";

	public const string IndexSub = "Bookshelf_IDX";

	private const string IndexStagingSub = "Bookshelf_IDX.staging";

	private const string DataPrefix = "HebrewBooks";

	private const string ProvisionedMarkerRel = "App\\.provisioned";

	private const double CompleteEnoughRatio = 0.99;

	public ProvisioningService(R2MirrorClient r2, ILogger<ProvisioningService>? log = null)
	{
		_r2 = r2;
		_log = log;
	}

	private static string TierToken(string installType, bool buildLocally)
	{
		return $"{installType}|{buildLocally}";
	}

	public void MarkProvisioned(string root, string installType, bool buildLocally)
	{
		try
		{
			string path = Path.Combine(root, "App\\.provisioned");
			Directory.CreateDirectory(Path.GetDirectoryName(path));
			File.WriteAllText(path, TierToken(installType, buildLocally));
		}
		catch
		{
		}
	}

	public ProvisionPlan ComputePendingPlan(string root, string installType, bool buildLocally)
	{
		InstallTier tier = ((installType == "CatalogOnly") ? InstallTier.CatalogOnly : ((!(installType == "CatalogPlusIndex")) ? InstallTier.Full : InstallTier.CatalogPlusIndex));
		ProvisionPlan provisionPlan = InstallTiers.ToPlan(tier, buildLocally);
		if (!provisionPlan.HasWork)
		{
			return provisionPlan;
		}
		try
		{
			string path = Path.Combine(root, "App\\.provisioned");
			if (File.Exists(path) && string.Equals(File.ReadAllText(path).Trim(), TierToken(installType, buildLocally), StringComparison.Ordinal))
			{
				return new ProvisionPlan(Index: false, Books: false, BuildIndexLocally: false);
			}
		}
		catch
		{
		}
		return provisionPlan;
	}

	public bool IsCompleteDataRoot(string root)
	{
		return File.Exists(Path.Combine(root, "App", "Katalog.db"));
	}

	public LibraryDownloadStatus DescribeStatus(string root, string installType, bool buildLocally)
	{
		ProvisionPlan pending = ComputePendingPlan(root, installType, buildLocally);
		int booksOnDisk = 0;
		try
		{
			string path = Path.Combine(root, "Books");
			if (Directory.Exists(path))
			{
				booksOnDisk = Directory.EnumerateFiles(path, "*.pdf", SearchOption.AllDirectories).Count();
			}
		}
		catch
		{
		}
		bool marked = false;
		try
		{
			string path2 = Path.Combine(root, "App\\.provisioned");
			marked = File.Exists(path2) && string.Equals(File.ReadAllText(path2).Trim(), TierToken(installType, buildLocally), StringComparison.Ordinal);
		}
		catch
		{
		}
		return new LibraryDownloadStatus(pending, HasIndex(root), booksOnDisk, marked);
	}

	public bool HasIndex(string root)
	{
		return Directory.Exists(Path.Combine(root, "Bookshelf_IDX"));
	}

	public bool LooksAlreadyProvisioned(string root, ProvisionPlan plan)
	{
		try
		{
			if (plan.Index && !plan.BuildIndexLocally && !HasIndex(root))
			{
				return false;
			}
			if (!plan.Books)
			{
				return true;
			}
			int num = CountCatalogPdfBooks(root);
			if (num <= 0)
			{
				return false;
			}
			string path = Path.Combine(root, "Books");
			if (!Directory.Exists(path))
			{
				return false;
			}
			int num2 = Directory.EnumerateFiles(path, "*.pdf", SearchOption.AllDirectories).Count();
			bool flag = (double)num2 >= (double)num * 0.99;
			_log?.LogInformation("Provisioning self-check: {OnDisk} PDFs on disk vs {Expected} catalog books → {Verdict}", num2, num, flag ? "already provisioned" : "download still owed");
			return flag;
		}
		catch (Exception exception)
		{
			_log?.LogDebug(exception, "Provisioning self-check failed; assuming the download is still owed");
			return false;
		}
	}

	private static int CountCatalogPdfBooks(string root)
	{
		string text = Path.Combine(root, "App", "Katalog.db");
		if (!File.Exists(text))
		{
			return 0;
		}
		using SqliteConnection sqliteConnection = new SqliteConnection(new SqliteConnectionStringBuilder
		{
			DataSource = text,
			Mode = SqliteOpenMode.ReadOnly,
			Cache = SqliteCacheMode.Private
		}.ToString());
		sqliteConnection.Open();
		using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
		sqliteCommand.CommandText = "SELECT COUNT(*) FROM Katalog WHERE COALESCE(NULLIF(SourceType, ''), 'PDF') = 'PDF'";
		return Convert.ToInt32(sqliteCommand.ExecuteScalar());
	}

	public void CreateEmptyDataRoot(string root)
	{
		Directory.CreateDirectory(Path.Combine(root, "App"));
		Directory.CreateDirectory(Path.Combine(root, "Books"));
		Directory.CreateDirectory(Path.Combine(root, "Bookshelf_IDX"));
	}

	public Task ProvisionCatalogBlockingAsync(string root, IProgress<(long Bytes, long Total, int Files, int TotalFiles)>? progress = null, CancellationToken ct = default(CancellationToken))
	{
		return _r2.DownloadPrefixAsync("HebrewBooks/App", root, "HebrewBooks", 16, verifyHash: true, progress, ct);
	}

	public async Task ProvisionIndexBackgroundAsync(string root, IProgress<(long Bytes, long Total, int Files, int TotalFiles)>? progress = null, CancellationToken ct = default(CancellationToken))
	{
		string staging = Path.Combine(root, "Bookshelf_IDX.staging");
		string final = Path.Combine(root, "Bookshelf_IDX");
		await _r2.DownloadPrefixAsync("HebrewBooks/Bookshelf_IDX", staging, "HebrewBooks/Bookshelf_IDX", 16, verifyHash: false, progress, ct).ConfigureAwait(continueOnCapturedContext: false);
		SwapIn(staging, final);
	}

	public Task ProvisionBooksBackgroundAsync(string root, IProgress<(long Bytes, long Total, int Files, int TotalFiles)>? progress = null, CancellationToken ct = default(CancellationToken))
	{
		return _r2.DownloadPrefixAsync("HebrewBooks/books", Path.Combine(root, "Books"), "HebrewBooks/books", 16, verifyHash: false, progress, ct);
	}

	public async Task RunBackgroundAsync(string root, ProvisionPlan plan, IProgress<(long Bytes, long Total)>? progress = null, CancellationToken ct = default(CancellationToken))
	{
		bool doIndex = plan.Index && !HasIndex(root);
		long num = ((!doIndex) ? 0 : (await _r2.SumPrefixBytesAsync("HebrewBooks/Bookshelf_IDX", ct).ConfigureAwait(continueOnCapturedContext: false)));
		long indexTotal = num;
		num = ((!plan.Books) ? 0 : (await _r2.SumPrefixBytesAsync("HebrewBooks/books", ct).ConfigureAwait(continueOnCapturedContext: false)));
		long num2 = num;
		long grandTotal = indexTotal + num2;
		long baseBytes = 0L;
		Progress<(long Bytes, long Total, int Files, int TotalFiles)> sub = new Progress<(long, long, int, int)>(delegate((long Bytes, long Total, int Files, int TotalFiles) t)
		{
			progress?.Report((baseBytes + t.Bytes, grandTotal));
		});
		if (doIndex)
		{
			await ProvisionIndexBackgroundAsync(root, sub, ct).ConfigureAwait(continueOnCapturedContext: false);
			baseBytes += indexTotal;
		}
		if (plan.Books)
		{
			await ProvisionBooksBackgroundAsync(root, sub, ct).ConfigureAwait(continueOnCapturedContext: false);
		}
	}

	private static void SwapIn(string staging, string final)
	{
		string text = final + ".old";
		try
		{
			if (Directory.Exists(text))
			{
				Directory.Delete(text, recursive: true);
			}
		}
		catch
		{
		}
		if (Directory.Exists(final))
		{
			Directory.Move(final, text);
		}
		Directory.Move(staging, final);
		try
		{
			if (Directory.Exists(text))
			{
				Directory.Delete(text, recursive: true);
			}
		}
		catch
		{
		}
	}
}
