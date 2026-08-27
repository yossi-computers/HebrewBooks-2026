using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using HebrewBooks.Core.Resources;

namespace HebrewBooks.Diagnostics;

public sealed class DiagnosticReport
{
	public required DateTime GeneratedAtUtc { get; init; }

	public required string AppVersion { get; init; }

	public required string Os { get; init; }

	public required IReadOnlyList<DiagnosticResult> Results { get; init; }

	public DiagnosticSeverity Worst
	{
		get
		{
			if (Results.Count != 0)
			{
				return Results.Max((DiagnosticResult r) => r.Severity);
			}
			return DiagnosticSeverity.Ok;
		}
	}

	public int CountOf(DiagnosticSeverity severity)
	{
		return Results.Count((DiagnosticResult r) => r.Severity == severity);
	}

	public string ToText()
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine(CoreStrings.C31);
		IFormatProvider invariantCulture = CultureInfo.InvariantCulture;
		IFormatProvider provider = invariantCulture;
		DefaultInterpolatedStringHandler handler = new DefaultInterpolatedStringHandler(0, 2, invariantCulture);
		handler.AppendFormatted(CoreStrings.C27);
		handler.AppendFormatted(GeneratedAtUtc, "yyyy-MM-dd HH:mm:ss");
		stringBuilder.AppendLine(string.Create(provider, ref handler));
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder3 = stringBuilder2;
		StringBuilder.AppendInterpolatedStringHandler handler2 = new StringBuilder.AppendInterpolatedStringHandler(0, 2, stringBuilder2);
		handler2.AppendFormatted(CoreStrings.C28);
		handler2.AppendFormatted(AppVersion);
		stringBuilder3.AppendLine(ref handler2);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder4 = stringBuilder2;
		handler2 = new StringBuilder.AppendInterpolatedStringHandler(0, 2, stringBuilder2);
		handler2.AppendFormatted(CoreStrings.C29);
		handler2.AppendFormatted(Os);
		stringBuilder4.AppendLine(ref handler2);
		invariantCulture = CultureInfo.InvariantCulture;
		IFormatProvider provider2 = invariantCulture;
		handler = new DefaultInterpolatedStringHandler(22, 5, invariantCulture);
		handler.AppendFormatted(CoreStrings.C30);
		handler.AppendFormatted(CountOf(DiagnosticSeverity.Ok));
		handler.AppendLiteral("  INFO=");
		handler.AppendFormatted(CountOf(DiagnosticSeverity.Info));
		handler.AppendLiteral("  ");
		handler.AppendLiteral("WARN=");
		handler.AppendFormatted(CountOf(DiagnosticSeverity.Warning));
		handler.AppendLiteral("  ERROR=");
		handler.AppendFormatted(CountOf(DiagnosticSeverity.Error));
		stringBuilder.AppendLine(string.Create(provider2, ref handler));
		stringBuilder.AppendLine();
		foreach (DiagnosticResult result in Results)
		{
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder5 = stringBuilder2;
			handler2 = new StringBuilder.AppendInterpolatedStringHandler(6, 3, stringBuilder2);
			handler2.AppendLiteral("[");
			handler2.AppendFormatted(Tag(result.Severity));
			handler2.AppendLiteral("] (");
			handler2.AppendFormatted(result.Category);
			handler2.AppendLiteral(") ");
			handler2.AppendFormatted(result.Title);
			stringBuilder5.AppendLine(ref handler2);
			if (!string.IsNullOrWhiteSpace(result.Detail))
			{
				stringBuilder.AppendLine("    " + result.Detail.Replace("\n", "\n    "));
			}
			if (!string.IsNullOrWhiteSpace(result.Evidence))
			{
				stringBuilder.AppendLine("    » " + result.Evidence.Replace("\n", "\n      "));
			}
		}
		return stringBuilder.ToString();
	}

	private static string Tag(DiagnosticSeverity s)
	{
		return s switch
		{
			DiagnosticSeverity.Ok => "OK  ", 
			DiagnosticSeverity.Info => "INFO", 
			DiagnosticSeverity.Warning => "WARN", 
			DiagnosticSeverity.Error => "FAIL", 
			_ => "????", 
		};
	}
}
