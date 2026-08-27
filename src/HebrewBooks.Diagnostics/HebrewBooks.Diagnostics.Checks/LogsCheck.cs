using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HebrewBooks.Core.Resources;

namespace HebrewBooks.Diagnostics.Checks;

public sealed partial class LogsCheck : IDiagnosticCheck
{
	private const int TailLines = 1500;

	private const int RepeatThreshold = 25;

	public string Id => "logs";

	public string Category => CoreStrings.C191;

	[GeneratedRegex("\\[(ERR|FTL)\\]", RegexOptions.CultureInvariant)]
	private static partial Regex ErrorLineRegex();

	[GeneratedRegex("\\[WRN\\]", RegexOptions.CultureInvariant)]
	private static partial Regex WarnLineRegex();

	[GeneratedRegex("fallback|degrad|read-?only|corrupt|truncat|time(d\\s*out|out)|retry|retrying|SQLITE_BUSY|IOERR|out\\s*of\\s*memory|OutOfMemory|\\bOOM\\b|zombie|unhandled|crash|could not|unable to|access denied|נכשל|כשל|חסר|לא נמצא|פגום", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
	private static partial Regex NotableRegex();

	[GeneratedRegex("^\\s*\\d{4}-\\d{2}-\\d{2}[ T]\\d{2}:\\d{2}:\\d{2}[.,]\\d+\\s*(?:[+\\-]\\d{2}:\\d{2}\\s*)?\\[[A-Z]{3}\\]\\s*", RegexOptions.CultureInvariant)]
	private static partial Regex PrefixRegex();

	public Task<IReadOnlyList<DiagnosticResult>> RunAsync(DiagnosticContext ctx, CancellationToken ct)
	{
		List<DiagnosticResult> list = new List<DiagnosticResult>();
		string directoryName = Path.GetDirectoryName(ctx.Settings.SettingsPath);
		string text = ((directoryName == null) ? null : Path.Combine(directoryName, "logs"));
		if (text == null || !Directory.Exists(text))
		{
			list.Add(Info("logs.none", CoreStrings.C192, CoreStrings.C193, text));
			return Task.FromResult((IReadOnlyList<DiagnosticResult>)list);
		}
		FileInfo fileInfo = (from f in new DirectoryInfo(text).GetFiles("bookshelf-*.log")
			orderby f.LastWriteTimeUtc descending
			select f).FirstOrDefault();
		if (fileInfo == null)
		{
			list.Add(Info("logs.empty", CoreStrings.C194, CoreStrings.C195, text));
			return Task.FromResult((IReadOnlyList<DiagnosticResult>)list);
		}
		List<string> list2 = ReadTail(fileInfo.FullName, 1500);
		List<string> list3 = list2.Where((string l) => ErrorLineRegex().IsMatch(l)).ToList();
		List<string> list4 = list2.Where((string l) => WarnLineRegex().IsMatch(l)).ToList();
		List<string> list5 = list2.Where((string l) => NotableRegex().IsMatch(l) && !ErrorLineRegex().IsMatch(l) && !WarnLineRegex().IsMatch(l)).ToList();
		bool flag = false;
		if (list3.Count > 0)
		{
			flag = true;
			list.Add(new DiagnosticResult
			{
				Id = "logs.errors",
				Category = Category,
				Title = $"{CoreStrings.C180}{list3.Count:N0}{CoreStrings.C181}",
				Severity = DiagnosticSeverity.Warning,
				Detail = CoreStrings.C182 + fileInfo.Name + CoreStrings.C183,
				Evidence = Join(list3.TakeLast(6))
			});
		}
		if (list4.Count > 0)
		{
			flag = true;
			list.Add(new DiagnosticResult
			{
				Id = "logs.warnings",
				Category = Category,
				Title = $"{CoreStrings.C180}{list4.Count:N0}{CoreStrings.C184}",
				Severity = DiagnosticSeverity.Info,
				Detail = CoreStrings.C196,
				Evidence = Join(list4.TakeLast(6))
			});
		}
		if (list5.Count > 0)
		{
			flag = true;
			list.Add(new DiagnosticResult
			{
				Id = "logs.notable",
				Category = Category,
				Title = $"{CoreStrings.C180}{list5.Count:N0}{CoreStrings.C185}",
				Severity = DiagnosticSeverity.Info,
				Detail = CoreStrings.C197 + CoreStrings.C198,
				Evidence = Join(list5.TakeLast(8))
			});
		}
		var (s, num) = MostRepeated(list2);
		if (num >= 25)
		{
			flag = true;
			list.Add(new DiagnosticResult
			{
				Id = "logs.repeated",
				Category = Category,
				Title = $"{CoreStrings.C186}{num:N0}{CoreStrings.C187}",
				Severity = DiagnosticSeverity.Info,
				Detail = CoreStrings.C199,
				Evidence = Truncate(s, 300)
			});
		}
		if (!flag)
		{
			list.Add(new DiagnosticResult
			{
				Id = "logs.clean",
				Category = Category,
				Title = CoreStrings.C200,
				Severity = DiagnosticSeverity.Ok,
				Detail = $"{CoreStrings.C188}{list2.Count:N0}{CoreStrings.C189}{fileInfo.Name}{CoreStrings.C190}",
				Evidence = fileInfo.FullName
			});
		}
		return Task.FromResult((IReadOnlyList<DiagnosticResult>)list);
	}

	private DiagnosticResult Info(string id, string title, string detail, string? evidence)
	{
		return new DiagnosticResult
		{
			Id = id,
			Category = Category,
			Title = title,
			Severity = DiagnosticSeverity.Info,
			Detail = detail,
			Evidence = evidence
		};
	}

	private static string Join(IEnumerable<string> lines)
	{
		return string.Join("\n", lines);
	}

	private static string Truncate(string s, int max)
	{
		if (s.Length > max)
		{
			return s.Substring(0, max) + "…";
		}
		return s;
	}

	private static (string message, int count) MostRepeated(List<string> tail)
	{
		Dictionary<string, int> dictionary = new Dictionary<string, int>(StringComparer.Ordinal);
		foreach (string item in tail)
		{
			string text = PrefixRegex().Replace(item, "").Trim();
			if (text.Length >= 8)
			{
				dictionary[text] = ((!dictionary.TryGetValue(text, out var value)) ? 1 : (value + 1));
			}
		}
		if (dictionary.Count == 0)
		{
			return (message: "", count: 0);
		}
		KeyValuePair<string, int> keyValuePair = dictionary.MaxBy((KeyValuePair<string, int> kv) => kv.Value);
		return (message: keyValuePair.Key, count: keyValuePair.Value);
	}

	private static List<string> ReadTail(string path, int maxLines)
	{
		try
		{
			using FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			using StreamReader streamReader = new StreamReader(stream);
			LinkedList<string> linkedList = new LinkedList<string>();
			string value;
			while ((value = streamReader.ReadLine()) != null)
			{
				linkedList.AddLast(value);
				if (linkedList.Count > maxLines)
				{
					linkedList.RemoveFirst();
				}
			}
			return linkedList.ToList();
		}
		catch
		{
			return new List<string>();
		}
	}
}
