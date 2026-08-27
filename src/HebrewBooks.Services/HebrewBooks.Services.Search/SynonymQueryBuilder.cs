using System;
using System.Collections.Generic;
using System.Linq;

namespace HebrewBooks.Services.Search;

public static class SynonymQueryBuilder
{
	public static string Build(string query, IReadOnlyDictionary<string, IReadOnlyList<string>> selBySource, QueryBuildOptions opts)
	{
		if (selBySource.Count == 0)
		{
			return QueryBuilder.Build(query, opts);
		}
		IReadOnlyList<string> readOnlyList = QueryBuilder.SplitTerms(query);
		List<string> list = new List<string>();
		int num = 0;
		while (num < readOnlyList.Count)
		{
			bool flag = false;
			for (int num2 = Math.Min(readOnlyList.Count - num, 5); num2 >= 1; num2--)
			{
				string text = string.Join(' ', readOnlyList.Skip(num).Take(num2));
				if (selBySource.TryGetValue(text.Replace("\"", ""), out IReadOnlyList<string> value) && value.Count > 0)
				{
					List<string> list2 = new List<string> { "(" + QueryBuilder.Build(text, opts) + ")" };
					foreach (string item in value)
					{
						list2.Add(SynUnit(item, opts));
					}
					list.Add("(" + string.Join(" OR ", list2) + ")");
					num += num2;
					flag = true;
					break;
				}
			}
			if (!flag)
			{
				list.Add("(" + QueryBuilder.Build(readOnlyList[num], opts) + ")");
				num++;
			}
		}
		if (list.Count == 1)
		{
			return list[0];
		}
		int num3 = Math.Max(1, opts.DefaultProximity);
		string text2 = (opts.RequireWordOrder ? "pre/" : "w/");
		string text3 = list[0];
		for (int i = 1; i < list.Count; i++)
		{
			text3 = "(" + text3 + " " + text2 + num3 + " " + list[i] + ")";
		}
		return text3;
	}

	private static string SynUnit(string syn, QueryBuildOptions opts)
	{
		syn = syn.Trim();
		if (!syn.Contains(' '))
		{
			return "(" + QueryBuilder.Build(syn, opts) + ")";
		}
		return QueryBuilder.RenderPhrase(syn, opts.AddPrefixLetters);
	}
}
