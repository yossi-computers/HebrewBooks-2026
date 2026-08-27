using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace HebrewBooks.Services.Search;

public static class QueryBuilder
{
	private readonly record struct Alt(string Text, int Tier);

	private readonly record struct Segment(string Text, bool IsQuoted, bool IsGroup);

	public const string FirstWordMarker = "▶";

	public const string LastWordMarker = "◀";

	private static readonly Regex SegmentPattern = new Regex("\"[^\"]*\"|\\([^)]*\\)|\\S+", RegexOptions.Compiled);

	private static readonly Regex GroupMemberPattern = new Regex("\"[^\"]*\"|\\S+", RegexOptions.Compiled);

	private static readonly Regex OperatorPattern = new Regex("\\b(AND|OR|NOT|w/\\d+|xfilter|xfirstword|xlastword)\\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

	private static readonly string[] HebrewPrefixes = new string[8] { "ב", "ד", "ה", "ו", "כ", "ל", "מ", "ש" };

	private const string HebrewPrefixLetters = "בדהוכלמש";

	private const int TierWord = 0;

	private const int TierPrefix = 1;

	private const int TierAram = 2;

	private const int TierRashey = 3;

	private const int TierNumber = 4;

	private const int TierRoot = 5;

	private const int TierSpelling = 6;

	private const int TierWeak = 7;

	private const int TierRashiOcr = 8;

	private static readonly char[] AcronymMarks = new char[4] { '״', '׳', '"', '\'' };

	private static readonly HashSet<string> _searchOperatorBlacklist = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "AND", "OR", "NOT", "AND_NOT", "ANDNOT", "xfilter", "xfirstword", "xlastword", "contains", "name" };

	public static IReadOnlyList<string> SplitTerms(string input)
	{
		if (string.IsNullOrWhiteSpace(input))
		{
			return Array.Empty<string>();
		}
		return (from m in SegmentPattern.Matches(NormalizeQuotes(input).Trim())
			select m.Value).ToList();
	}

