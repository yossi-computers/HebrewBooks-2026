using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HebrewBooks.Search.Incremental;

public sealed class IndexManifest
{
	public const string FileName = "hb-manifest.json";

	private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
	{
		WriteIndented = true,
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
	};

	[JsonPropertyName("version")]
	public int Version { get; set; } = 1;

	[JsonPropertyName("corpusRootName")]
	public string CorpusRootName { get; set; } = "";

	[JsonPropertyName("entries")]
	public Dictionary<string, ManifestEntry> Entries { get; set; } = new Dictionary<string, ManifestEntry>(StringComparer.OrdinalIgnoreCase);

	public static string PathFor(string indexPath)
	{
		return Path.Combine(indexPath, "hb-manifest.json");
	}

	public static IndexManifest? Load(string indexPath)
	{
		try
		{
			string path = PathFor(indexPath);
			if (!File.Exists(path))
			{
				return null;
			}
			IndexManifest indexManifest = JsonSerializer.Deserialize<IndexManifest>(File.ReadAllText(path), JsonOpts);
			if (indexManifest == null)
			{
				return null;
			}
			indexManifest.Entries = new Dictionary<string, ManifestEntry>(indexManifest.Entries, StringComparer.OrdinalIgnoreCase);
			return indexManifest;
		}
		catch
		{
			return null;
		}
	}

	public void Save(string indexPath)
	{
		string text = PathFor(indexPath);
		Directory.CreateDirectory(Path.GetDirectoryName(text));
		string text2 = text + ".tmp";
		string contents = JsonSerializer.Serialize(this, JsonOpts);
		File.WriteAllText(text2, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		File.Move(text2, text, overwrite: true);
	}
}
