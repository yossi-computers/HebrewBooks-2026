using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HebrewBooks.Core.Resources;
using HebrewBooks.Data;
using HebrewBooks.Search.Incremental;
using Microsoft.Data.Sqlite;

namespace HebrewBooks.Diagnostics.Checks;

public sealed class IndexVsCatalogCheck : IDiagnosticCheck
{
	public string Id => "index";

	public string Category => CoreStrings.C157;

	public Task<IReadOnlyList<DiagnosticResult>> RunAsync(DiagnosticContext ctx, CancellationToken ct)
	{
		List<DiagnosticResult> list = new List<DiagnosticResult>();
		if (ctx.Paths == null)
		{
			return Task.FromResult((IReadOnlyList<DiagnosticResult>)list);
		}
		SqliteConnectionFactory sqliteConnectionFactory = new SqliteConnectionFactory(ctx.Paths.CatalogDbPath, ctx.Options.NetworkInstall);
		if (!File.Exists(ctx.Paths.CatalogDbPath))
		{
			return Task.FromResult((IReadOnlyList<DiagnosticResult>)list);
		}
		try
		{
			using SqliteConnection conn = sqliteConnectionFactory.Open();
			long expected = ScalarLong(conn, "SELECT COUNT(DISTINCT FileID) FROM Katalog WHERE SourceType = 'PDF' AND FileID IS NOT NULL AND FileID <> ''");
			list.AddRange(CheckCorpus(ctx, CoreStrings.C49, ctx.Paths.IndexesRoot, expected, "index.pdf"));
			long num = ScalarLong(conn, "SELECT COUNT(*) FROM Katalog WHERE SourceType = 'Text'");
			if (num > 0 || Directory.Exists(ctx.Paths.OtzrayaIndexPath))
			{
				list.AddRange(CheckCorpus(ctx, CoreStrings.C124, ctx.Paths.OtzrayaIndexPath, num, "index.otzraya"));
			}
			long num2 = ScalarLong(conn, "SELECT COUNT(*) FROM Katalog WHERE SourceType = 'Personal'");
			if (num2 > 0 || Directory.Exists(ctx.Paths.PersonalIndexPath))
			{
				list.AddRange(CheckCorpus(ctx, CoreStrings.C15, ctx.Paths.PersonalIndexPath, num2, "index.personal"));
			}
		}
		catch (SqliteException ex) when (((Func<bool>)delegate
		{
			// Could not convert BlockContainer to single expression
			int sqliteErrorCode = ex.SqliteErrorCode;
			return ((sqliteErrorCode == 11 || sqliteErrorCode == 26) ? true : false) || ex.Message.Contains("malformed", StringComparison.OrdinalIgnoreCase);
		}).Invoke())
		{
		}
		return Task.FromResult((IReadOnlyList<DiagnosticResult>)list);
	}

	private static IEnumerable<DiagnosticResult> CheckCorpus(DiagnosticContext ctx, string corpusName, string indexPath, long expected, string idBase)
	{
		if (expected == 0L && !Directory.Exists(indexPath))
		{
			yield break;
		}
		IndexManifest indexManifest = IndexManifest.Load(indexPath);
		bool flag = Directory.Exists(indexPath);
		if (!flag || indexManifest == null)
		{
			yield return new DiagnosticResult
			{
				Id = idBase + ".missing",
				Category = CoreStrings.C157,
				Title = CoreStrings.C166 + corpusName,
				Severity = ((expected <= 0) ? DiagnosticSeverity.Info : DiagnosticSeverity.Warning),
				Detail = ((expected > 0) ? $"{CoreStrings.C167}{expected:N0}{CoreStrings.C168}" : CoreStrings.C177),
				Evidence = indexPath + "\n" + (flag ? CoreStrings.C178 : CoreStrings.C179),
				Fix = ((expected > 0) ? AppOnlyBuildFix(idBase, corpusName) : null)
			};
			yield break;
		}
		long num = indexManifest.Entries.Count;
		long num2 = expected - num;
		if (num2 > 0)
		{
			yield return new DiagnosticResult
			{
				Id = idBase + ".stale",
				Category = CoreStrings.C157,
				Title = $"{CoreStrings.C169}{num2:N0}{CoreStrings.C170}{corpusName}",
				Severity = DiagnosticSeverity.Warning,
				Detail = $"{CoreStrings.C115}{num:N0}{CoreStrings.C171}{expected:N0}{CoreStrings.C172}",
				Evidence = $"{indexPath}{CoreStrings.C173}{num}{CoreStrings.C174}{expected}",
				Fix = AppOnlyBuildFix(idBase, corpusName)
			};
		}
		else
		{
			yield return new DiagnosticResult
			{
				Id = idBase + ".ok",
				Category = CoreStrings.C157,
				Title = CoreStrings.C145 + corpusName + CoreStrings.C175,
				Severity = DiagnosticSeverity.Ok,
				Detail = $"{num:N0}{CoreStrings.C176}{expected:N0}).",
				Evidence = indexPath
			};
		}
	}

	private static DiagnosticFix AppOnlyBuildFix(string idBase, string corpusName)
	{
		return new DiagnosticFix
		{
			Id = idBase + ".build",
			Label = CoreStrings.C122 + corpusName + ")",
			Kind = FixKind.AppOnly,
			Run = null
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
