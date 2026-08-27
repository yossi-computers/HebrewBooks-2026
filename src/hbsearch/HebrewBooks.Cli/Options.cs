using System;
using System.Collections.Generic;
using System.IO;
using HebrewBooks.Services.Search;

namespace HebrewBooks.Cli;

internal sealed class Options
{
	public string? Query { get; private set; }

	public bool ShowHelp { get; private set; }

	public bool Serve { get; private set; }

	public int Port { get; private set; } = 8080;

	public string? InstallerPath { get; private set; }

	public bool EnableInstall { get; private set; }

	public bool InstallerUpdate { get; private set; } = true;

	public string InstallerRepo { get; private set; } = "yossi-computers/HebrewBooks-2026";

	public bool InstallEnabled
	{
		get
		{
			if (!EnableInstall)
			{
				return !string.IsNullOrWhiteSpace(InstallerPath);
			}
			return true;
		}
	}

	public bool UpdateFeed { get; private set; } = true;

	public string UpdateChannel { get; private set; } = "offline";

	public bool UpdateFeedEnabled
	{
		get
		{
			if (InstallEnabled)
			{
				return UpdateFeed;
			}
			return false;
		}
	}

	public string? ClientBase { get; private set; }

	public string? AdvertiseHost { get; private set; }

	public string? ShareUser { get; private set; }

	public string? SharePass { get; private set; }

	public int Proximity { get; private set; } = 30;

	public int Fuzziness { get; private set; }

	public bool Hybur { get; private set; }

	public bool Roots { get; private set; }

	public bool Gematria { get; private set; }

	public bool Spelling { get; private set; }

	public bool NumberGender { get; private set; }

	public bool Aramaic { get; private set; }

	public bool RasheyTevot { get; private set; }

	public bool FirstWord { get; private set; }

	public bool LastWord { get; private set; }

	public bool RequireWordOrder { get; private set; }

	public bool RashiOcr { get; private set; }

	public bool WeakLetters { get; private set; }

	public SortMode Sort { get; private set; } = SortMode.HitCount;

	public HashSet<string> Corpora { get; } = new HashSet<string>(StringComparer.Ordinal);

	public int MaxFiles { get; private set; } = 10000;

	public int? Limit { get; private set; }

	public bool ExcludePersonal { get; private set; }

	public OutputFormat Format { get; private set; }

	public bool Pretty { get; private set; }

