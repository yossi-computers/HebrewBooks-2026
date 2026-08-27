using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HebrewBooks.Core.Abstractions;
using HebrewBooks.Infrastructure.Settings;
using NuGet.Versioning;
using Serilog;
using Velopack;
using Velopack.Sources;

namespace HebrewBooks.UI.Services;

public sealed class AppUpdateService
{
	public const string DefaultRepoUrl = "https://github.com/yossi-computers/HebrewBooks-2026";

	private readonly JsonSettingsStore? _settings;

	private readonly string _repoUrl;

	private readonly UpdateManager? _identityManager;

	private readonly IProtectMode? _protect;

	private bool? _userOptedIntoBeta;

	public bool IsEnabled => _identityManager?.IsInstalled ?? false;

	public string? UpdateExePath
	{
		get
		{
			UpdateManager? identityManager = _identityManager;
			if (identityManager == null || !identityManager.IsInstalled)
			{
				return null;
			}
			try
			{
				string fullPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Update.exe"));
				return File.Exists(fullPath) ? fullPath : null;
			}
			catch
			{
				return null;
			}
		}
	}

	public bool CanUninstall => UpdateExePath != null;

	public string CurrentVersion => _identityManager?.CurrentVersion?.ToString() ?? "dev";

	public bool IsBetaInstall
	{
		get
		{
			if (!string.IsNullOrEmpty(CurrentVersion))
			{
				return CurrentVersion.Contains('-');
			}
			return false;
		}
	}

	public bool ShowBetaFeatures
	{
		get
		{
			if (!string.Equals(CurrentVersion, "dev", StringComparison.Ordinal) && !IsBetaInstall)
			{
				return UserOptedIntoBeta;
			}
			return true;
		}
	}

	private bool UserOptedIntoBeta
	{
		get
		{
			bool valueOrDefault = _userOptedIntoBeta == true;
			if (!_userOptedIntoBeta.HasValue)
			{
				valueOrDefault = _settings?.Load().Updates.IncludeBeta ?? false;
				_userOptedIntoBeta = valueOrDefault;
				return valueOrDefault;
			}
			return valueOrDefault;
		}
	}

	public AppUpdateService(JsonSettingsStore? settings = null, IProtectMode? protectMode = null, string? repoUrl = null)
	{
		_settings = settings;
		_protect = protectMode;
		_repoUrl = (string.IsNullOrWhiteSpace(repoUrl) ? "https://github.com/yossi-computers/HebrewBooks-2026" : repoUrl);
		try
		{
			_identityManager = new UpdateManager(new GithubSource(_repoUrl, null, prerelease: false));
		}
		catch (Exception ex)
		{
			Log.Information("AppUpdate disabled: {Message}", ex.Message);
		}
	}

	public bool BeginUninstall()
	{
		string updateExePath = UpdateExePath;
		if (updateExePath == null)
		{
			return false;
		}
		try
		{
			Process.Start(new ProcessStartInfo
			{
				FileName = updateExePath,
				Arguments = "--uninstall",
				UseShellExecute = false
			});
			return true;
		}
		catch (Exception exception)
		{
			Log.Warning(exception, "Uninstall launch failed");
			return false;
		}
	}

