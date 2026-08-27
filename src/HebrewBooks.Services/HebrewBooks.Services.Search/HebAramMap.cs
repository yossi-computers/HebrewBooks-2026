using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace HebrewBooks.Services.Search;

public sealed class HebAramMap
{
	public const string RemoteUrl = "https://raw.githubusercontent.com/HebrewBooks-2026/Hebrewbooks-Releases/main/HebAram.DB";

	private Dictionary<string, IReadOnlyList<string>> _byForm;

	private List<IReadOnlyList<string>> _groups;

	private readonly Func<string?>? _lazyTextProvider;

	private volatile bool _loaded;

	private readonly object _loadLock = new object();

	public static readonly HebAramMap Empty = new HebAramMap(Array.Empty<IReadOnlyList<string>>());

	public int Count
	{
		get
		{
			EnsureLoaded();
			return _groups.Count;
		}
	}

	public IReadOnlyList<IReadOnlyList<string>> Groups
	{
		get
		{
			EnsureLoaded();
			return _groups;
		}
	}

	public HebAramMap(IEnumerable<IReadOnlyList<string>> groups)
	{
		_groups = new List<IReadOnlyList<string>>();
		_byForm = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
		foreach (IReadOnlyList<string> group in groups)
		{
			AddGroup(group);
		}
		_loaded = true;
	}

	public HebAramMap(Func<string?> textProvider)
	{
		_lazyTextProvider = textProvider;
		_groups = new List<IReadOnlyList<string>>();
		_byForm = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
	}

	private void EnsureLoaded()
	{
		if (_loaded)
		{
			return;
		}
		lock (_loadLock)
		{
			if (_loaded)
			{
				return;
			}
			try
			{
				string text = _lazyTextProvider?.Invoke();
				if (!string.IsNullOrWhiteSpace(text))
				{
					foreach (IReadOnlyList<string> item in ParseGroups(text))
					{
						AddGroup(item);
					}
				}
			}
			catch
			{
			}
			_loaded = true;
		}
	}

	private void AddGroup(IReadOnlyList<string> group)
	{
		string[] array = (from s in @group
			select s.Trim() into s
			where s.Length > 0
			select s).Distinct<string>(StringComparer.Ordinal).ToArray();
		if (array.Length < 2)
		{
			return;
		}
		_groups.Add(array);
		string[] array2 = array;
		foreach (string text in array2)
		{
			if (!text.Contains(' '))
			{
				_byForm[text] = array;
			}
		}
	}

	public IReadOnlyList<string>? ExpandForQuery(string word)
	{
		if (string.IsNullOrEmpty(word))
		{
			return null;
		}
		EnsureLoaded();
		if (!_byForm.TryGetValue(word, out IReadOnlyList<string> value))
		{
			return null;
		}
		List<string> list = new List<string>(value.Count) { word };
		foreach (string item in value)
		{
			if (!(item == word) && !item.Contains(' '))
			{
				list.Add(item);
			}
		}
		if (list.Count <= 1)
		{
			return null;
		}
		return list;
	}

	public void ReplaceWith(IEnumerable<IReadOnlyList<string>> groups)
	{
		if (this == Empty)
		{
			throw new InvalidOperationException("Cannot mutate the shared Empty map; unwrap it first.");
		}
		List<IReadOnlyList<string>> groups2 = new List<IReadOnlyList<string>>();
		Dictionary<string, IReadOnlyList<string>> byForm = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
		_groups = groups2;
		_byForm = byForm;
		foreach (IReadOnlyList<string> group in groups)
		{
			AddGroup(group);
		}
		_loaded = true;
	}

	public static string? ReadFileText(string path)
	{
		if (!File.Exists(path))
		{
			return null;
		}
		Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
		byte[] array = File.ReadAllBytes(path);
		return ((array.Length >= 3 && array[0] == 239 && array[1] == 187 && array[2] == 191) ? Encoding.UTF8 : Encoding.GetEncoding(1255)).GetString(array);
	}

	public static HebAramMap Parse(string text)
	{
		return new HebAramMap(ParseGroups(text));
	}

	private static List<IReadOnlyList<string>> ParseGroups(string text)
	{
		List<IReadOnlyList<string>> list = new List<IReadOnlyList<string>>();
		if (string.IsNullOrEmpty(text))
		{
			return list;
		}
		string[] array = text.Split(new string[2] { "\r\n", "\n" }, StringSplitOptions.None);
		for (int i = 0; i < array.Length; i++)
		{
			string text2 = array[i].Trim('\ufeff', ' ', '\t');
			if (text2.Length != 0 && text2[0] != ';' && text2[0] != '[')
			{
				string[] array2 = (from s in text2.Split('|', StringSplitOptions.RemoveEmptyEntries)
					select s.Trim() into s
					where s.Length > 0
					select s).ToArray();
				if (array2.Length >= 2)
				{
					list.Add(array2);
				}
			}
		}
		return list;
	}

	public void Save(string path)
	{
		EnsureLoaded();
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.Append("; HebAram.DB — מילון שקילות ארמית-עברית\r\n");
		stringBuilder.Append("; כל שורה = קבוצת שקילות; הפרידו בין הצורות בתו |\r\n");
		stringBuilder.Append("; שורות שמתחילות ב-; הן הערות. עריכה נכנסת לתוקף בחיפוש הבא.\r\n");
		foreach (IReadOnlyList<string> group in _groups)
		{
			stringBuilder.Append(string.Join('|', group)).Append("\r\n");
		}
		UTF8Encoding uTF8Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
		byte[] bytes = uTF8Encoding.GetPreamble().Concat(uTF8Encoding.GetBytes(stringBuilder.ToString())).ToArray();
		string directoryName = Path.GetDirectoryName(path);
		if (!string.IsNullOrEmpty(directoryName))
		{
			Directory.CreateDirectory(directoryName);
		}
		string text = path + ".tmp";
		File.WriteAllBytes(text, bytes);
		if (File.Exists(path))
		{
			File.Replace(text, path, null);
		}
		else
		{
			File.Move(text, path);
		}
	}
}