	public static string Build(string input, QueryBuildOptions options)
	{
		if (string.IsNullOrWhiteSpace(input))
		{
			return string.Empty;
		}
		string text = NormalizeQuotes(input).Trim();
		bool flag = options.FirstWordOnly;
		bool flag2 = options.LastWordOnly;
		int value;
		if (text.StartsWith("▶", StringComparison.Ordinal))
		{
			flag = true;
			string text2 = text;
			value = "▶".Length;
			text = text2.Substring(value, text2.Length - value).TrimStart();
		}
		if (text.EndsWith("◀", StringComparison.Ordinal))
		{
			flag2 = true;
			string text2 = text;
			value = "◀".Length;
			text = text2.Substring(0, text2.Length - value).TrimEnd();
		}
		if (string.IsNullOrEmpty(text))
		{
			return string.Empty;
		}
		if (OperatorPattern.IsMatch(text))
		{
			return text;
		}
		List<Segment> list = new List<Segment>();
		foreach (Match item3 in SegmentPattern.Matches(text))
		{
			string value2 = item3.Value;
			int num;
			if (value2.Length >= 2 && value2[0] == '"')
			{
				num = ((value2[value2.Length - 1] == '"') ? 1 : 0);
			}
			else
			{
				num = 0;
			}
			bool flag3 = (byte)num != 0;
			int num2;
			if (!flag3 && value2.Length >= 2 && value2[0] == '(')
			{
				num2 = ((value2[value2.Length - 1] == ')') ? 1 : 0);
			}
			else
			{
				num2 = 0;
			}
			bool flag4 = (byte)num2 != 0;
			string text3;
			if (!(flag3 || flag4))
			{
				text3 = value2;
			}
			else
			{
				string text2 = value2;
				text3 = text2.Substring(1, text2.Length - 1 - 1);
			}
			string text4 = text3;
			list.Add(new Segment(text4, flag3, flag4));
		}
		if (list.Count == 0)
		{
			return string.Empty;
		}
		int item = Math.Max(1, options.DefaultProximity);
		List<Segment> list2 = new List<Segment>(list.Count);
		List<int> joins = new List<int>(list.Count);
		for (int i = 0; i < list.Count; i++)
		{
			Segment item2 = list[i];
			if (!item2.IsQuoted && !item2.IsGroup && list2.Count > 0 && i + 1 < list.Count && IsPlainInteger(item2.Text, out var value3) && !IsPlainInteger(list[i + 1].Text, out value))
			{
				List<int> list3 = joins;
				list3[list3.Count - 1] = Math.Max(1, value3);
			}
			else
			{
				list2.Add(item2);
				joins.Add(item);
			}
		}
		if (list2.Count == 0)
		{
			return string.Empty;
		}
		string[] array = new string[list2.Count];
		List<Alt>[] array2 = new List<Alt>[list2.Count];
		for (int j = 0; j < list2.Count; j++)
		{
			Segment segment = list2[j];
			if (segment.IsQuoted)
			{
				array[j] = RenderPhrase(segment.Text);
			}
			else if (segment.IsGroup)
			{
				List<string> list4 = (from Match m in GroupMemberPattern.Matches(segment.Text)
					select m.Value).ToList();
				if (list4.Count != 0)
				{
					array[j] = ((list4.Count == 1) ? RenderMember(list4[0]) : ("(" + string.Join(" OR ", list4.Select(RenderMember)) + ")"));
				}
			}
			else
			{
				array2[j] = ExpandAlts(segment.Text, options);
			}
		}
		if (options.IndexWordFilter != null)
		{
			HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
			List<Alt>[] array3 = array2;
			foreach (List<Alt> list5 in array3)
			{
				if (list5 == null)
				{
					continue;
				}
				foreach (Alt item4 in list5)
				{
					if (item4.Tier != 0 && IsSingleBareWord(item4.Text))
					{
						hashSet.Add(item4.Text);
					}
				}
			}
			if (hashSet.Count > 0)
			{
				ISet<string> existing;
				try
				{
					existing = options.IndexWordFilter(hashSet);
				}
				catch
				{
					existing = hashSet;
				}
				for (int num3 = 0; num3 < array2.Length; num3++)
				{
					List<Alt> list6 = array2[num3];
					if (list6 != null)
					{
						array2[num3] = list6.Where((Alt a) => a.Tier == 0 || !IsSingleBareWord(a.Text) || existing.Contains(a.Text)).ToList();
					}
				}
			}
		}
		if (list2.Count >= 4)
		{
			ApplyBudget(array, array2, Math.Max(1, options.MaxTotalExpansions));
		}
		List<string> list7 = new List<string>(list2.Count);
		List<int> clauseTermIndex = new List<int>(list2.Count);
		for (int num4 = 0; num4 < list2.Count; num4++)
		{
			object obj2 = array[num4];
			if (obj2 == null)
			{
				List<Alt> list8 = array2[num4];
				obj2 = ((list8 != null && list8.Count > 0) ? RenderAlts(list8) : null);
			}
			string text5 = (string)obj2;
			if (text5 != null)
			{
				if (flag && num4 == 0)
				{
					text5 = "xfirstword(" + text5 + ")";
				}
				if (flag2 && num4 == list2.Count - 1)
				{
					text5 = "xlastword(" + text5 + ")";
				}
				list7.Add(text5);
				clauseTermIndex.Add(num4);
			}
		}
		if (list7.Count == 0)
		{
			return string.Empty;
		}
		if (list7.Count == 1)
		{
			return list7[0];
		}
		string value4 = (options.RequireWordOrder ? "pre/" : "w/");
		if (list7.Count <= 3)
		{
			StringBuilder stringBuilder = new StringBuilder(list7[0]);
			for (int num5 = 1; num5 < list7.Count; num5++)
			{
				stringBuilder.Append(' ').Append(value4).Append(Join(num5))
					.Append(' ')
					.Append(list7[num5]);
			}
			return stringBuilder.ToString();
		}
		string text6 = list7[0];
		for (int num6 = 1; num6 < list7.Count; num6++)
		{
			text6 = $"({text6} {value4}{Join(num6)} {list7[num6]})";
		}
		return text6;
		int Join(int index)
		{
			return joins[clauseTermIndex[index] - 1];
		}
		string RenderMember(string p)
		{
			if (p.Length >= 2 && p[0] == '"')
			{
				if (p[p.Length - 1] == '"')
				{
					return RenderPhrase(p.Substring(1, p.Length - 1 - 1));
				}
			}
			return RenderAlts(ExpandAlts(p, options));
		}
	}

