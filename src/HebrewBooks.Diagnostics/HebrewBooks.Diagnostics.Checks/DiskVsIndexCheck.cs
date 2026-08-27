using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HebrewBooks.Core.Resources;
using HebrewBooks.Search.Incremental;

namespace HebrewBooks.Diagnostics.Checks;

public sealed class DiskVsIndexCheck : IDiagnosticCheck
{
	private static readonly EnumerationOptions EnumOpts = new EnumerationOptions
	{
		RecurseSubdirectories = true,
		IgnoreInaccessible = true
	};

	public string Id => "diskindex";

	public string Category => CoreStrings.C123;

	public Task<IReadOnlyList<DiagnosticResult>> RunAsync(DiagnosticContext ctx, CancellationToken ct)
	{
		List<DiagnosticResult> list = new List<DiagnosticResult>();
		if (ctx.Paths == null)
		{
			return Task.FromResult((IReadOnlyList<DiagnosticResult>)list);
		}
		list.AddRange(CheckCorpus(ctx, CoreStrings.C49, ctx.Paths.PdfsRoot, ctx.Paths.IndexesRoot, "*.pdf", "index.pdf", ct));
		list.AddRange(CheckCorpus(ctx, CoreStrings.C124, ctx.Paths.OtzrayaRoot, ctx.Paths.OtzrayaIndexPath, "*.txt", "index.otzraya", ct));
		list.AddRange(CheckCorpus(ctx, CoreStrings.C15, ctx.Paths.PersonalRoot, ctx.Paths.PersonalIndexPath, "*.pdf", "index.personal", ct));
		return Task.FromResult((IReadOnlyList<DiagnosticResult>)list);
	}

	private static IEnumerable<DiagnosticResult> CheckCorpus(DiagnosticContext ctx, string label, string sourceFolder, string indexPath, string pattern, string idBase, CancellationToken ct)
	{
		bool flag = Directory.Exists(sourceFolder);
		bool flag2 = Directory.Exists(indexPath);
		if (!flag && !flag2)
		{
			yield break;
		}
		long num = (flag ? CountFiles(sourceFolder, pattern, ct) : 0);
		IndexManifest? indexManifest = IndexManifest.Load(indexPath);
		long num2 = indexManifest?.Entries.Count ?? 0;
		string evidence = $"{CoreStrings.C105}{num:N0}{CoreStrings.C106}{pattern}{CoreStrings.C107}{num2:N0}{CoreStrings.C108}{sourceFolder}\n{indexPath}";
		if (indexManifest == null || !flag2)
		{
			if (num > 0)
			{
				yield return new DiagnosticResult
				{
					Id = idBase + ".disk.noindex",
					Category = CoreStrings.C123,
					Title = $"{CoreStrings.C109}{label} — {num:N0}{CoreStrings.C110}",
					Severity = DiagnosticSeverity.Warning,
					Detail = CoreStrings.C125,
					Evidence = evidence,
					Fix = BuildFix(idBase, label)
				};
			}
			yield break;
		}
		long num3 = num - num2;
		long num4 = num2 - num;
		if (num3 > 0)
		{
			yield return new DiagnosticResult
			{
				Id = idBase + ".disk.gap",
				Category = CoreStrings.C123,
				Title = $"{num3:N0}{CoreStrings.C111}{label})",
				Severity = DiagnosticSeverity.Warning,
				Detail = $"{CoreStrings.C112}{num:N0}{CoreStrings.C113}{num2:N0}{CoreStrings.C114}",
				Evidence = evidence,
				Fix = BuildFix(idBase, label)
			};
		}
		else if (num4 > 0)
		{
			yield return new DiagnosticResult
			{
				Id = idBase + ".disk.stale",
				Category = CoreStrings.C123,
				Title = $"{CoreStrings.C115}{num4:N0}{CoreStrings.C116}{label})",
				Severity = DiagnosticSeverity.Info,
				Detail = $"{CoreStrings.C115}{num2:N0}{CoreStrings.C117}{num:N0}{CoreStrings.C118}",
				Evidence = evidence
			};
		}
		else
		{
			yield return new DiagnosticResult
			{
				Id = idBase + ".disk.ok",
				Category = CoreStrings.C123,
				Title = CoreStrings.C119 + label + ")",
				Severity = DiagnosticSeverity.Ok,
				Detail = $"{CoreStrings.C120}{num:N0}{CoreStrings.C121}",
				Evidence = evidence
			};
		}
	}

	private static long CountFiles(string folder, string pattern, CancellationToken ct)
	{
		long num = 0L;
		foreach (string item in Directory.EnumerateFiles(folder, pattern, EnumOpts))
		{
			_ = item;
			ct.ThrowIfCancellationRequested();
			num++;
		}
		return num;
	}

	private static DiagnosticFix BuildFix(string idBase, string label)
	{
		return new DiagnosticFix
		{
			Id = idBase + ".build",
			Label = CoreStrings.C122 + label + ")",
			Kind = FixKind.AppOnly,
			Run = null
		};
	}
}
