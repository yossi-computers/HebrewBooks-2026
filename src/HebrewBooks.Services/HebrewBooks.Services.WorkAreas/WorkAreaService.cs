using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HebrewBooks.Core.Abstractions;
using HebrewBooks.Core.Models;

namespace HebrewBooks.Services.WorkAreas;

public sealed class WorkAreaService(IPathResolver paths) : IWorkAreaService
{
	private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
	{
		WriteIndented = true,
		PropertyNamingPolicy = null
	};

	public Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default(CancellationToken))
	{
		string workAreaDir = paths.WorkAreaDir;
		if (!Directory.Exists(workAreaDir))
		{
			return Task.FromResult((IReadOnlyList<string>)Array.Empty<string>());
		}
		return Task.FromResult((IReadOnlyList<string>)(from string n in from n in Directory.EnumerateFiles(workAreaDir, "*.json").Select(Path.GetFileNameWithoutExtension)
				where !string.IsNullOrEmpty(n)
				select n
			orderby n
			select n).ToList());
	}

	public async Task<WorkArea?> LoadAsync(string name, CancellationToken ct = default(CancellationToken))
	{
		string path = ResolvePath(name);
		if (!File.Exists(path))
		{
			return null;
		}
		WorkArea result;
		await using (FileStream stream = File.OpenRead(path))
		{
			result = await JsonSerializer.DeserializeAsync<WorkArea>((Stream)stream, JsonOpts, ct);
		}
		return result;
	}

	public async Task SaveAsync(WorkArea area, CancellationToken ct = default(CancellationToken))
	{
		Directory.CreateDirectory(paths.WorkAreaDir);
		string path = ResolvePath(area.Name);
		string temp = path + ".tmp";
		await using (FileStream stream = File.Create(temp))
		{
			await JsonSerializer.SerializeAsync((Stream)stream, area, JsonOpts, ct);
		}
		File.Move(temp, path, overwrite: true);
	}

	public Task DeleteAsync(string name, CancellationToken ct = default(CancellationToken))
	{
		string path = ResolvePath(name);
		if (File.Exists(path))
		{
			File.Delete(path);
		}
		return Task.CompletedTask;
	}

	private string ResolvePath(string name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			throw new ArgumentException("Work area name required.", "name");
		}
		string text = string.Join('_', name.Split(Path.GetInvalidFileNameChars()));
		return Path.Combine(paths.WorkAreaDir, text + ".json");
	}
}
