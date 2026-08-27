using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using HebrewBooks.Core.Abstractions;

namespace HebrewBooks.Infrastructure.Settings;

public sealed class JsonSettingsStore
{
	public static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		WriteIndented = true,
		PropertyNamingPolicy = null,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};

	private readonly IProtectMode? _protect;

	public string SettingsPath { get; }

	public bool Exists => File.Exists(SettingsPath);

	public JsonSettingsStore(string? settingsPath = null, IProtectMode? protectMode = null)
	{
		SettingsPath = settingsPath ?? DefaultPath();
		_protect = protectMode;
	}

	public static string DefaultPath()
	{
		return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HebrewBooks", "settings.json");
	}

	public BookshelfOptions Load()
	{
		if (!File.Exists(SettingsPath))
		{
			return new BookshelfOptions();
		}
		return JsonSerializer.Deserialize<BookshelfOptions>(File.ReadAllText(SettingsPath), JsonOptions) ?? new BookshelfOptions();
	}

	public void Save(BookshelfOptions options)
	{
		IProtectMode? protect = _protect;
		if (protect != null && protect.IsActive)
		{
			SaveDeploymentFieldsOnly(options);
			return;
		}
		Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
		string text = SettingsPath + ".tmp";
		string contents = JsonSerializer.Serialize(options, JsonOptions);
		File.WriteAllText(text, contents);
		File.Move(text, SettingsPath, overwrite: true);
	}

	public void Update(Action<BookshelfOptions> mutate)
	{
		BookshelfOptions bookshelfOptions = Load();
		mutate(bookshelfOptions);
		Save(bookshelfOptions);
	}

	private void SaveDeploymentFieldsOnly(BookshelfOptions options)
	{
		BookshelfOptions bookshelfOptions = Load();
		if (bookshelfOptions.UseOnlineService != options.UseOnlineService || !(bookshelfOptions.Paths.OnlineServiceUrl == options.Paths.OnlineServiceUrl) || !(bookshelfOptions.Paths.OnlinePdfBaseUrl == options.Paths.OnlinePdfBaseUrl))
		{
			bookshelfOptions.UseOnlineService = options.UseOnlineService;
			bookshelfOptions.Paths.OnlineServiceUrl = options.Paths.OnlineServiceUrl;
			bookshelfOptions.Paths.OnlinePdfBaseUrl = options.Paths.OnlinePdfBaseUrl;
			Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
			string text = SettingsPath + ".tmp";
			File.WriteAllText(text, JsonSerializer.Serialize(bookshelfOptions, JsonOptions));
			File.Move(text, SettingsPath, overwrite: true);
		}
	}
}
