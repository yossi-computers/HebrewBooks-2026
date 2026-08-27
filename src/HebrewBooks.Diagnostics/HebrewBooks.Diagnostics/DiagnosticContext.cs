using System;
using HebrewBooks.Core.Abstractions;
using HebrewBooks.Infrastructure.Paths;
using HebrewBooks.Infrastructure.Settings;

namespace HebrewBooks.Diagnostics;

public sealed class DiagnosticContext
{
	public JsonSettingsStore Settings { get; }

	public BookshelfOptions Options { get; }

	public string? DataRoot { get; }

	public string? DataRootError { get; }

	public IPathResolver? Paths { get; }

	private DiagnosticContext(JsonSettingsStore settings, BookshelfOptions options, string? dataRoot, string? dataRootError, IPathResolver? paths)
	{
		Settings = settings;
		Options = options;
		DataRoot = dataRoot;
		DataRootError = dataRootError;
		Paths = paths;
	}

	public static DiagnosticContext Resolve(JsonSettingsStore settings, string[]? args = null)
	{
		BookshelfOptions options;
		try
		{
			options = settings.Load();
		}
		catch
		{
			options = new BookshelfOptions();
		}
		string dataRoot = null;
		string dataRootError = null;
		IPathResolver paths = null;
		try
		{
			dataRoot = new DataRootResolver(settings).Resolve(args, () => (string?)null);
			paths = new PathResolver(dataRoot, options);
		}
		catch (Exception ex)
		{
			dataRootError = ex.Message;
		}
		return new DiagnosticContext(settings, options, dataRoot, dataRootError, paths);
	}

	public static DiagnosticContext FromResolved(JsonSettingsStore settings, IPathResolver paths)
	{
		BookshelfOptions options = settings.Load();
		return new DiagnosticContext(settings, options, paths.DataDriveRoot, null, paths);
	}
}
