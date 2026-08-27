using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using HebrewBooks.Core.Resources;
using HebrewBooks.Data;
using Microsoft.Data.Sqlite;

namespace HebrewBooks.Diagnostics.Checks;

public sealed class BookFilesCheck : IDiagnosticCheck
{
	private const int SampleSize = 400;

	public string Id => "files";

	public string Category => CoreStrings.C46;

	public Task<IReadOnlyList<DiagnosticResult>> RunAsync(DiagnosticContext ctx, CancellationToken ct)
	{
		List<DiagnosticResult> list = new List<DiagnosticResult>();
		if (ctx.Paths == null || !File.Exists(ctx.Paths.CatalogDbPath))
		{
			return Task.FromResult((IReadOnlyList<DiagnosticResult>)list);
		}
		using SqliteConnection conn = new SqliteConnectionFactory(ctx.Paths.CatalogDbPath, ctx.Options.NetworkInstall).Open();
		if (string.Equals(ctx.Options.Paths.InstallType, "Full", StringComparison.OrdinalIgnoreCase))
		{
			list.Add(SamplePdf(ctx, conn, ct));
		}
		else
		{
			list.Add(new DiagnosticResult
			{
				Id = "files.pdf.ondemand",
				Category = Category,
				Title = CoreStrings.C47,
				Severity = DiagnosticSeverity.Info,
				Detail = CoreStrings.C32 + ctx.Options.Paths.InstallType + CoreStrings.C33 + CoreStrings.C48
			});
		}
		DiagnosticResult diagnosticResult = SamplePersonal(ctx, conn, ct);
		if (diagnosticResult != null)
		{
			list.Add(diagnosticResult);
		}
		return Task.FromResult((IReadOnlyList<DiagnosticResult>)list);
	}

	private DiagnosticResult SamplePdf(DiagnosticContext ctx, SqliteConnection conn, CancellationToken ct)
	{
		int num = 0;
		int num2 = 0;
		List<string> list = new List<string>();
		using (SqliteCommand sqliteCommand = conn.CreateCommand())
		{
			sqliteCommand.CommandText = "SELECT FileID, Folder, BookName FROM Katalog WHERE SourceType = 'PDF' AND FileID IS NOT NULL AND FileID <> '' ORDER BY RANDOM() LIMIT $n";
			sqliteCommand.Parameters.AddWithValue("$n", 400);
			using SqliteDataReader sqliteDataReader = sqliteCommand.ExecuteReader();
			while (sqliteDataReader.Read())
			{
				ct.ThrowIfCancellationRequested();
				string text = sqliteDataReader.GetString(0);
				if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
				{
					continue;
				}
				string folder = (sqliteDataReader.IsDBNull(1) ? null : sqliteDataReader.GetString(1));
				string value = (sqliteDataReader.IsDBNull(2) ? text : sqliteDataReader.GetString(2));
				num++;
				if (!File.Exists(ctx.Paths.PdfPath(result, folder)))
				{
					num2++;
					if (list.Count < 8)
					{
						list.Add($"{value} (FileID {result})");
					}
				}
			}
		}
		return BuildSampleResult(ctx, "files.pdf", CoreStrings.C49, num, num2, list, "SELECT COUNT(*) FROM Katalog WHERE SourceType = 'PDF'", conn);
	}

	private DiagnosticResult? SamplePersonal(DiagnosticContext ctx, SqliteConnection conn, CancellationToken ct)
	{
		if (ScalarLong(conn, "SELECT COUNT(*) FROM Katalog WHERE SourceType = 'Personal'") == 0L)
		{
			return null;
		}
		int num = 0;
		int num2 = 0;
		List<string> list = new List<string>();
		using (SqliteCommand sqliteCommand = conn.CreateCommand())
		{
			sqliteCommand.CommandText = "SELECT RelativePath, BookName FROM Katalog WHERE SourceType = 'Personal' AND RelativePath IS NOT NULL AND RelativePath <> '' ORDER BY RANDOM() LIMIT $n";
			sqliteCommand.Parameters.AddWithValue("$n", 400);
			using SqliteDataReader sqliteDataReader = sqliteCommand.ExecuteReader();
			while (sqliteDataReader.Read())
			{
				ct.ThrowIfCancellationRequested();
				string text = sqliteDataReader.GetString(0);
				string item = (sqliteDataReader.IsDBNull(1) ? text : sqliteDataReader.GetString(1));
				num++;
				if (!File.Exists(ctx.Paths.PersonalFilePath(text)))
				{
					num2++;
					if (list.Count < 8)
					{
						list.Add(item);
					}
				}
			}
		}
		return BuildSampleResult(ctx, "files.personal", CoreStrings.C15, num, num2, list, "SELECT COUNT(*) FROM Katalog WHERE SourceType = 'Personal'", conn);
	}

	private static DiagnosticResult BuildSampleResult(DiagnosticContext ctx, string idBase, string label, int sampled, int missing, List<string> examples, string totalSql, SqliteConnection conn)
	{
		long num = ScalarLong(conn, totalSql);
		if (sampled == 0)
		{
			return new DiagnosticResult
			{
				Id = idBase + ".none",
				Category = CoreStrings.C46,
				Title = CoreStrings.C34 + label,
				Severity = DiagnosticSeverity.Info,
				Detail = CoreStrings.C50
			};
		}
		DefaultInterpolatedStringHandler handler;
		if (missing == 0)
		{
			DiagnosticResult obj = new DiagnosticResult
			{
				Id = idBase + ".ok",
				Category = CoreStrings.C46,
				Title = CoreStrings.C35 + label + CoreStrings.C36,
				Severity = DiagnosticSeverity.Ok
			};
			handler = new DefaultInterpolatedStringHandler(2, 4);
			handler.AppendFormatted(CoreStrings.C37);
			handler.AppendFormatted(sampled, "N0");
			handler.AppendFormatted(CoreStrings.C38);
			handler.AppendFormatted(num, "N0");
			handler.AppendLiteral(").");
			obj.Detail = handler.ToStringAndClear();
			return obj;
		}
		double num2 = (double)missing / (double)sampled;
		long value = (long)Math.Round(num2 * (double)num);
		DiagnosticResult obj2 = new DiagnosticResult
		{
			Id = idBase + ".missing",
			Category = CoreStrings.C46,
			Title = CoreStrings.C39 + label,
			Severity = DiagnosticSeverity.Warning
		};
		IFormatProvider invariantCulture = CultureInfo.InvariantCulture;
		handler = new DefaultInterpolatedStringHandler(3, 11, invariantCulture);
		handler.AppendFormatted(CoreStrings.C40);
		handler.AppendFormatted(sampled, "N0");
		handler.AppendFormatted(CoreStrings.C41);
		handler.AppendFormatted(missing, "N0");
		handler.AppendFormatted(CoreStrings.C42);
		handler.AppendFormatted(num2, "P0");
		handler.AppendLiteral("). ");
		handler.AppendFormatted(CoreStrings.C43);
		handler.AppendFormatted(value, "N0");
		handler.AppendFormatted(CoreStrings.C44);
		handler.AppendFormatted(num, "N0");
		handler.AppendFormatted(CoreStrings.C45);
		obj2.Detail = string.Create(invariantCulture, ref handler);
		obj2.Evidence = ((examples.Count == 0) ? null : (CoreStrings.C51 + string.Join("\n", examples)));
		return obj2;
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