	public static int CountMatchWords(string input)
	{
		if (string.IsNullOrWhiteSpace(input))
		{
			return 1;
		}
		string text = NormalizeQuotes(input).Trim();
		int value;
		if (text.StartsWith("▶", StringComparison.Ordinal))
		{
			string text2 = text;
			value = "▶".Length;
			text = text2.Substring(value, text2.Length - value).TrimStart();
		}
		if (text.EndsWith("◀", StringComparison.Ordinal))
		{
			string text2 = text;
			value = "◀".Length;
			text = text2.Substring(0, text2.Length - value).TrimEnd();
		}
		if (text.Length == 0)
		{
			return 1;
		}
		if (OperatorPattern.IsMatch(text))
		{
			return 1;
		}
		List<Segment> list = new List<Segment>();
		foreach (Match item in SegmentPattern.Matches(text))
		{
			string value2 = item.Value;
			int num;
			if (value2.Length >= 2 && value2[0] == '"')
			{
				num = ((value2[value2.Length - 1] == '"') ? 1 : 0);
			}
			else
			{
				num = 0;
			}
			bool flag = (byte)num != 0;
			int num2;
			if (!flag && value2.Length >= 2 && value2[0] == '(')
			{
				num2 = ((value2[value2.Length - 1] == ')') ? 1 : 0);
			}
			else
			{
				num2 = 0;
			}
			bool flag2 = (byte)num2 != 0;
			string text3;
			if (!(flag || flag2))
			{
				text3 = value2;
			}
			else
			{
				string text2 = value2;
				text3 = text2.Substring(1, text2.Length - 1 - 1);
			}
			string text4 = text3;
			list.Add(new Segment(text4, flag, flag2));
		}
		int num3 = 0;
		for (int i = 0; i < list.Count; i++)
		{
			Segment segment = list[i];
			if (segment.IsQuoted || segment.IsGroup || num3 <= 0 || i + 1 >= list.Count || !IsPlainInteger(segment.Text, out value) || IsPlainInteger(list[i + 1].Text, out value))
			{
				num3 += ((!segment.IsQuoted) ? 1 : Math.Max(1, segment.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length));
			}
		}
		return Math.Max(1, num3);
	}