	public static Options Parse(string[] args)
	{
		Options options = new Options();
		for (int i = 0; i < args.Length; i++)
		{
			string text = args[i];
			if (i == 0 && string.Equals(text, "search", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}
			if ((text == "-h" || text == "--help") ? true : false)
			{
				options.ShowHelp = true;
				return options;
			}
			if (!text.StartsWith("--", StringComparison.Ordinal))
			{
				if (options.Query == null)
				{
					options.Query = text;
					continue;
				}
				throw new ArgUsageException("unexpected argument '" + text + "' (wrap multi-word queries in quotes).");
			}
			switch (text)
			{
			case "--hybur":
				options.Hybur = true;
				break;
			case "--roots":
				options.Roots = true;
				break;
			case "--gematria":
				options.Gematria = true;
				break;
			case "--spelling":
				options.Spelling = true;
				break;
			case "--number-gender":
				options.NumberGender = true;
				break;
			case "--aramaic":
				options.Aramaic = true;
				break;
			case "--rashetevot":
				options.RasheyTevot = true;
				break;
			case "--first-word":
				options.FirstWord = true;
				break;
			case "--last-word":
				options.LastWord = true;
				break;
			case "--require-word-order":
				options.RequireWordOrder = true;
				break;
			case "--rashi-ocr":
				options.RashiOcr = true;
				break;
			case "--weak":
				options.WeakLetters = true;
				break;
			case "--exclude-personal":
				options.ExcludePersonal = true;
				break;
			case "--pretty":
				options.Pretty = true;
				break;
			case "--serve":
				options.Serve = true;
				break;
			case "--port":
				options.Port = ParseRangedInt(RequireValue(args, ref i, text), text, 1, 65535);
				break;
			case "--installer":
				options.InstallerPath = RequireValue(args, ref i, text);
				break;
			case "--enable-install":
				options.EnableInstall = true;
				break;
			case "--no-installer-update":
				options.InstallerUpdate = false;
				break;
			case "--installer-repo":
				options.InstallerRepo = RequireValue(args, ref i, text);
				break;
			case "--no-update-feed":
				options.UpdateFeed = false;
				break;
			case "--update-channel":
				options.UpdateChannel = RequireValue(args, ref i, text);
				break;
			case "--client-base":
				options.ClientBase = RequireValue(args, ref i, text);
				break;
			case "--advertise-host":
				options.AdvertiseHost = RequireValue(args, ref i, text);
				break;
			case "--share-user":
				options.ShareUser = RequireValue(args, ref i, text);
				break;
			case "--share-pass":
				options.SharePass = RequireValue(args, ref i, text);
				break;
			case "--data-root":
				RequireValue(args, ref i, text);
				break;
			case "--proximity":
				options.Proximity = ParsePositiveInt(RequireValue(args, ref i, text), text);
				break;
			case "--fuzziness":
				options.Fuzziness = ParseRangedInt(RequireValue(args, ref i, text), text, 0, 10);
				break;
			case "--max":
				options.MaxFiles = ParsePositiveInt(RequireValue(args, ref i, text), text);
				break;
			case "--limit":
				options.Limit = ParsePositiveInt(RequireValue(args, ref i, text), text);
				break;
			case "--sort":
				options.Sort = ParseSort(RequireValue(args, ref i, text));
				break;
			case "--corpus":
				ParseCorpora(options, RequireValue(args, ref i, text));
				break;
			case "--format":
				options.Format = ParseFormat(RequireValue(args, ref i, text));
				break;
			default:
				throw new ArgUsageException("unknown option '" + text + "'.");
			case "--compact-char-class":
				break;
			}
		}
		return options;
	}

	private static string RequireValue(string[] args, ref int i, string flag)
	{
		if (i + 1 >= args.Length)
		{
			throw new ArgUsageException("option '" + flag + "' requires a value.");
		}
		return args[++i];
	}

	private static int ParsePositiveInt(string v, string flag)
	{
		if (!int.TryParse(v, out var result) || result <= 0)
		{
			throw new ArgUsageException($"option '{flag}' expects a positive integer, got '{v}'.");
		}
		return result;
	}

	private static int ParseRangedInt(string v, string flag, int min, int max)
	{
		if (!int.TryParse(v, out var result) || result < min || result > max)
		{
			throw new ArgUsageException($"option '{flag}' expects an integer {min}..{max}, got '{v}'.");
		}
		return result;
	}

	private static SortMode ParseSort(string v)
	{
		switch (v.ToLowerInvariant())
		{
		case "hitcount":
		case "hits":
			return SortMode.HitCount;
		case "bookname":
		case "name":
		case "book":
			return SortMode.BookName;
		case "authorname":
		case "author":
			return SortMode.AuthorName;
		case "printplace":
		case "place":
			return SortMode.PrintPlace;
		case "year":
		case "printyear":
			return SortMode.PrintYear;
		case "id":
			return SortMode.Id;
		default:
			throw new ArgUsageException("unknown sort '" + v + "' (use: hitcount|bookname|author|place|year|id).");
		}
	}

	private static void ParseCorpora(Options o, string v)
	{
		string[] array = v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		foreach (string text in array)
		{
			switch (text.ToLowerInvariant())
			{
			case "pdf":
			case "hebrewbooks":
				o.Corpora.Add("PDF");
				break;
			case "otzraya":
			case "text":
				o.Corpora.Add("Text");
				break;
			case "personal":
				o.Corpora.Add("Personal");
				break;
			default:
				throw new ArgUsageException("unknown corpus '" + text + "' (use: pdf|otzraya|personal).");
			}
		}
	}

	private static OutputFormat ParseFormat(string v)
	{
		switch (v.ToLowerInvariant())
		{
		case "json":
			return OutputFormat.Json;
		case "jsonl":
		case "ndjson":
			return OutputFormat.Jsonl;
		default:
			throw new ArgUsageException("unknown format '" + v + "' (use: json|jsonl).");
		}
	}

	public static void PrintUsage(TextWriter w)
	{
		w.WriteLine("hbsearch — headless HebrewBooks full-text search (JSON to stdout)");
		w.WriteLine();
		w.WriteLine("USAGE:");
		w.WriteLine("  hbsearch \"<query>\" [options]");
		w.WriteLine();
		w.WriteLine("QUERY EXPANSIONS (match the app's search toggles):");
		w.WriteLine("  --hybur            expand Hebrew prefix letters (ה/ו/ב/כ/ל/מ/ש)");
		w.WriteLine("  --roots            expand root/inflection forms");
		w.WriteLine("  --gematria         expand number <-> gematria");
		w.WriteLine("  --number-gender    expand cardinal number gender (שלושה<->שלוש)");
		w.WriteLine("  --spelling         expand ktiv male/chaser (ו/י) variants");
		w.WriteLine("  --aramaic          expand Hebrew<->Aramaic equivalents");
		w.WriteLine("  --rashetevot       expand Rashey-Tevot (acronyms)");
		w.WriteLine("  --first-word       anchor first word (xfirstword)");
		w.WriteLine("  --last-word        anchor last word (xlastword)");
		w.WriteLine("  --require-word-order  keep words in the typed order within the proximity window");
		w.WriteLine("  --rashi-ocr        expand Rashi-script OCR letter confusions (א↔ח, ת↔מ, …)");
		w.WriteLine("  --weak             expand Yiddish weak-letter spellings (א/ה/ע flex; אמריקה↔אמעריקא)");
		w.WriteLine();
		w.WriteLine("MATCHING / SCOPE:");
		w.WriteLine("  --proximity <n>    default word proximity w/N (default 30)");
		w.WriteLine("  --fuzziness <0-10> dtSearch fuzziness (default 0)");
		w.WriteLine("  --corpus <list>    comma list of pdf,otzraya,personal (default: all)");
		w.WriteLine("  --max <n>          max documents to retrieve (default 10000)");
		w.WriteLine("  --exclude-personal drop SourceType=Personal rows from output");
		w.WriteLine();
		w.WriteLine("OUTPUT:");
		w.WriteLine("  --sort <mode>      hitcount|bookname|author|place|year|id (default hitcount)");
		w.WriteLine("  --limit <n>        cap number of result rows emitted");
		w.WriteLine("  --format <fmt>     json (envelope) | jsonl (one row per line)  (default json)");
		w.WriteLine("  --pretty           indent json output");
		w.WriteLine();
		w.WriteLine("SERVICE (run on the server so stations search over HTTP, index stays warm):");
		w.WriteLine("  --serve            run as an HTTP search service instead of one-shot");
		w.WriteLine("  --port <n>         port for --serve (default 8080)");
		w.WriteLine("                     GET /search?q=<query>&proximity=30&hybur=true&... → JSONL stream");
		w.WriteLine("                     GET /health → {\"ok\":true}");
		w.WriteLine();
		w.WriteLine("CLIENT INSTALL (let stations install + auto-configure from the server over the LAN):");
		w.WriteLine("  --installer <path> OFFLINE-fallback client Setup.exe hosted at GET /Setup.exe;");
		w.WriteLine("                     enables GET /install. Served when no fresher copy was downloaded.");
		w.WriteLine("  --enable-install   enable install endpoints with no local file (download-on-demand)");
		w.WriteLine("  --no-installer-update  don't fetch from GitHub; serve only the local/cached file");
		w.WriteLine("  --installer-repo <owner/repo>  releases repo for the offline Setup");
		w.WriteLine("                     (default yossi-computers/HebrewBooks-2026)");
		w.WriteLine("  --client-base <unc>  shared base folder written to the client (NetworkBasePath),");
		w.WriteLine("                     e.g. \\\\SERVER\\f\\HebrewBooks");
		w.WriteLine("  --share-user <name>  read-only account stations auto-authenticate to the share with");
		w.WriteLine("  --share-pass <pass>  its password; with --share-user the install script silently");
		w.WriteLine("                     connects the station (cmdkey + net use) — LAN cleartext, see docs");
		w.WriteLine("  --advertise-host <h> host baked into the install script (else from the request)");
		w.WriteLine("                     When online, the LATEST offline Setup is downloaded + cached;");
		w.WriteLine("                     offline, the local/cached copy is served. Stations run:");
		w.WriteLine("                       irm http://SERVER:PORT/install | iex");
		w.WriteLine();
		w.WriteLine("CLIENT UPDATES (already-installed stations auto-update over the LAN, no internet):");
		w.WriteLine("  (on by default whenever install is enabled) GET /vpk/* serves a Velopack update");
		w.WriteLine("  feed, mirrored on demand from the releases repo and cached on disk. Stations on a");
		w.WriteLine("  network install point Velopack at this server instead of GitHub.");
		w.WriteLine("  --no-update-feed   disable the /vpk update feed");
		w.WriteLine("  --update-channel <name>  channel to pre-warm at startup (default offline)");
		w.WriteLine();
		w.WriteLine("DATA:");
		w.WriteLine("  --data-root <path> override the data drive (else HEBREWBOOKS_DATA / auto-detect)");
		w.WriteLine("  -h, --help         show this help");
		w.WriteLine();
		w.WriteLine("Diagnostics go to stderr; only result JSON goes to stdout.");
		w.WriteLine("Power users: a query already containing dtSearch operators (w/ AND OR NOT");
		w.WriteLine("xfilter) is passed through verbatim — expansions are skipped.");
	}
}