	public async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default(CancellationToken))
	{
		if (_identityManager == null)
		{
			return null;
		}
		bool includeBeta = _settings?.Load().Updates.IncludeBeta ?? false;
		try
		{
			return await new UpdateManager(ResolveSource(includeBeta)).CheckForUpdatesAsync().ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception exception)
		{
			Log.Warning(exception, "AppUpdate check failed (includeBeta={IncludeBeta})", includeBeta);
			return null;
		}
	}

	private IUpdateSource ResolveSource(bool includeBeta)
	{
		string text = LanFeedUrl();
		if (text != null)
		{
			Log.Information("AppUpdate: using LAN feed {Feed}", text);
			return new SimpleWebSource(text);
		}
		return new GithubSource(_repoUrl, null, includeBeta);
	}

	private string? LanFeedUrl()
	{
		string text = _settings?.Load().EffectiveSearchServiceUrl();
		if (!string.IsNullOrWhiteSpace(text))
		{
			return text.TrimEnd('/') + "/vpk/";
		}
		return null;
	}

	public async Task DownloadAsync(UpdateInfo info, IProgress<int>? progress = null, CancellationToken ct = default(CancellationToken))
	{
		if (_identityManager != null)
		{
			bool includeBeta = _settings?.Load().Updates.IncludeBeta ?? false;
			await new UpdateManager(ResolveSource(includeBeta)).DownloadUpdatesAsync(info, (progress == null) ? null : ((Action<int>)delegate(int i)
			{
				progress.Report(i);
			})).ConfigureAwait(continueOnCapturedContext: false);
		}
	}

	public void ApplyAndRestart(UpdateInfo info)
	{
		IProtectMode? protect = _protect;
		string[] restartArgs = ((protect == null || !protect.IsActive) ? null : new string[1] { "--kiosk" });
		_identityManager?.ApplyUpdatesAndRestart(info, restartArgs);
	}

	public async Task<string?> GetCumulativeReleaseNotesAsync(string? installedVersion, string? targetVersion, CancellationToken ct = default(CancellationToken))
	{
		if (string.IsNullOrWhiteSpace(installedVersion) || string.IsNullOrWhiteSpace(targetVersion))
		{
			return null;
		}
		if (!TryParseVersion(installedVersion, out SemanticVersion fromVer) || !TryParseVersion(targetVersion, out SemanticVersion toVer))
		{
			return null;
		}
		if (toVer <= fromVer)
		{
			return null;
		}
		string text = ExtractOwnerRepo(_repoUrl);
		if (text == null)
		{
			return null;
		}
		try
		{
			using HttpClient http = new HttpClient
			{
				Timeout = TimeSpan.FromSeconds(15.0)
			};
			http.DefaultRequestHeaders.UserAgent.ParseAdd("HebrewBooks-Updater/1.0");
			string[] array = new string[2]
			{
				$"https://github.com/{text}/releases/download/v{targetVersion}/CHANGELOG.md",
				"https://github.com/" + text + "/releases/latest/download/CHANGELOG.md"
			};
			string text2 = null;
			string[] array2 = array;
			foreach (string url in array2)
			{
				using HttpResponseMessage resp = await http.GetAsync(url, ct).ConfigureAwait(continueOnCapturedContext: false);
				if (resp.IsSuccessStatusCode)
				{
					text2 = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
					break;
				}
				Log.Information("GetCumulativeReleaseNotes: {Url} returned {Status}", url, (int)resp.StatusCode);
			}
			if (text2 == null)
			{
				return null;
			}
			List<(SemanticVersion, string)> list = ParseChangelogSections(text2);
			if (list.Count == 0)
			{
				return null;
			}
			List<(SemanticVersion, string)> list2 = list.Where<(SemanticVersion, string)>(((SemanticVersion Ver, string Body) s) => s.Ver > fromVer && s.Ver <= toVer).ToList();
			if (list2.Count == 0)
			{
				return null;
			}
			list2.Sort(((SemanticVersion Ver, string Body) a, (SemanticVersion Ver, string Body) b) => b.Ver.CompareTo(a.Ver));
			StringBuilder stringBuilder = new StringBuilder();
			foreach (var (value, value2) in list2)
			{
				if (stringBuilder.Length > 0)
				{
					stringBuilder.AppendLine().AppendLine();
				}
				stringBuilder.Append("● ").Append(value).AppendLine();
				stringBuilder.Append(value2);
			}
			return stringBuilder.ToString();
		}
		catch (Exception exception)
		{
			Log.Warning(exception, "GetCumulativeReleaseNotes failed");
			return null;
		}
	}

	private static List<(SemanticVersion Ver, string Body)> ParseChangelogSections(string text)
	{
		List<(SemanticVersion, string)> result = new List<(SemanticVersion, string)>();
		if (string.IsNullOrWhiteSpace(text))
		{
			return result;
		}
		string[] lines = text.Replace("\r\n", "\n").Split('\n');
		int? currentStart = null;
		SemanticVersion currentVer = null;
		for (int i = 0; i < lines.Length; i++)
		{
			string text2 = lines[i];
			if (text2.StartsWith("## "))
			{
				FlushSection(i);
				string text3 = text2;
				string text4 = text3.Substring(3, text3.Length - 3).Trim();
				int num = text4.IndexOf(' ');
				if (TryParseVersion((num >= 0) ? text4.Substring(0, num) : text4, out SemanticVersion ver))
				{
					currentVer = ver;
					currentStart = i + 1;
				}
			}
		}
		FlushSection(lines.Length);
		return result;
		void FlushSection(int endExclusive)
		{
			if ((object)currentVer != null && currentStart.HasValue)
			{
				string text5 = string.Join("\n", lines, currentStart.Value, endExclusive - currentStart.Value).Trim();
				if (!string.IsNullOrWhiteSpace(text5))
				{
					result.Add((currentVer, text5));
				}
				currentVer = null;
				currentStart = null;
			}
		}
	}

	private static bool TryParseVersion(string s, out SemanticVersion ver)
	{
		if (s == "dev")
		{
			ver = new SemanticVersion(0, 0, 0);
			return false;
		}
		return SemanticVersion.TryParse(s, out ver);
	}

	private static string? ExtractOwnerRepo(string repoUrl)
	{
		if (string.IsNullOrWhiteSpace(repoUrl))
		{
			return null;
		}
		try
		{
			Uri uri = new Uri(repoUrl);
			if (!uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
			{
				return null;
			}
			string[] array = uri.AbsolutePath.Trim('/').Split('/');
			if (array.Length < 2)
			{
				return null;
			}
			return array[0] + "/" + array[1];
		}
		catch
		{
			return null;
		}
	}
}