	private static bool IsPlainInteger(string s, out int value)
	{
		value = 0;
		if (!string.IsNullOrEmpty(s) && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
		{
			return value > 0;
		}
		return false;
	}

	private static List<Alt> ExpandAlts(string word, QueryBuildOptions o)
	{
		bool? flag = null;
		bool flag2 = false;
		if (word.Length > 1 && word[0] == '=')
		{
			flag = false;
			string text = word;
			word = text.Substring(1, text.Length - 1);
		}
		else if (word.Length > 1 && word[0] == '+')
		{
			flag = true;
			string text = word;
			word = text.Substring(1, text.Length - 1);
		}
		else if (word.Length > 1 && word[0] == '#')
		{
			flag2 = true;
			string text = word;
			word = text.Substring(1, text.Length - 1);
		}
		string[] array = SplitAcronym(word);
		bool flag3 = array != null;
		word = StripGershayim(word);
		bool flag4 = IsExpandableWord(word);
		bool flag5 = (flag ?? o.AddPrefixLetters) && flag4;
		List<Alt> alts = new List<Alt>();
		HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
		TryAdd(word, 0);
		if (flag3)
		{
			string text2 = string.Join(' ', array);
			TryAdd("\"" + text2 + "\"", 0);
			if (flag5)
			{
				string[] hebrewPrefixes = HebrewPrefixes;
				foreach (string text3 in hebrewPrefixes)
				{
					TryAdd("\"" + text3 + text2 + "\"", 1);
				}
			}
		}
		IReadOnlyList<string> readOnlyList = null;
		if (o.RasheyTevot != null && o.RasheyTevot.Count > 0 && flag4 && flag != false && o.RasheyTevot.TryGet(word, out IReadOnlyList<string> expansions) && expansions.Count > 0)
		{
			readOnlyList = expansions;
		}
		IReadOnlyList<string> readOnlyList2 = null;
		if ((o.ExpandRoots || flag2) && flag4 && !flag3 && flag != false)
		{
			IReadOnlyList<string> readOnlyList3 = HebrewMorph.ExpandForQuery(word);
			if (readOnlyList3.Count > 1)
			{
				readOnlyList2 = readOnlyList3;
			}
		}
		IReadOnlyList<string> readOnlyList4 = null;
		if (o.ExpandNumberGender && flag4 && !flag3 && flag != false)
		{
			IReadOnlyList<string> readOnlyList5 = HebrewNumbers.ExpandGender(word);
			if (readOnlyList5 != null && readOnlyList5.Count > 1)
			{
				readOnlyList4 = readOnlyList5;
			}
		}
		IReadOnlyList<string> readOnlyList6 = null;
		if (o.ExpandGematria && flag4 && !flag3 && flag != false)
		{
			IReadOnlyList<string> readOnlyList7 = HebrewNumbers.ExpandGematria(word);
			if (readOnlyList7 != null && readOnlyList7.Count > 1)
			{
				readOnlyList6 = readOnlyList7;
			}
		}
		IReadOnlyList<string> readOnlyList8 = null;
		if (o.ExpandSpelling && flag4 && !flag3 && flag != false)
		{
			IReadOnlyList<string> readOnlyList9 = HebrewSpelling.Expand(word);
			if (readOnlyList9.Count > 1)
			{
				readOnlyList8 = readOnlyList9;
			}
		}
		IReadOnlyList<string> readOnlyList10 = null;
		if (o.ExpandWeakLetters && flag4 && !flag3 && flag != false)
		{
			IReadOnlyList<string> readOnlyList11 = HebrewWeakLetters.Expand(word);
			if (readOnlyList11.Count > 1)
			{
				readOnlyList10 = readOnlyList11;
			}
		}
		IReadOnlyList<string> readOnlyList12 = null;
		if (o.Aramaic != null && o.Aramaic.Count > 0 && flag4 && !flag3 && flag != false)
		{
			IReadOnlyList<string> readOnlyList13 = o.Aramaic.ExpandForQuery(word);
			if (readOnlyList13 != null && readOnlyList13.Count > 1)
			{
				readOnlyList12 = readOnlyList13;
			}
		}
		IReadOnlyList<string> readOnlyList14 = null;
		if (o.ExpandRashiOcr && flag4 && !flag3 && flag != false)
		{
			IReadOnlyList<string> readOnlyList15 = new RashiOcrMap().ExpandForQuery(word);
			if (readOnlyList15.Count > 1)
			{
				readOnlyList14 = readOnlyList15;
			}
		}
		if (flag5)
		{
			string[] hebrewPrefixes = HebrewPrefixes;
			for (int i = 0; i < hebrewPrefixes.Length; i++)
			{
				TryAdd(hebrewPrefixes[i] + word, 1);
			}
		}
		if (readOnlyList != null)
		{
			foreach (string item in readOnlyList)
			{
				string text4 = item.Trim();
				if (text4.Length == 0)
				{
					continue;
				}
				if (text4.IndexOf(' ') >= 0)
				{
					TryAdd(RenderPhrase(text4), 3);
				}
				else if (flag5)
				{
					TryAdd(text4, 3);
					string[] hebrewPrefixes = HebrewPrefixes;
					for (int i = 0; i < hebrewPrefixes.Length; i++)
					{
						TryAdd(hebrewPrefixes[i] + text4, 3);
					}
				}
				else
				{
					TryAdd(text4, 3);
				}
			}
		}
		if (readOnlyList2 != null)
		{
			foreach (string item2 in readOnlyList2)
			{
				TryAdd(item2, 5);
			}
		}
		if (readOnlyList4 != null)
		{
			foreach (string item3 in readOnlyList4)
			{
				TryAdd(item3, 4);
			}
		}
		if (readOnlyList6 != null)
		{
			foreach (string item4 in readOnlyList6)
			{
				TryAdd(item4, 4);
			}
		}
		if (readOnlyList8 != null)
		{
			foreach (string item5 in readOnlyList8)
			{
				TryAdd(item5, 6);
			}
		}
		if (readOnlyList10 != null)
		{
			foreach (string item6 in readOnlyList10)
			{
				TryAdd(item6, 7);
			}
		}
		if (readOnlyList12 != null)
		{
			foreach (string item7 in readOnlyList12)
			{
				TryAdd(item7, 2);
			}
		}
		if (readOnlyList14 != null)
		{
			for (int j = 1; j < readOnlyList14.Count; j++)
			{
				TryAdd(readOnlyList14[j], 8);
			}
		}
		return alts;
		void TryAdd(string text5, int tier)
		{
			if (text5.Length > 0 && seen.Add(text5))
			{
				alts.Add(new Alt(text5, tier));
			}
		}
	}

	private static string RenderAlts(List<Alt> alts)
	{
		if (alts.Count != 1)
		{
			return "(" + string.Join(" OR ", alts.Select((Alt a) => a.Text)) + ")";
		}
		return alts[0].Text;
	}

	private static bool IsSingleBareWord(string s)
	{
		if (s.Length > 0 && s.IndexOf(' ') < 0 && s[0] != '"' && s[0] != '(')
		{
			return s.IndexOf('[') < 0;
		}
		return false;
	}

	private static int WordCount(string s)
	{
		int num = 0;
		string[] array = s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
		foreach (string text in array)
		{
			if (!text.Equals("OR", StringComparison.OrdinalIgnoreCase) && !text.StartsWith("w/", StringComparison.OrdinalIgnoreCase))
			{
				num++;
			}
		}
		return num;
	}

	private static void ApplyBudget(string?[] fixedClause, List<Alt>?[] flexAlts, int budget)
	{
		int num = 0;
		foreach (string text in fixedClause)
		{
			if (text != null)
			{
				num += WordCount(text);
			}
		}
		HashSet<string>[] keep = new HashSet<string>[flexAlts.Length];
		for (int j = 0; j < flexAlts.Length; j++)
		{
			List<Alt> list = flexAlts[j];
			if (list == null || list.Count == 0)
			{
				continue;
			}
			keep[j] = new HashSet<string>(StringComparer.Ordinal);
			foreach (Alt item in list)
			{
				if (item.Tier == 0 && keep[j].Add(item.Text))
				{
					num += WordCount(item.Text);
				}
			}
		}
		for (int k = 1; k <= 7; k++)
		{
			List<Alt>[] array = new List<Alt>[flexAlts.Length];
			int num2 = 0;
			for (int l = 0; l < flexAlts.Length; l++)
			{
				if (flexAlts[l] != null && keep[l] != null)
				{
					int t = k;
					array[l] = flexAlts[l].Where((Alt a) => a.Tier == t).ToList();
					if (array[l].Count > num2)
					{
						num2 = array[l].Count;
					}
				}
			}
			for (int num3 = 0; num3 < num2; num3++)
			{
				if (num >= budget)
				{
					break;
				}
				for (int num4 = 0; num4 < flexAlts.Length; num4++)
				{
					if (array[num4] == null || num3 >= array[num4].Count)
					{
						continue;
					}
					Alt alt = array[num4][num3];
					if (!keep[num4].Contains(alt.Text))
					{
						int num5 = WordCount(alt.Text);
						if (num + num5 <= budget)
						{
							keep[num4].Add(alt.Text);
							num += num5;
						}
					}
				}
			}
		}
		int i2;
		for (i2 = 0; i2 < flexAlts.Length; i2++)
		{
			if (flexAlts[i2] != null && keep[i2] != null)
			{
				flexAlts[i2] = flexAlts[i2].Where((Alt a) => keep[i2].Contains(a.Text)).ToList();
			}
		}
	}

	private static string NormalizeQuotes(string s)
	{
		if (string.IsNullOrEmpty(s))
		{
			return s;
		}
		return s.Replace('“', '"').Replace('”', '"').Replace('″', '"')
			.Replace('‘', '\'')
			.Replace('’', '\'')
			.Replace('′', '\'');
	}

	public static string RenderPhrase(string phrase, bool addPrefixLetters = false)
	{
		string[] array = phrase.Split(new char[2] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
		if (array.Length == 0)
		{
			return "\"" + phrase + "\"";
		}
		List<string> forms = new List<string>();
		AddBothOrders(array);
		if (addPrefixLetters)
		{
			string text = "בדהוכלמש";
			foreach (char c in text)
			{
				string[] array2 = (string[])array.Clone();
				array2[0] = c + array2[0];
				AddBothOrders(array2);
			}
		}
		if (forms.Count != 1)
		{
			return "(" + string.Join(" OR ", forms) + ")";
		}
		return forms[0];
		void AddBothOrders(string[] w)
		{
			forms.Add("\"" + string.Join(' ', w) + "\"");
			if (w.Length >= 2)
			{
				string[] array3 = (string[])w.Clone();
				Array.Reverse(array3);
				forms.Add("\"" + string.Join(' ', array3) + "\"");
			}
		}
	}

	private static string[]? SplitAcronym(string word)
	{
		if (string.IsNullOrEmpty(word) || word.IndexOfAny(AcronymMarks) < 0)
		{
			return null;
		}
		string[] array = word.Split(AcronymMarks, StringSplitOptions.RemoveEmptyEntries);
		if (array.Length < 2)
		{
			return null;
		}
		return array;
	}

	private static string StripGershayim(string word)
	{
		if (word.Length == 0)
		{
			return word;
		}
		StringBuilder stringBuilder = new StringBuilder(word.Length);
		foreach (char c in word)
		{
			if (c != '״' && c != '׳' && c != '"' && c != '\'')
			{
				stringBuilder.Append(c);
			}
		}
		return stringBuilder.ToString();
	}

	private static bool IsExpandableWord(string s)
	{
		if (string.IsNullOrEmpty(s))
		{
			return false;
		}
		bool flag = true;
		foreach (char c in s)
		{
			bool flag2;
			switch (c)
			{
			case '(':
			case ')':
			case '*':
			case '?':
			case '~':
				flag2 = true;
				break;
			default:
				flag2 = false;
				break;
			}
			if (flag2)
			{
				return false;
			}
			if (!char.IsDigit(c))
			{
				flag = false;
			}
		}
		return !flag;
	}

	public static IReadOnlyList<string> ExtractHighlightTerms(string queryText, bool addPrefixes = false, bool expandRoots = false, bool expandNumberGender = false, bool expandGematria = false, bool expandSpelling = false, HebAramMap? aramaic = null, bool expandRashiOcr = false, bool dropPhraseConstituents = false, bool expandWeakLetters = false)
	{
		if (string.IsNullOrWhiteSpace(queryText))
		{
			return Array.Empty<string>();
		}
		string text = NormalizeQuotes(queryText).Trim().TrimStart('▶').TrimEnd('◀')
			.Trim();
		if (text.Length == 0)
		{
			return Array.Empty<string>();
		}
		List<string> result = new List<string>();
		HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
		foreach (Match item in SegmentPattern.Matches(text))
		{
			string value = item.Value;
			int num;
			if (value.Length >= 2 && value[0] == '"')
			{
				num = ((value[value.Length - 1] == '"') ? 1 : 0);
			}
			else
			{
				num = 0;
			}
			bool flag = (byte)num != 0;
			int num2;
			if (!flag && value.Length >= 2 && value[0] == '(')
			{
				num2 = ((value[value.Length - 1] == ')') ? 1 : 0);
			}
			else
			{
				num2 = 0;
			}
			bool flag2 = (byte)num2 != 0;
			string text2;
			if (!(flag || flag2))
			{
				text2 = value.Trim();
			}
			else
			{
				string text3 = value;
				text2 = text3.Substring(1, text3.Length - 1 - 1).Trim();
			}
			string text4 = text2;
			if (text4.Length == 0 || (!flag && !flag2 && (text4.StartsWith("w/", StringComparison.OrdinalIgnoreCase) || text4.StartsWith("pre/", StringComparison.OrdinalIgnoreCase) || _searchOperatorBlacklist.Contains(text4))))
			{
				continue;
			}
			if (flag)
			{
				Add(text4);
				if (!dropPhraseConstituents)
				{
					string[] array = text4.Split(new char[2] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
					for (int i = 0; i < array.Length; i++)
					{
						AddWord(array[i], addPrefixes);
					}
				}
			}
			else if (flag2)
			{
				string[] array = text4.Split(new char[2] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
				for (int i = 0; i < array.Length; i++)
				{
					var (word, flag3, forceRoot) = ParseExpansionOverride(array[i]);
					AddWord(word, flag3 ?? addPrefixes, forceRoot);
				}
			}
			else
			{
				var (word2, flag4, forceRoot2) = ParseExpansionOverride(text4);
				AddWord(word2, flag4 ?? addPrefixes, forceRoot2);
			}
		}
		return result;
		void Add(string term)
		{
			string text5 = term.Trim();
			if (text5.Length != 0)
			{
				bool flag5 = false;
				string text6 = text5;
				for (int j = 0; j < text6.Length; j++)
				{
					if (char.IsLetter(text6[j]))
					{
						flag5 = true;
						break;
					}
				}
				if (flag5 && seen.Add(text5))
				{
					result.Add(text5);
				}
			}
		}
		void AddWord(string text5, bool expand, bool flag5 = false)
		{
			Add(text5);
			if (expand && IsExpandableWord(text5))
			{
				string[] hebrewPrefixes = HebrewPrefixes;
				for (int j = 0; j < hebrewPrefixes.Length; j++)
				{
					Add(hebrewPrefixes[j] + text5);
				}
			}
			if ((expandRoots || flag5) && IsExpandableWord(text5))
			{
				foreach (string item2 in HebrewMorph.ExpandForHighlight(text5))
				{
					Add(item2);
				}
			}
			if ((expandNumberGender || expandGematria) && IsExpandableWord(text5))
			{
				string word3 = StripGershayim(text5);
				if (expandNumberGender)
				{
					IReadOnlyList<string> readOnlyList = HebrewNumbers.ExpandGender(word3);
					if (readOnlyList != null)
					{
						foreach (string item3 in readOnlyList)
						{
							Add(item3);
						}
					}
				}
				if (expandGematria)
				{
					IReadOnlyList<string> readOnlyList2 = HebrewNumbers.ExpandGematria(word3);
					if (readOnlyList2 != null)
					{
						foreach (string item4 in readOnlyList2)
						{
							Add(item4);
						}
					}
				}
			}
			if (expandSpelling && IsExpandableWord(text5))
			{
				foreach (string item5 in HebrewSpelling.Expand(StripGershayim(text5)))
				{
					Add(item5);
				}
			}
			if (expandWeakLetters && IsExpandableWord(text5))
			{
				foreach (string item6 in HebrewWeakLetters.Expand(StripGershayim(text5)))
				{
					Add(item6);
				}
			}
			if (aramaic != null && aramaic.Count > 0 && IsExpandableWord(text5))
			{
				string word4 = StripGershayim(text5);
				IReadOnlyList<string> readOnlyList3 = aramaic.ExpandForQuery(word4);
				if (readOnlyList3 != null)
				{
					foreach (string item7 in readOnlyList3)
					{
						Add(item7);
					}
				}
			}
			if (expandRashiOcr && IsExpandableWord(text5))
			{
				foreach (string item8 in new RashiOcrMap().ExpandForQuery(StripGershayim(text5)))
				{
					Add(item8);
				}
			}
		}
	}

	private static (string Word, bool? Force, bool ForceRoot) ParseExpansionOverride(string token)
	{
		if (token.Length > 1)
		{
			if (token[0] == '=')
			{
				string text = token;
				return (Word: text.Substring(1, text.Length - 1), Force: false, ForceRoot: false);
			}
			if (token[0] == '+')
			{
				string text = token;
				return (Word: text.Substring(1, text.Length - 1), Force: true, ForceRoot: false);
			}
			if (token[0] == '#')
			{
				string text = token;
				return (Word: text.Substring(1, text.Length - 1), Force: null, ForceRoot: true);
			}
		}
		return (Word: token, Force: null, ForceRoot: false);
	}
}
