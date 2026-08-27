using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HebrewBooks.Core.Abstractions;

namespace HebrewBooks.Services.Search;

public sealed class SpellSuggestionService(ISearchEngine engine, Action<string>? trace = null)
{
	public sealed record Correction(string Original, string Corrected);

	private readonly record struct Token(string Text, bool IsWord);

	private const long MinCandidateCount = 50L;

	private const long BoostFactor = 20L;

	private const int MinWordLength = 3;

	private const int MaxSuspects = 2;

	private const long RareCeiling = 25000L;

	private const int MaxInsertionLength = 8;

	private static readonly char[] Alphabet = (from c in Enumerable.Range(1488, 27)
		select (char)c).ToArray();

	private void Trace(string what, Stopwatch sw)
	{
		trace?.Invoke($"{what} {sw.ElapsedMilliseconds}ms");
	}

	public static async Task<Correction?> SuggestAsync(ISearchEngine engine, string? query, TimeSpan budget, CancellationToken ct = default(CancellationToken), Action<string>? trace = null)
	{
		using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
		cts.CancelAfter(budget);
		CancellationToken inner = cts.Token;
		try
		{
			return await Task.Run(() => new SpellSuggestionService(engine, trace).Suggest(query, inner), CancellationToken.None).WaitAsync(inner).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (OperationCanceledException)
		{
			return null;
		}
	}

	public Correction? Suggest(string? query, CancellationToken ct = default(CancellationToken))
	{
		if (string.IsNullOrWhiteSpace(query))
		{
			return null;
		}
		string text = query.Trim();
		if (text.AsSpan().IndexOfAny("\"()*?~▶◀") >= 0)
		{
			return null;
		}
		string text2 = HebrewKeyboard.TryRecover(text);
		if (text2 != null && !string.Equals(text2, text, StringComparison.Ordinal))
		{
			List<string> list = HebrewWords(text2);
			if (list.Count > 0)
			{
				IReadOnlyDictionary<string, long> counts = engine.GetIndexWordCounts(list);
				if (list.Any((string w) => counts.GetValueOrDefault(w) >= 50))
				{
					return new Correction(text, text2);
				}
			}
		}
		List<Token> list2 = Tokenize(text);
		List<string> list3 = (from t in list2
			where t.IsWord && t.Text.Length >= 3 && IsHebrew(t.Text)
			select t.Text).Distinct<string>(StringComparer.Ordinal).ToList();
		if (list3.Count == 0)
		{
			return null;
		}
		Stopwatch sw = Stopwatch.StartNew();
		IReadOnlyDictionary<string, long> typedCounts = engine.GetIndexWordCounts(list3);
		Trace($"typedCounts({list3.Count} words)", sw);
		if (typedCounts.Count == 0)
		{
			return null;
		}
		Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (string item in (from w in list3
			where typedCounts.GetValueOrDefault(w) < 25000
			orderby typedCounts.GetValueOrDefault(w)
			select w).Take(2))
		{
			if (ct.IsCancellationRequested)
			{
				return null;
			}
			string text3 = BestReplacement(item, typedCounts.GetValueOrDefault(item));
			if (text3 != null)
			{
				dictionary[item] = text3;
			}
		}
		if (dictionary.Count == 0)
		{
			return null;
		}
		StringBuilder stringBuilder = new StringBuilder(text.Length + 8);
		foreach (Token item2 in list2)
		{
			stringBuilder.Append((item2.IsWord && dictionary.TryGetValue(item2.Text, out var value)) ? value : item2.Text);
		}
		string text4 = stringBuilder.ToString();
		if (!string.Equals(text4, text, StringComparison.Ordinal))
		{
			return new Correction(text, text4);
		}
		return null;
	}

	private string? BestReplacement(string word, long typedCount)
	{
		long floor = Math.Max(50L, typedCount * 20);
		Dictionary<string, long> dictionary = new Dictionary<string, long>(StringComparer.Ordinal);
		HashSet<string> hashSet = Generate(word);
		if (hashSet.Count > 0)
		{
			Stopwatch sw = Stopwatch.StartNew();
			foreach (KeyValuePair<string, long> indexWordCount in engine.GetIndexWordCounts(hashSet))
			{
				if (indexWordCount.Value > 0)
				{
					dictionary[indexWordCount.Key] = indexWordCount.Value;
				}
			}
			Trace($"validate('{word}', {hashSet.Count} candidates)→{dictionary.Count}", sw);
		}
		return (from c in (from kv in dictionary
				where !string.Equals(kv.Key, word, StringComparison.Ordinal) && kv.Value >= floor
				select new
				{
					Word = kv.Key,
					Count = kv.Value,
					IsAnagram = IsAnagram(word, kv.Key),
					Distance = Distance(word, kv.Key),
					LengthDelta = Math.Abs(kv.Key.Length - word.Length)
				}).Where(c =>
			{
				int distance = c.Distance;
				return distance > 0 && distance <= 2;
			})
			orderby c.IsAnagram descending, c.Distance, c.LengthDelta, c.Count descending
			select c.Word).FirstOrDefault();
	}

	private static bool IsAnagram(string a, string b)
	{
		if (a.Length != b.Length || string.Equals(a, b, StringComparison.Ordinal))
		{
			return false;
		}
		char[] array = a.ToCharArray();
		Array.Sort(array);
		char[] array2 = b.ToCharArray();
		Array.Sort(array2);
		return array.AsSpan().SequenceEqual(array2);
	}

	private static HashSet<string> Generate(string word)
	{
		HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
		char[] array = word.ToCharArray();
		for (int i = 0; i < array.Length; i++)
		{
			char c = array[i];
			char[] alphabet = Alphabet;
			foreach (char c2 in alphabet)
			{
				if (c2 != c)
				{
					array[i] = c2;
					hashSet.Add(new string(array));
				}
			}
			array[i] = c;
		}
		if (word.Length > 3)
		{
			for (int k = 0; k < word.Length; k++)
			{
				hashSet.Add(word.Remove(k, 1));
			}
		}
		if (word.Length <= 8)
		{
			for (int l = 0; l <= word.Length; l++)
			{
				char[] alphabet = Alphabet;
				foreach (char c3 in alphabet)
				{
					hashSet.Add(word.Insert(l, c3.ToString()));
				}
			}
		}
		if (word.Length <= 7)
		{
			for (int m = 0; m < word.Length; m++)
			{
				for (int n = m + 1; n < word.Length; n++)
				{
					if (word[m] != word[n])
					{
						char[] array2 = word.ToCharArray();
						ref char reference = ref array2[m];
						ref char reference2 = ref array2[n];
						char c4 = array2[n];
						char c5 = array2[m];
						reference = c4;
						reference2 = c5;
						hashSet.Add(new string(array2));
					}
				}
			}
		}
		else
		{
			for (int num = 0; num + 1 < word.Length; num++)
			{
				if (word[num] != word[num + 1])
				{
					char[] array3 = word.ToCharArray();
					ref char reference = ref array3[num];
					ref char reference3 = ref array3[num + 1];
					char c5 = array3[num + 1];
					char c4 = array3[num];
					reference = c5;
					reference3 = c4;
					hashSet.Add(new string(array3));
				}
			}
		}
		foreach (string item in HebrewSpelling.Expand(word))
		{
			hashSet.Add(item);
		}
		hashSet.Remove(word);
		return hashSet;
	}

	private static int Distance(string a, string b)
	{
		int length = a.Length;
		int length2 = b.Length;
		if (Math.Abs(length - length2) > 3)
		{
			return 4;
		}
		int[,] array = new int[length + 1, length2 + 1];
		for (int i = 0; i <= length; i++)
		{
			array[i, 0] = i;
		}
		for (int j = 0; j <= length2; j++)
		{
			array[0, j] = j;
		}
		for (int k = 1; k <= length; k++)
		{
			for (int l = 1; l <= length2; l++)
			{
				int num = ((a[k - 1] != b[l - 1]) ? 1 : 0);
				array[k, l] = Math.Min(Math.Min(array[k - 1, l] + 1, array[k, l - 1] + 1), array[k - 1, l - 1] + num);
				if (k > 1 && l > 1 && a[k - 1] == b[l - 2] && a[k - 2] == b[l - 1])
				{
					array[k, l] = Math.Min(array[k, l], array[k - 2, l - 2] + 1);
				}
			}
		}
		return Math.Min(array[length, length2], 4);
	}

	private static List<Token> Tokenize(string s)
	{
		List<Token> list = new List<Token>();
		int i = 0;
		while (i < s.Length)
		{
			bool flag = IsWordChar(s[i]);
			int num = i;
			for (; i < s.Length && IsWordChar(s[i]) == flag; i++)
			{
			}
			int num2 = num;
			list.Add(new Token(s.Substring(num2, i - num2), flag));
		}
		return list;
	}

	private static bool IsWordChar(char c)
	{
		bool flag = char.IsLetter(c);
		if (!flag)
		{
			bool flag2;
			switch (c)
			{
			case '"':
			case '\'':
			case '׳':
			case '״':
				flag2 = true;
				break;
			default:
				flag2 = false;
				break;
			}
			flag = flag2;
		}
		return flag;
	}

	private static bool IsHebrew(string s)
	{
		return s.All((char c) => c >= 'א' && c <= 'ת');
	}

	private static List<string> HebrewWords(string s)
	{
		return (from t in Tokenize(s)
			where t.IsWord && t.Text.Length >= 2 && IsHebrew(t.Text)
			select t.Text).Distinct<string>(StringComparer.Ordinal).ToList();
	}
}
