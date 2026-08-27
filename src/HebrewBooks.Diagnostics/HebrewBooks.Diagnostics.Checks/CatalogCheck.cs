using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HebrewBooks.Core.Resources;
using HebrewBooks.Data;
using HebrewBooks.Services.Downloader;
using HebrewBooks.Services.Provisioning;
using Microsoft.Data.Sqlite;

namespace HebrewBooks.Diagnostics.Checks;

public sealed class CatalogCheck : IDiagnosticCheck
{
	public string Id => "catalog";

	public string Category => CoreStrings.C66;

	public Task<IReadOnlyList<DiagnosticResult>> RunAsync(DiagnosticContext ctx, CancellationToken ct)
	{
		List<DiagnosticResult> list = new List<DiagnosticResult>();
		if (ctx.Paths == null)
		{
			return Task.FromResult((IReadOnlyList<DiagnosticResult>)list);
		}
		string catalogDbPath = ctx.Paths.CatalogDbPath;
		if (!File.Exists(catalogDbPath))
		{
			list.Add(new DiagnosticResult
			{
				Id = "catalog.missing",
				Category = Category,
				Title = CoreStrings.C67,
				Severity = DiagnosticSeverity.Error,
				Detail = CoreStrings.C68,
				Evidence = catalogDbPath
			});
			return Task.FromResult((IReadOnlyList<DiagnosticResult>)list);
		}
		SqliteConnectionFactory factory = new SqliteConnectionFactory(catalogDbPath, ctx.Options.NetworkInstall);
		try
		{
			using SqliteConnection conn = factory.Open();
			long num = ScalarLong(conn, "SELECT COUNT(*) FROM Katalog");
			long value = ScalarLong(conn, "SELECT COUNT(*) FROM Katalog WHERE SourceType = 'PDF'");
			long value2 = ScalarLong(conn, "SELECT COUNT(*) FROM Katalog WHERE SourceType = 'Text'");
			long value3 = ScalarLong(conn, "SELECT COUNT(*) FROM Katalog WHERE SourceType = 'Personal'");
			list.Add(new DiagnosticResult
			{
				Id = "catalog.ok",
				Category = Category,
				Title = ((num > 0) ? $"{CoreStrings.C52}{num:N0}{CoreStrings.C53}" : CoreStrings.C230),
				Severity = ((num <= 0) ? DiagnosticSeverity.Warning : DiagnosticSeverity.Ok),
				Detail = $"PDF: {value:N0}{CoreStrings.C54}{value2:N0}{CoreStrings.C55}{value3:N0}",
				Evidence = catalogDbPath + (factory.IsReadOnly ? CoreStrings.C69 : "")
			});
			long num2 = ScalarLong(conn, "SELECT COUNT(*) FROM Katalog_fts");
			if (num2 != num)
			{
				list.Add(new DiagnosticResult
				{
					Id = "catalog.fts.sync",
					Category = Category,
					Title = CoreStrings.C70,
					Severity = DiagnosticSeverity.Warning,
					Detail = $"{CoreStrings.C56}{num:N0}{CoreStrings.C57}{num2:N0}. " + CoreStrings.C71,
					Evidence = $"Katalog={num}  Katalog_fts={num2}",
					Fix = (factory.IsReadOnly ? null : new DiagnosticFix
					{
						Id = "catalog.fts.rebuild",
						Label = CoreStrings.C72,
						Kind = FixKind.Safe,
						Run = delegate
						{
							using SqliteConnection sqliteConnection = factory.Open();
							using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
							sqliteCommand.CommandText = "INSERT INTO Katalog_fts(Katalog_fts) VALUES('rebuild')";
							sqliteCommand.ExecuteNonQuery();
							return Task.FromResult(FixOutcome.Ok(CoreStrings.C73));
						}
					})
				});
			}
			long num3 = ScalarLong(conn, "SELECT COUNT(*) FROM (SELECT FileID FROM Katalog WHERE FileID IS NOT NULL AND FileID <> '' GROUP BY FileID HAVING COUNT(*) > 1)");
			if (num3 > 0)
			{
				list.Add(new DiagnosticResult
				{
					Id = "catalog.dup.fileid",
					Category = Category,
					Title = $"{num3:N0}{CoreStrings.C58}",
					Severity = DiagnosticSeverity.Info,
					Detail = CoreStrings.C74
				});
			}
		}
		catch (SqliteException ex) when (IsCorruption(ex))
		{
			list.Add(new DiagnosticResult
			{
				Id = "catalog.corrupt",
				Category = Category,
				Title = CoreStrings.C75,
				Severity = DiagnosticSeverity.Error,
				Detail = CoreStrings.C76 + CoreStrings.C77 + (ctx.Options.NetworkInstall ? CoreStrings.C78 : CoreStrings.C79),
				Evidence = catalogDbPath + "\n" + ex.Message,
				Fix = (ctx.Options.NetworkInstall ? null : BuildRedownloadFix(ctx, catalogDbPath))
			});
		}
		return Task.FromResult((IReadOnlyList<DiagnosticResult>)list);
	}

	private static bool IsCorruption(SqliteException ex)
	{
		int sqliteErrorCode = ex.SqliteErrorCode;
		bool flag = ((sqliteErrorCode == 11 || sqliteErrorCode == 26) ? true : false);
		if (!flag && !ex.Message.Contains("malformed", StringComparison.OrdinalIgnoreCase))
		{
			return ex.Message.Contains("not a database", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	private static DiagnosticFix BuildRedownloadFix(DiagnosticContext ctx, string dbPath)
	{
		return new DiagnosticFix
		{
			Id = "catalog.redownload",
			Label = CoreStrings.C80,
			Kind = FixKind.Confirm,
			Run = async delegate(CancellationToken ct)
			{
				string[] array = new string[3]
				{
					dbPath,
					dbPath + "-wal",
					dbPath + "-shm"
				};
				foreach (string path in array)
				{
					try
					{
						if (File.Exists(path))
						{
							File.Delete(path);
						}
					}
					catch (Exception ex)
					{
						return FixOutcome.Fail(CoreStrings.C59 + ex.Message + CoreStrings.C60);
					}
				}
				try
				{
					await new ProvisioningService(new R2MirrorClient()).ProvisionCatalogBlockingAsync(ctx.DataRoot, null, ct).ConfigureAwait(continueOnCapturedContext: false);
				}
				catch (Exception ex2)
				{
					return FixOutcome.Fail(CoreStrings.C61 + ex2.Message + CoreStrings.C62);
				}
				try
				{
					using SqliteConnection conn = new SqliteConnectionFactory(dbPath, ctx.Options.NetworkInstall).Open();
					long value = ScalarLong(conn, "SELECT COUNT(*) FROM Katalog");
					return FixOutcome.Ok($"{CoreStrings.C63}{value:N0}{CoreStrings.C64}");
				}
				catch (Exception ex3)
				{
					return FixOutcome.Fail(CoreStrings.C65 + ex3.Message + ".");
				}
			}
		};
	}

	private static long ScalarLong(SqliteConnection conn, string sql)
	{
		using SqliteCommand sqliteCommand = conn.CreateCommand();
		sqliteCommand.CommandText = sql;
		object obj = sqliteCommand.ExecuteScalar();
		bool flag = ((obj == null || obj is DBNull) ? true : false);
		return flag ? 0 : Convert.ToInt64(obj);
	}
}
