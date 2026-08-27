using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace HebrewBooks.Services.Search;

public sealed class RasheyTevotMap
{
	private Dictionary<string, IReadOnlyList<string>> _entries;

	public static readonly RasheyTevotMap Empty = new RasheyTevotMap(new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal));

	public int Count => _entries.Count;

	public IReadOnlyDictionary<string, IReadOnlyList<string>> Entries => _entries;

	public RasheyTevotMap(IDictionary<string, IReadOnlyList<string>> entries)
	{
		_entries = new Dictionary<string, IReadOnlyList<string>>(entries, StringComparer.Ordinal);
	}

	public bool TryGet(string acronym, out IReadOnlyList<string> expansions)
	{
		if (_entries.TryGetValue(acronym, out IReadOnlyList<string> value))
		{
			expansions = value;
			return true;
		}
		expansions = Array.Empty<string>();
		return false;
	}

	public void ReplaceWith(IDictionary<string, IReadOnlyList<string>> entries)
	{
		if (this == Empty)
		{
			throw new InvalidOperationException("Cannot mutate the shared Empty map; unwrap it first.");
		}
		_entries = new Dictionary<string, IReadOnlyList<string>>(entries, StringComparer.Ordinal);
	}

	public void Save(string path)
	{
		Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.Append("[ראשי תיבות]\r\n");
		foreach (KeyValuePair<string, IReadOnlyList<string>> item in _entries.OrderBy<KeyValuePair<string, IReadOnlyList<string>>, string>((KeyValuePair<string, IReadOnlyList<string>> k) => k.Key, StringComparer.Ordinal))
		{
			stringBuilder.Append(item.Key);
			stringBuilder.Append('=');
			stringBuilder.Append(string.Join('|', item.Value));
			stringBuilder.Append("|\r\n");
		}
		byte[] bytes = Encoding.GetEncoding(1255).GetBytes(stringBuilder.ToString());
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

	public static RasheyTevotMap LoadFromFile(string path)
	{
		if (!File.Exists(path))
		{
			return Empty;
		}
		Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
		byte[] array = File.ReadAllBytes(path);
		return Parse(((array.Length >= 3 && array[0] == 239 && array[1] == 187 && array[2] == 191) ? Encoding.UTF8 : Encoding.GetEncoding(1255)).GetString(array));
	}

	public static RasheyTevotMap Parse(string text)
	{
		Dictionary<string, IReadOnlyList<string>> dictionary = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
		if (string.IsNullOrEmpty(text))
		{
			return new RasheyTevotMap(dictionary);
		}
		string[] array = text.Split(new string[2] { "\r\n", "\n" }, StringSplitOptions.None);
		for (int i = 0; i < array.Length; i++)
		{
			string text2 = array[i].Trim('\ufeff', ' ', '\t');
			if (text2.Length == 0 || text2[0] == '[' || text2[0] == ';')
			{
				continue;
			}
			int num = text2.IndexOf('=');
			if (num <= 0)
			{
				continue;
			}
			string text3 = text2.Substring(0, num).Trim();
			if (text3.Length != 0)
			{
				string text4 = text2;
				int num2 = num + 1;
				string[] array2 = (from s in text4.Substring(num2, text4.Length - num2).Split('|', StringSplitOptions.RemoveEmptyEntries)
					select s.Trim() into s
					where s.Length > 0
					select s).ToArray();
				if (array2.Length != 0)
				{
					dictionary[text3] = array2;
				}
			}
		}
		return new RasheyTevotMap(dictionary);
	}
}
