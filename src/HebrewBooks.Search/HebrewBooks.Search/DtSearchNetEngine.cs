using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HebrewBooks.Core.Abstractions;
using HebrewBooks.Core.Indexing;
using HebrewBooks.Core.Models;
using HebrewBooks.Search.Incremental;
using dtSearch.Engine;

namespace HebrewBooks.Search;

public sealed class DtSearchNetEngine : ISearchEngine, IDisposable
{
	private sealed class IndexStatusHandler : IIndexStatusHandler
	{
		public int Checked;

		public int Binary;

		public int Encrypted;

		public string? FirstSeenFile;

		public int LastStep;

		public IProgress<double>? Progress;

		public IProgress<IndexProgressReport>? Detail;

		public string IndexName = "";

		public string IndexLocation = "";

		public string? DiskRoot;

		public double ProgressBase;

		public double ProgressSpan = 1.0;

		public CancellationToken Ct;

		private int _ticks;

		private long _lastLogMs = Environment.TickCount64;

		private long _lastDetailMs;

		private int _lastDetailStep = -1;

		private double _lastReportedPercent = -1.0;

		public AbortValue CheckForAbort()
		{
			if (!Ct.IsCancellationRequested)
			{
				return AbortValue.Continue;
			}
			return AbortValue.Cancel;
		}

		public void OnProgressUpdate(IndexProgressInfo info)
		{
			if (info == null)
			{
				return;
			}
			_ticks++;
			try
			{
				Checked = (int)info.FilesChecked;
				Binary = (int)info.BinaryCount;
				Encrypted = (int)info.EncryptedCount;
				LastStep = (int)info.Step;
				if (FirstSeenFile == null && info.File != null)
				{
					string text = info.File.ToString();
					if (!string.IsNullOrEmpty(text))
					{
						FirstSeenFile = text;
					}
				}
				double num = StepBase(LastStep);
				double num2 = ((LastStep <= 0 || LastStep >= 8) ? num : StepBase(LastStep + 1));
				if (_lastReportedPercent < num)
				{
					_lastReportedPercent = num;
				}
				else if (_lastReportedPercent < num2 - 0.5)
				{
					double num3 = num2 - _lastReportedPercent;
					_lastReportedPercent = Math.Min(num2 - 0.5, _lastReportedPercent + num3 * 0.015);
				}
				double num4 = ProgressBase + _lastReportedPercent / 100.0 * ProgressSpan;
				Progress?.Report(num4);
				int percentDone = (int)Math.Round(num4 * 100.0);
				long tickCount = Environment.TickCount64;
				if (Detail != null && (LastStep != _lastDetailStep || tickCount - _lastDetailMs >= 250))
				{
					_lastDetailMs = tickCount;
					_lastDetailStep = LastStep;
					long diskFreeBytes = 0L;
					try
					{
						if (!string.IsNullOrEmpty(DiskRoot))
						{
							diskFreeBytes = new DriveInfo(DiskRoot).AvailableFreeSpace;
						}
					}
					catch
					{
					}
					IndexFileInfo file = info.File;
					Detail.Report(new IndexProgressReport(LastStep, percentDone, info.FilesToIndex, info.FilesRead, info.BytesToIndexKB, info.BytesReadKB, info.DocsInIndex, info.WordsInIndex, (int)info.ElapsedSeconds, (int)info.EstRemainingSeconds, diskFreeBytes, IndexName, IndexLocation, file?.Name, file?.Location, file?.Type, file?.Size ?? 0, file?.WordCount ?? 0, file?.PercentDone ?? 0));
				}
				if (tickCount - _lastLogMs >= 5000)
				{
					_lastLogMs = tickCount;
					DumpDebug($"BuildIndex tick #{_ticks} step={LastStep} checked={Checked} binary={Binary} pct={_lastReportedPercent} firstSeen={FirstSeenFile ?? "<none>"}");
				}
			}
			catch
			{
			}
			static double StepBase(int step)
			{
				return step switch
				{
					1 => 2, 
					2 => 5, 
					3 => 10, 
					4 => 35, 
					5 => 65, 
					6 => 80, 
					7 => 92, 
					8 => 100, 
					_ => 0, 
				};
			}
		}
	}

	private sealed class StreamingStatusHandler : ISearchStatusHandler
	{
		private readonly List<SearchHit> _sink;

		private readonly IProgress<SearchHit>? _progress;

		private readonly CancellationToken _ct;

		private readonly Func<string, string?>? _filenameToCatalogId;

		private readonly int _maxHits;

		private readonly int _hitDivisor;

		private bool _capReached;

		public bool CapReached => _capReached;

		public StreamingStatusHandler(List<SearchHit> sink, IProgress<SearchHit>? progress, CancellationToken ct, Func<string, string?>? filenameToCatalogId, int maxHits, int hitDivisor)
		{
			_sink = sink;
			_progress = progress;
			_ct = ct;
			_filenameToCatalogId = filenameToCatalogId;
			_maxHits = ((maxHits > 0) ? maxHits : int.MaxValue);
			_hitDivisor = ((hitDivisor <= 1) ? 1 : hitDivisor);
		}

		public void OnFound(SearchResultsItem item)
		{
			if (item == null)
			{
				return;
			}
			if (_sink.Count >= _maxHits)
			{
				_capReached = true;
				return;
			}
			string text = item.Filename ?? "";
			string text2 = ((_filenameToCatalogId == null) ? Path.GetFileNameWithoutExtension(text) : _filenameToCatalogId(text));
			if (!string.IsNullOrEmpty(text2))
			{
				int hitCount = ((_hitDivisor > 1) ? Math.Max(1, item.HitCount / _hitDivisor) : item.HitCount);
				SearchHit searchHit = new SearchHit(text2, hitCount, item.ShortName ?? text, null);
				_sink.Add(searchHit);
				_progress?.Report(searchHit);
			}
		}

		public void OnSearchingIndex(string index)
		{
		}

		public void OnSearchingFile(string filename)
		{
		}

		public AbortValue CheckForAbort()
		{
			if (!_ct.IsCancellationRequested && !_capReached)
			{
				return AbortValue.Continue;
			}
			return AbortValue.CancelImmediately;
		}
	}

	private readonly EngineOptions _options;

	private Server? _server;

	private string? _indexPath;

	private readonly List<string> _extraIndexPaths = new List<string>();

	private SearchJob? _lastSearchJob;

	private SearchResults? _lastSearchResults;

	private string? _lastSearchQueryText;

	private readonly Dictionary<string, int> _fileIdToIndex = new Dictionary<string, int>(StringComparer.Ordinal);

	private readonly object _resultsLock = new object();

	private long _searchGeneration;

	private readonly Dictionary<string, WordListBuilder> _wordLists = new Dictionary<string, WordListBuilder>(StringComparer.OrdinalIgnoreCase);

	private readonly object _wordListLock = new object();

	private const string ReportHitOpen = "<<<HBHIT>>>";

	private const string ReportHitClose = "<<</HBHIT>>>";

	public Func<string, string?>? FileNameToCatalogId { get; set; }

	public bool IsServerLoaded => _server != null;

	public DtSearchNetEngine(EngineOptions options)
	{
		_options = options;
	}

	private Server EnsureServer()
	{
		if (_server != null)
		{
			return _server;
		}
		_server = new Server();
		Options options = new Options
		{
			AlphabetFile = _options.AlphabetFile,
			PhonicChar = _options.PhonicChar,
			IndexNumbers = _options.IndexNumbers
		};
		if (!string.IsNullOrEmpty(_options.PrivateDir))
		{
			options.PrivateDir = _options.PrivateDir;
		}
		options.Save();
		if (!string.IsNullOrEmpty(_options.DebugLogPath))
		{
			Server.SetDebugLogging(_options.DebugLogPath, DebugLogFlags.dtsLogCommit);
		}
		return _server;
	}

	public Task OpenIndexAsync(string indexPath, CancellationToken ct = default(CancellationToken))
	{
		EnsureServer();
		if (string.IsNullOrEmpty(_indexPath))
		{
			_indexPath = indexPath;
		}
		else if (!string.Equals(_indexPath, indexPath, StringComparison.OrdinalIgnoreCase) && !_extraIndexPaths.Any((string p) => string.Equals(p, indexPath, StringComparison.OrdinalIgnoreCase)))
		{
			_extraIndexPaths.Add(indexPath);
		}
		return Task.CompletedTask;
	}

	public void RemoveDocumentsFromIndex(string indexPath, IReadOnlyList<string> absolutePaths)
	{
		if (string.IsNullOrEmpty(indexPath) || absolutePaths == null || absolutePaths.Count == 0)
		{
			return;
		}
		EnsureServer();
		string text = null;
		try
		{
			text = Path.Combine(Path.GetTempPath(), $"hb-remove-{Guid.NewGuid():N}.txt");
			using (StreamWriter streamWriter = new StreamWriter(text, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
			{
				foreach (string absolutePath in absolutePaths)
				{
					if (!string.IsNullOrEmpty(absolutePath))
					{
						streamWriter.WriteLine(absolutePath);
					}
				}
			}
			using IndexJob indexJob = new IndexJob
			{
				IndexPath = indexPath,
				ActionAdd = false,
				ActionCreate = false,
				ActionRemoveListed = true,
				ToRemoveListName = text,
				TempFileDir = LocalIndexTempDir(),
				MaxMemToUseMB = 256
			};
			indexJob.Execute();
			DumpDebug($"RemoveDocs indexPath={indexPath} files={absolutePaths.Count}");
		}
		catch (Exception ex)
		{
			DumpDebug($"RemoveDocs FAILED indexPath={indexPath}: {ex.GetType().Name}: {ex.Message}");
		}
		finally
		{
			if (text != null)
			{
				try
				{
					if (File.Exists(text))
					{
						File.Delete(text);
					}
				}
				catch
				{
				}
			}
		}
	}

	private static string LocalIndexTempDir()
	{
		try
		{
			string text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HebrewBooks", "IndexTemp");
			Directory.CreateDirectory(text);
			return text;
		}
		catch
		{
			return Path.GetTempPath();
		}
	}

	public void AddDocumentsToIndex(string indexPath, IReadOnlyList<string> absolutePaths)
	{
		if (string.IsNullOrEmpty(indexPath))
		{
			throw new ArgumentException("indexPath is required", "indexPath");
		}
		if (absolutePaths == null || absolutePaths.Count == 0)
		{
			return;
		}
		EnsureServer();
		Directory.CreateDirectory(indexPath);
		bool flag = Directory.EnumerateFiles(indexPath, "*.ix").Any();
		string text = null;
		try
		{
			text = Path.Combine(Path.GetTempPath(), $"hb-add-{Guid.NewGuid():N}.txt");
			using (StreamWriter streamWriter = new StreamWriter(text, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
			{
				foreach (string absolutePath in absolutePaths)
				{
					if (!string.IsNullOrEmpty(absolutePath))
					{
						streamWriter.WriteLine(absolutePath);
					}
				}
			}
			using IndexJob indexJob = new IndexJob
			{
				IndexPath = indexPath,
				ActionCreate = !flag,
				ActionAdd = true,
				ToAddFileListName = text,
				TempFileDir = LocalIndexTempDir(),
				MaxMemToUseMB = 256
			};
			indexJob.Execute();
			JobErrorInfo errors = indexJob.Errors;
			if (errors != null && errors.Count > 0)
			{
				List<string> list = new List<string>();
				for (int i = 0; i < errors.Count; i++)
				{
					list.Add(errors.Message(i));
				}
				string text2 = string.Join(" | ", list);
				DumpDebug("AddDocs indexPath=" + indexPath + " ERRORS: " + text2);
				throw new InvalidOperationException($"dtSearch AddDocumentsToIndex reported {errors.Count} errors: {text2}");
			}
			DumpDebug($"AddDocs indexPath={indexPath} files={absolutePaths.Count} created={!flag}");
		}
		finally
		{
			if (text != null)
			{
				try
				{
					if (File.Exists(text))
					{
						File.Delete(text);
					}
				}
				catch
				{
				}
			}
		}
	}

	public Task BuildIndexAsync(IndexSpec spec, IProgress<double>? progress, IProgress<IndexProgressReport>? detail = null, CancellationToken ct = default(CancellationToken))
	{
		return Task.Run(delegate
		{
			EnsureServer();
			string pathRoot = Path.GetPathRoot(spec.IndexPath);
			bool flag = Directory.Exists(spec.IndexPath) && Directory.EnumerateFiles(spec.IndexPath, "*.ix").Any();
			string fileName = Path.GetFileName((spec.RelativeKeyRoot ?? spec.SourceFolders.FirstOrDefault() ?? "").TrimEnd('\\', '/'));
			IndexStatusHandler indexStatusHandler = new IndexStatusHandler
			{
				Progress = progress,
				Detail = detail,
				IndexName = Path.GetFileName(spec.IndexPath.TrimEnd('\\', '/')),
				IndexLocation = (Path.GetDirectoryName(spec.IndexPath) ?? spec.IndexPath),
				DiskRoot = Path.GetPathRoot(spec.IndexPath),
				Ct = ct,
				ProgressBase = 0.05,
				ProgressSpan = 0.95
			};
			IndexManifest indexManifest = (flag ? IndexManifest.Load(spec.IndexPath) : null);
			long num = ((long?)indexManifest?.Entries.Count) ?? (flag ? QuickIndexFileCount(spec.IndexPath) : 0);
			progress?.Report(0.02);
			List<ScannedFile> list = new List<ScannedFile>();
			HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			bool flag2 = true;
			long tickCount = Environment.TickCount64;
			foreach (string sourceFolder in spec.SourceFolders)
			{
				if (string.IsNullOrEmpty(sourceFolder) || !Directory.Exists(sourceFolder))
				{
					DumpDebug("BuildIndex probe folder='" + sourceFolder + "' MISSING");
				}
				else
				{
					try
					{
						EnumerationOptions enumerationOptions = new EnumerationOptions
						{
							RecurseSubdirectories = true,
							IgnoreInaccessible = false,
							AttributesToSkip = FileAttributes.None
						};
						foreach (FileInfo item in new DirectoryInfo(sourceFolder).EnumerateFiles("*", enumerationOptions))
						{
							ct.ThrowIfCancellationRequested();
							long length;
							long ticks;
							try
							{
								length = item.Length;
								ticks = item.LastWriteTimeUtc.Ticks;
							}
							catch
							{
								continue;
							}
							string fullName = item.FullName;
							string text = CorpusKey.Compute(fullName, spec.RelativeKeyRoot, pathRoot);
							if (!string.IsNullOrEmpty(text) && hashSet.Add(text))
							{
								list.Add(new ScannedFile(text, length, ticks, fullName));
								if (list.Count % 500 == 0 && Environment.TickCount64 - tickCount >= 300)
								{
									tickCount = Environment.TickCount64;
									double num2 = ((num > 0) ? Math.Min(1.0, (double)list.Count / (double)num) : Math.Min(0.95, (double)list.Count / 100000.0));
									progress?.Report(0.05 * num2);
									ReportScan(detail, indexStatusHandler, list.Count, num, fullName);
								}
							}
						}
					}
					catch (OperationCanceledException)
					{
						throw;
					}
					catch (Exception ex2)
					{
						flag2 = false;
						DumpDebug("BuildIndex probe folder='" + sourceFolder + "' enumeration failed: " + ex2.Message);
					}
				}
			}
			DumpDebug($"BuildIndex scan: {list.Count} files indexExists={flag} keyRoot={spec.RelativeKeyRoot ?? "<stem>"} expectedTotal={num}");
			if (!flag2)
			{
				DumpDebug($"BuildIndex: scan INCOMPLETE for {spec.IndexPath} (scanned {list.Count}) — index+manifest left untouched");
				progress?.Report(1.0);
			}
			else
			{
				List<string> list2 = new List<string>();
				if (indexManifest == null)
				{
					indexManifest = (flag ? BootstrapFromIndex(spec, pathRoot, list, fileName, list2) : new IndexManifest
					{
						CorpusRootName = fileName
					});
				}
				IndexPlan indexPlan = IncrementalIndexPlanner.Plan(list, indexManifest);
				list2.AddRange(indexPlan.RemovePaths);
				DumpDebug($"BuildIndex plan: new={indexPlan.NewCount} changed={indexPlan.ChangedCount} removed={indexPlan.RemovedCount} unchanged={indexPlan.UnchangedCount} totalRemoveList={list2.Count}");
				if (indexPlan.AddPaths.Count == 0 && list2.Count == 0)
				{
					if (list.Count == 0)
					{
						DumpDebug("BuildIndex: source corpus empty/absent (" + string.Join(";", spec.SourceFolders) + ") — skipping empty IndexJob");
					}
					if (flag)
					{
						try
						{
							indexPlan.UpdatedManifest.Save(spec.IndexPath);
						}
						catch (Exception ex3)
						{
							DumpDebug("manifest save failed: " + ex3.Message);
						}
					}
					progress?.Report(1.0);
					ct.ThrowIfCancellationRequested();
				}
				else
				{
					RunIndexJob(spec.IndexPath, indexPlan.AddPaths, list2, !flag, indexStatusHandler);
					try
					{
						indexPlan.UpdatedManifest.Save(spec.IndexPath);
					}
					catch (Exception ex4)
					{
						DumpDebug("manifest save failed: " + ex4.Message);
					}
					progress?.Report(1.0);
					ct.ThrowIfCancellationRequested();
				}
			}
		}, ct);
	}

	public Task UpdateIndexForFilesAsync(IndexSpec spec, IReadOnlyList<string> changedPaths, IReadOnlyList<string> deletedPaths, IProgress<double>? progress = null, IProgress<IndexProgressReport>? detail = null, CancellationToken ct = default(CancellationToken))
	{
		return Task.Run(delegate
		{
			EnsureServer();
			IndexManifest indexManifest = ((Directory.Exists(spec.IndexPath) && Directory.EnumerateFiles(spec.IndexPath, "*.ix").Any()) ? IndexManifest.Load(spec.IndexPath) : null);
			if (indexManifest == null)
			{
				DumpDebug("UpdateIndexForFiles: no manifest at " + spec.IndexPath + " -> full BuildIndex");
				BuildIndexAsync(spec, progress, detail, ct).GetAwaiter().GetResult();
			}
			else
			{
				string pathRoot = Path.GetPathRoot(spec.IndexPath);
				Path.GetFileName((spec.RelativeKeyRoot ?? spec.SourceFolders.FirstOrDefault() ?? "").TrimEnd('\\', '/'));
				List<string> list = new List<string>();
				List<string> list2 = new List<string>();
				foreach (string item in changedPaths ?? Array.Empty<string>())
				{
					ct.ThrowIfCancellationRequested();
					if (!string.IsNullOrEmpty(item) && File.Exists(item))
					{
						long length;
						long ticks;
						try
						{
							FileInfo fileInfo = new FileInfo(item);
							length = fileInfo.Length;
							ticks = fileInfo.LastWriteTimeUtc.Ticks;
						}
						catch
						{
							continue;
						}
						string text = CorpusKey.Compute(item, spec.RelativeKeyRoot, pathRoot);
						if (!string.IsNullOrEmpty(text))
						{
							if (indexManifest.Entries.TryGetValue(text, out ManifestEntry value) && !string.IsNullOrEmpty(value.IndexedPath) && !string.Equals(value.IndexedPath, item, StringComparison.OrdinalIgnoreCase))
							{
								list2.Add(value.IndexedPath);
							}
							list.Add(item);
							indexManifest.Entries[text] = new ManifestEntry
							{
								Size = length,
								Mtime = ticks,
								IndexedPath = item
							};
						}
					}
				}
				foreach (string item2 in deletedPaths ?? Array.Empty<string>())
				{
					ct.ThrowIfCancellationRequested();
					if (!string.IsNullOrEmpty(item2))
					{
						string text2 = CorpusKey.Compute(item2, spec.RelativeKeyRoot, pathRoot);
						if (!string.IsNullOrEmpty(text2) && indexManifest.Entries.TryGetValue(text2, out ManifestEntry value2))
						{
							if (!string.IsNullOrEmpty(value2.IndexedPath))
							{
								list2.Add(value2.IndexedPath);
							}
							indexManifest.Entries.Remove(text2);
						}
					}
				}
				DumpDebug($"UpdateIndexForFiles: indexPath={spec.IndexPath} add={list.Count} remove={list2.Count}");
				if (list.Count == 0 && list2.Count == 0)
				{
					progress?.Report(1.0);
				}
				else
				{
					IndexStatusHandler statusHandler = new IndexStatusHandler
					{
						Progress = progress,
						Detail = detail,
						IndexName = Path.GetFileName(spec.IndexPath.TrimEnd('\\', '/')),
						IndexLocation = (Path.GetDirectoryName(spec.IndexPath) ?? spec.IndexPath),
						DiskRoot = Path.GetPathRoot(spec.IndexPath),
						Ct = ct
					};
					RunIndexJob(spec.IndexPath, list, list2, createNew: false, statusHandler);
					try
					{
						indexManifest.Save(spec.IndexPath);
					}
					catch (Exception ex)
					{
						DumpDebug("manifest save failed: " + ex.Message);
					}
					progress?.Report(1.0);
					ct.ThrowIfCancellationRequested();
				}
			}
		}, ct);
	}

	private void RunIndexJob(string indexPath, IReadOnlyList<string> addPaths, IReadOnlyList<string> removePaths, bool createNew, IndexStatusHandler statusHandler)
	{
		string text = null;
		string text2 = null;
		try
		{
			if (addPaths.Count > 0)
			{
				text = WriteListFile(addPaths);
			}
			if (removePaths.Count > 0)
			{
				text2 = WriteListFile(removePaths);
			}
			using IndexJob indexJob = new IndexJob
			{
				IndexPath = indexPath,
				ActionCreate = createNew,
				ActionAdd = true,
				ActionRemoveListed = (removePaths.Count > 0),
				ActionRemoveDeleted = false,
				MaxMemToUseMB = 256,
				TempFileDir = LocalIndexTempDir(),
				StatusHandler = statusHandler
			};
			if (text != null)
			{
				indexJob.ToAddFileListName = text;
			}
			if (text2 != null)
			{
				indexJob.ToRemoveListName = text2;
			}
			DumpDebug($"IndexJob start indexPath={indexPath} create={createNew} add={addPaths.Count} remove={removePaths.Count}");
			Stopwatch stopwatch = Stopwatch.StartNew();
			try
			{
				indexJob.Execute();
			}
			catch (Exception ex)
			{
				stopwatch.Stop();
				DumpDebug($"IndexJob Execute THREW after {stopwatch.ElapsedMilliseconds}ms: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
				throw;
			}
			stopwatch.Stop();
			JobErrorInfo errors = indexJob.Errors;
			if (errors != null && errors.Count > 0)
			{
				StringBuilder stringBuilder = new StringBuilder();
				bool flag = false;
				for (int i = 0; i < errors.Count; i++)
				{
					string text3 = errors.Message(i) ?? string.Empty;
					stringBuilder.Append("  ").Append(text3).Append('\n');
					if (text3.IndexOf("truncated", StringComparison.OrdinalIgnoreCase) >= 0 || text3.IndexOf("Unable to access index", StringComparison.OrdinalIgnoreCase) >= 0)
					{
						flag = true;
					}
				}
				DumpDebug($"IndexJob errors ({errors.Count}):\n{stringBuilder}");
				if (flag)
				{
					throw new CorruptIndexException(indexPath, stringBuilder.ToString().Trim());
				}
			}
			long num = 0L;
			int num2 = 0;
			try
			{
				if (Directory.Exists(indexPath))
				{
					foreach (string item in Directory.EnumerateFiles(indexPath, "*.ix"))
					{
						num2++;
						num += new FileInfo(item).Length;
					}
				}
			}
			catch
			{
			}
			DumpDebug($"IndexJob done elapsed={stopwatch.ElapsedMilliseconds}ms ixFiles={num2} indexBytes={num} added={addPaths.Count} removed={removePaths.Count}");
		}
		finally
		{
			if (text != null)
			{
				try
				{
					if (File.Exists(text))
					{
						File.Delete(text);
					}
				}
				catch
				{
				}
			}
			if (text2 != null)
			{
				try
				{
					if (File.Exists(text2))
					{
						File.Delete(text2);
					}
				}
				catch
				{
				}
			}
		}
	}

	private static string WriteListFile(IReadOnlyList<string> paths)
	{
		string text = Path.Combine(Path.GetTempPath(), $"hb-idx-list-{Guid.NewGuid():N}.txt");
		using StreamWriter streamWriter = new StreamWriter(text, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		foreach (string path in paths)
		{
			streamWriter.WriteLine(path);
		}
		return text;
	}

	private static void ReportScan(IProgress<IndexProgressReport>? detail, IndexStatusHandler h, int scanned, long expectedTotal, string file)
	{
		if (detail != null)
		{
			int num = (int)Math.Round(h.ProgressBase * 100.0);
			int percentDone = (int)((expectedTotal > 0) ? Math.Min(num, (long)scanned * (long)num / expectedTotal) : 0);
			detail.Report(new IndexProgressReport(99, percentDone, scanned, expectedTotal, 0L, 0L, 0L, 0L, 0, 0, 0L, h.IndexName, h.IndexLocation, Path.GetFileName(file), Path.GetDirectoryName(file), "", 0L, 0L, 0));
		}
	}

	private IndexManifest BootstrapFromIndex(IndexSpec spec, string? currentDriveRoot, List<ScannedFile> current, string corpusName, List<string> dupRemovals)
	{
		Dictionary<string, List<string>> dictionary = EnumerateIndexedKeys(spec.IndexPath, spec.RelativeKeyRoot, currentDriveRoot);
		Dictionary<string, ScannedFile> dictionary2 = new Dictionary<string, ScannedFile>(StringComparer.OrdinalIgnoreCase);
		foreach (ScannedFile item in current)
		{
			dictionary2.TryAdd(item.Key, item);
		}
		IndexManifest indexManifest = new IndexManifest
		{
			CorpusRootName = corpusName
		};
		foreach (KeyValuePair<string, List<string>> item2 in dictionary)
		{
			if (dictionary2.TryGetValue(item2.Key, out var value))
			{
				indexManifest.Entries[item2.Key] = new ManifestEntry
				{
					Size = value.Size,
					Mtime = value.Mtime,
					IndexedPath = item2.Value[0]
				};
				for (int i = 1; i < item2.Value.Count; i++)
				{
					dupRemovals.Add(item2.Value[i]);
				}
			}
		}
		DumpDebug($"BuildIndex bootstrap: indexedKeys={dictionary.Count} seeded={indexManifest.Entries.Count} dupRemovals={dupRemovals.Count}");
		return indexManifest;
	}

	private long QuickIndexFileCount(string indexPath)
	{
		try
		{
			using SearchJob searchJob = new SearchJob();
			searchJob.IndexesToSearch.Add(indexPath);
			searchJob.Request = "xfirstword";
			searchJob.MaxFilesToRetrieve = 1;
			searchJob.Execute();
			return searchJob.FileCount;
		}
		catch (Exception ex)
		{
			DumpDebug("QuickIndexFileCount failed for " + indexPath + ": " + ex.Message);
			return 0L;
		}
	}

	private Dictionary<string, List<string>> EnumerateIndexedKeys(string indexPath, string? relativeKeyRoot, string? currentDriveRoot)
	{
		Dictionary<string, List<string>> dictionary = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
		try
		{
			using SearchJob searchJob = new SearchJob();
			searchJob.IndexesToSearch.Add(indexPath);
			searchJob.Request = "xfirstword";
			searchJob.MaxFilesToRetrieve = 2000000;
			searchJob.Execute();
			SearchResults results = searchJob.Results;
			int num = results?.Count ?? 0;
			for (int i = 0; i < num; i++)
			{
				try
				{
					results.GetNthDoc(i);
					string text = results.CurrentItem?.Filename ?? results.DocName ?? "";
					if (string.IsNullOrEmpty(text))
					{
						continue;
					}
					string text2 = CorpusKey.Compute(text, relativeKeyRoot, currentDriveRoot);
					if (!string.IsNullOrEmpty(text2))
					{
						if (!dictionary.TryGetValue(text2, out var value))
						{
							value = (dictionary[text2] = new List<string>());
						}
						value.Add(text);
					}
				}
				catch (Exception ex)
				{
					DumpDebug($"EnumerateIndexedKeys idx={i} failed: {ex.Message}");
				}
			}
		}
		catch (Exception ex2)
		{
			DumpDebug("EnumerateIndexedKeys failed for " + indexPath + ": " + ex2.Message);
		}
		return dictionary;
	}

	public int ProbeIndexContentHits(string indexPath)
	{
		if (string.IsNullOrEmpty(indexPath))
		{
			return -1;
		}
		try
		{
			EnsureServer();
			using SearchJob searchJob = new SearchJob();
			searchJob.IndexesToSearch.Add(indexPath);
			searchJob.Request = "את or אשר or כל or על or לא or הוא or זה or אין";
			searchJob.MaxFilesToRetrieve = 1;
			searchJob.Execute();
			int fileCount = searchJob.FileCount;
			DumpDebug($"ProbeIndexContentHits index={indexPath} hits={fileCount}");
			return fileCount;
		}
		catch (Exception ex)
		{
			DumpDebug("ProbeIndexContentHits failed for " + indexPath + ": " + ex.Message);
			return -1;
		}
	}

	public Task<IReadOnlyList<SearchHit>> SearchAsync(SearchQuery query, IProgress<SearchHit>? progress = null, CancellationToken ct = default(CancellationToken))
	{
		long myGeneration = Interlocked.Increment(ref _searchGeneration);
		return Task.Run((Func<IReadOnlyList<SearchHit>>)delegate
		{
			EnsureServer();
			if (string.IsNullOrEmpty(_indexPath))
			{
				throw new InvalidOperationException("Call OpenIndexAsync first.");
			}
			SearchJob lastSearchJob;
			lock (_resultsLock)
			{
				lastSearchJob = _lastSearchJob;
				_lastSearchJob = null;
				_lastSearchResults = null;
				_lastSearchQueryText = null;
				_fileIdToIndex.Clear();
			}
			try
			{
				lastSearchJob?.Dispose();
			}
			catch
			{
			}
			List<SearchHit> list = new List<SearchHit>();
			StreamingStatusHandler streamingStatusHandler = new StreamingStatusHandler(list, progress, ct, FileNameToCatalogId, query.MaxFilesToRetrieve, query.HitCountDivisor);
			SearchJob searchJob = new SearchJob
			{
				Request = query.Text,
				MaxFilesToRetrieve = query.MaxFilesToRetrieve,
				AutoStopLimit = query.MaxFilesToRetrieve * 100,
				Fuzziness = Math.Clamp(query.Fuzziness, 0, 10),
				StatusHandler = streamingStatusHandler
			};
			try
			{
				if (query.Fuzziness > 0)
				{
					searchJob.SearchFlags |= SearchFlags.dtsSearchFuzzy;
				}
				DumpDebug("SearchAsync request=" + searchJob.Request + " fuzziness=" + searchJob.Fuzziness + " maxFiles=" + searchJob.MaxFilesToRetrieve + " flags=" + searchJob.SearchFlags);
				IReadOnlyList<string> restrictToIndexPaths = query.RestrictToIndexPaths;
				HashSet<string> hashSet = ((restrictToIndexPaths != null && restrictToIndexPaths.Count > 0) ? new HashSet<string>(query.RestrictToIndexPaths, StringComparer.OrdinalIgnoreCase) : null);
				if (hashSet == null || hashSet.Contains(_indexPath))
				{
					searchJob.IndexesToSearch.Add(_indexPath);
				}
				foreach (string extraIndexPath in _extraIndexPaths)
				{
					if (hashSet == null || hashSet.Contains(extraIndexPath))
					{
						searchJob.IndexesToSearch.Add(extraIndexPath);
					}
				}
				if (searchJob.IndexesToSearch.Count == 0)
				{
					searchJob.IndexesToSearch.Add(_indexPath);
					foreach (string extraIndexPath2 in _extraIndexPaths)
					{
						searchJob.IndexesToSearch.Add(extraIndexPath2);
					}
				}
				Stopwatch stopwatch = Stopwatch.StartNew();
				searchJob.Execute();
				stopwatch.Stop();
				JobErrorInfo errors = searchJob.Errors;
				if (errors != null && errors.Count > 0)
				{
					StringBuilder stringBuilder = new StringBuilder();
					for (int i = 0; i < errors.Count; i++)
					{
						stringBuilder.Append(errors.Message(i)).Append(" | ");
					}
					DumpDebug("SearchAsync errors: " + stringBuilder);
				}
				SearchResults results = searchJob.Results;
				int num = results?.Count ?? 0;
				Dictionary<string, int> dictionary = new Dictionary<string, int>(StringComparer.Ordinal);
				Stopwatch stopwatch2 = Stopwatch.StartNew();
				for (int j = 0; j < num; j++)
				{
					if (ct.IsCancellationRequested)
					{
						break;
					}
					try
					{
						results.GetNthDoc(j);
						string text = results.CurrentItem?.Filename ?? results.DocName ?? "";
						if (!string.IsNullOrEmpty(text))
						{
							string text2 = ((FileNameToCatalogId != null) ? FileNameToCatalogId(text) : Path.GetFileNameWithoutExtension(text));
							if (!string.IsNullOrEmpty(text2))
							{
								dictionary[text2] = j;
							}
						}
					}
					catch (Exception ex)
					{
						DumpDebug($"SearchAsync map build idx={j} failed: {ex.Message}");
					}
				}
				DumpDebug($"SearchAsync done hits={list.Count} elapsed={stopwatch.ElapsedMilliseconds}ms mapBuild={stopwatch2.ElapsedMilliseconds}ms mapDocs={dictionary.Count}/{num} aborted={ct.IsCancellationRequested} capReached={streamingStatusHandler.CapReached}");
				bool flag = false;
				lock (_resultsLock)
				{
					if (Interlocked.Read(in _searchGeneration) == myGeneration)
					{
						_lastSearchJob = searchJob;
						_lastSearchResults = searchJob.Results;
						_lastSearchQueryText = query.Text;
						foreach (KeyValuePair<string, int> item in dictionary)
						{
							_fileIdToIndex[item.Key] = item.Value;
						}
						flag = true;
					}
				}
				if (!flag)
				{
					DumpDebug($"SearchAsync superseded (gen={myGeneration}) — results discarded, job disposed");
					try
					{
						searchJob.Dispose();
					}
					catch
					{
					}
				}
				return list;
			}
			catch
			{
				try
				{
					searchJob.Dispose();
				}
				catch
				{
				}
				throw;
			}
		}, ct);
	}

	private List<string> OpenIndexPaths()
	{
		List<string> list = new List<string>();
		if (!string.IsNullOrEmpty(_indexPath))
		{
			list.Add(_indexPath);
		}
		list.AddRange(_extraIndexPaths);
		return list;
	}

	public ISet<string> FilterIndexedWords(IReadOnlyCollection<string> words)
	{
		HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
		if (words == null || words.Count == 0)
		{
			return hashSet;
		}
		List<string> list = OpenIndexPaths();
		if (list.Count == 0)
		{
			foreach (string word in words)
			{
				if (!string.IsNullOrEmpty(word))
				{
					hashSet.Add(word);
				}
			}
			return hashSet;
		}
		try
		{
			EnsureServer();
			lock (_wordListLock)
			{
				foreach (string word2 in words)
				{
					if (string.IsNullOrEmpty(word2) || hashSet.Contains(word2))
					{
						continue;
					}
					foreach (string item in list)
					{
						WordListBuilder wordList = GetWordList(item);
						if (wordList != null && WordExistsInIndex(wordList, word2))
						{
							hashSet.Add(word2);
							break;
						}
					}
				}
			}
			return hashSet;
		}
		catch (Exception ex)
		{
			DumpDebug("FilterIndexedWords failed, keeping all: " + ex.Message);
			hashSet.Clear();
			foreach (string word3 in words)
			{
				if (!string.IsNullOrEmpty(word3))
				{
					hashSet.Add(word3);
				}
			}
			return hashSet;
		}
	}

	public IReadOnlyDictionary<string, long> GetIndexWordCounts(IReadOnlyCollection<string> words)
	{
		Dictionary<string, long> dictionary = new Dictionary<string, long>(StringComparer.Ordinal);
		if (words == null || words.Count == 0)
		{
			return dictionary;
		}
		List<string> list = OpenIndexPaths();
		if (list.Count == 0)
		{
			return dictionary;
		}
		try
		{
			EnsureServer();
			lock (_wordListLock)
			{
				foreach (string word in words)
				{
					if (string.IsNullOrEmpty(word) || dictionary.ContainsKey(word))
					{
						continue;
					}
					long num = 0L;
					foreach (string item in list)
					{
						WordListBuilder wordList = GetWordList(item);
						if (wordList != null)
						{
							num += ExactWordCount(wordList, word);
						}
					}
					dictionary[word] = num;
				}
			}
		}
		catch (Exception ex)
		{
			DumpDebug("GetIndexWordCounts failed: " + ex.Message);
			dictionary.Clear();
		}
		return dictionary;
	}

	public IReadOnlyList<IndexWord> SuggestIndexWords(string word, int fuzziness = 3, int maxResults = 24)
	{
		if (string.IsNullOrWhiteSpace(word))
		{
			return Array.Empty<IndexWord>();
		}
		List<string> list = OpenIndexPaths();
		if (list.Count == 0)
		{
			return Array.Empty<IndexWord>();
		}
		Dictionary<string, (long Count, int DocCount)> dictionary = new Dictionary<string, (long Count, int DocCount)>(StringComparer.Ordinal);
		try
		{
			EnsureServer();
			lock (_wordListLock)
			{
				foreach (string item in list)
				{
					WordListBuilder wordList = GetWordList(item);
					if (wordList == null)
					{
						continue;
					}
					wordList.ListMatchingWords(word, maxResults, SearchFlags.dtsSearchFuzzy, Math.Clamp(fuzziness, 1, 10));
					int count = wordList.Count;
					for (int i = 0; i < count; i++)
					{
						string nthWord = wordList.GetNthWord(i);
						if (!string.IsNullOrEmpty(nthWord) && !string.Equals(nthWord, word, StringComparison.Ordinal))
						{
							(long, int) value;
							(long, int) tuple = (dictionary.TryGetValue(nthWord, out value) ? value : default((long, int)));
							dictionary[nthWord] = (tuple.Item1 + wordList.GetNthWordCount(i), tuple.Item2 + wordList.GetNthWordDocCount(i));
						}
					}
				}
			}
		}
		catch (Exception ex)
		{
			DumpDebug("SuggestIndexWords failed for '" + word + "': " + ex.Message);
			return Array.Empty<IndexWord>();
		}
		return (from kv in dictionary
			select new IndexWord(kv.Key, kv.Value.Count, kv.Value.DocCount) into w
			orderby w.Count descending
			select w).Take(maxResults).ToList();
	}

	private static long ExactWordCount(WordListBuilder wlb, string word)
	{
		wlb.ListMatchingWords(word, 4, (SearchFlags)0, 0);
		int count = wlb.Count;
		for (int i = 0; i < count; i++)
		{
			if (string.Equals(wlb.GetNthWord(i), word, StringComparison.Ordinal))
			{
				return wlb.GetNthWordCount(i);
			}
		}
		return 0L;
	}

	private WordListBuilder? GetWordList(string indexPath)
	{
		if (_wordLists.TryGetValue(indexPath, out WordListBuilder value))
		{
			return value;
		}
		WordListBuilder wordListBuilder = new WordListBuilder();
		if (!wordListBuilder.OpenIndex(indexPath))
		{
			DumpDebug($"WordListBuilder.OpenIndex failed for {indexPath}: {wordListBuilder.LastError}");
			try
			{
				wordListBuilder.CloseIndex();
			}
			catch
			{
			}
			return null;
		}
		_wordLists[indexPath] = wordListBuilder;
		return wordListBuilder;
	}

	private static bool WordExistsInIndex(WordListBuilder wlb, string word)
	{
		wlb.ListMatchingWords(word, 4, (SearchFlags)0, 0);
		int count = wlb.Count;
		for (int i = 0; i < count; i++)
		{
			if (string.Equals(wlb.GetNthWord(i), word, StringComparison.Ordinal))
			{
				return true;
			}
		}
		return false;
	}

	public Task<string?> GenerateHighlightedPdfAsync(string fileName, string queryText, CancellationToken ct = default(CancellationToken))
	{
		return Task.FromResult<string>(null);
	}

	public Task<IReadOnlyList<HitSpan>> ExtractHighlightsAsync(SearchHit hit, CancellationToken ct = default(CancellationToken))
	{
		return Task.FromResult((IReadOnlyList<HitSpan>)Array.Empty<HitSpan>());
	}

	public Task<InBookHitInfo> GetInBookHitsAsync(string fileName, string queryText, bool extractTerms = true, int fuzziness = 0, CancellationToken ct = default(CancellationToken))
	{
		return Task.Run(delegate
		{
			EnsureServer();
			if (string.IsNullOrEmpty(_indexPath))
			{
				throw new InvalidOperationException("Call OpenIndexAsync first.");
			}
			string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
			InBookHitInfo inBookHitInfo = TryExtractFromCorpusResults(fileName, fileNameWithoutExtension, queryText, extractTerms);
			if ((object)inBookHitInfo != null)
			{
				return inBookHitInfo;
			}
			InBookHitInfo inBookHitInfo2 = TryFastPath(queryText, fileName, fileNameWithoutExtension, extractTerms, fuzziness, ct);
			if ((object)inBookHitInfo2 != null)
			{
				return inBookHitInfo2;
			}
			Stopwatch stopwatch = Stopwatch.StartNew();
			using SearchJob searchJob = new SearchJob
			{
				Request = queryText,
				MaxFilesToRetrieve = 100000,
				AutoStopLimit = 1000000,
				Fuzziness = Math.Clamp(fuzziness, 0, 10)
			};
			if (fuzziness > 0)
			{
				searchJob.SearchFlags |= SearchFlags.dtsSearchFuzzy;
			}
			searchJob.IndexesToSearch.Add(_indexPath);
			foreach (string extraIndexPath in _extraIndexPaths)
			{
				searchJob.IndexesToSearch.Add(extraIndexPath);
			}
			searchJob.Execute();
			ct.ThrowIfCancellationRequested();
			SearchResults results = searchJob.Results;
			DumpDebug($"Fallback search count={results.Count} elapsed={stopwatch.ElapsedMilliseconds}ms");
			int num = Math.Min(3, results.Count);
			for (int i = 0; i < num; i++)
			{
				results.GetNthDoc(i);
				SearchResultsItem currentItem = results.CurrentItem;
				DumpDebug($"  sample[{i}] DocName='{results.DocName}' Filename='{currentItem?.Filename}' ShortName='{currentItem?.ShortName}' DisplayName='{currentItem?.DisplayName}' DocId={currentItem?.DocId} WhichIndex={currentItem?.WhichIndex}");
			}
			for (int j = 0; j < results.Count; j++)
			{
				results.GetNthDoc(j);
				string docName = results.DocName ?? "";
				string shortName = results.CurrentItem?.ShortName ?? "";
				if (DocMatchesTarget(docName, shortName, fileName, fileNameWithoutExtension))
				{
					int docHitCount = results.DocHitCount;
					var (pages, highlightXml) = ExtractPagesAndXml(results);
					IReadOnlyList<string> readOnlyList2;
					if (!extractTerms)
					{
						IReadOnlyList<string> readOnlyList = Array.Empty<string>();
						readOnlyList2 = readOnlyList;
					}
					else
					{
						readOnlyList2 = ExtractMatchedTerms(results, j);
					}
					IReadOnlyList<string> matchedTerms = readOnlyList2;
					return new InBookHitInfo(docHitCount, pages, matchedTerms, highlightXml);
				}
			}
			return new InBookHitInfo(0, Array.Empty<int>(), Array.Empty<string>());
		}, ct);
	}

	private InBookHitInfo? TryExtractFromCorpusResults(string fileName, string stem, string queryText, bool extractTerms)
	{
		lock (_resultsLock)
		{
			if (_lastSearchResults == null)
			{
				return null;
			}
			if (!string.Equals(_lastSearchQueryText, queryText, StringComparison.Ordinal))
			{
				return null;
			}
			int num = -1;
			int value2;
			if (!string.IsNullOrEmpty(stem) && _fileIdToIndex.TryGetValue(stem, out var value))
			{
				num = value;
			}
			else if (_fileIdToIndex.TryGetValue(fileName, out value2))
			{
				num = value2;
			}
			if (num < 0)
			{
				return null;
			}
			try
			{
				_lastSearchResults.GetNthDoc(num);
				int docHitCount = _lastSearchResults.DocHitCount;
				var (readOnlyList, text) = ExtractPagesAndXml(_lastSearchResults);
				IReadOnlyList<string> readOnlyList3;
				if (!extractTerms)
				{
					IReadOnlyList<string> readOnlyList2 = Array.Empty<string>();
					readOnlyList3 = readOnlyList2;
				}
				else
				{
					readOnlyList3 = ExtractMatchedTerms(_lastSearchResults, num);
				}
				IReadOnlyList<string> readOnlyList4 = readOnlyList3;
				DumpDebug($"InBookCached HIT idx={num} hitCount={docHitCount} pages={readOnlyList.Count} terms={readOnlyList4.Count} xmlLen={text.Length}");
				return new InBookHitInfo(docHitCount, readOnlyList, readOnlyList4, text);
			}
			catch (Exception ex)
			{
				DumpDebug($"InBookCached extract idx={num} failed: {ex.Message}");
				return null;
			}
		}
	}

	private InBookHitInfo? TryFastPath(string queryText, string fileName, string stem, bool extractTerms, int fuzziness, CancellationToken ct)
	{
		string text = Path.GetFileName(fileName);
		if (!text.Contains('.'))
		{
			text += ".*";
		}
		string text2 = $"({queryText}) and xfilter(name \"{text}\")";
		Stopwatch stopwatch = Stopwatch.StartNew();
		using SearchJob searchJob = new SearchJob
		{
			Request = text2,
			MaxFilesToRetrieve = 4,
			AutoStopLimit = 100000,
			Fuzziness = Math.Clamp(fuzziness, 0, 10)
		};
		if (fuzziness > 0)
		{
			searchJob.SearchFlags |= SearchFlags.dtsSearchFuzzy;
		}
		searchJob.IndexesToSearch.Add(_indexPath);
		foreach (string extraIndexPath in _extraIndexPaths)
		{
			searchJob.IndexesToSearch.Add(extraIndexPath);
		}
		try
		{
			searchJob.Execute();
		}
		catch (Exception ex)
		{
			DumpDebug("FastPath threw: " + ex.Message);
			return null;
		}
		ct.ThrowIfCancellationRequested();
		SearchResults results = searchJob.Results;
		DumpDebug($"FastPath count={results.Count} elapsed={stopwatch.ElapsedMilliseconds}ms fuzziness={searchJob.Fuzziness} flags={searchJob.SearchFlags} request=[{text2}]");
		for (int i = 0; i < results.Count; i++)
		{
			results.GetNthDoc(i);
			string docName = results.DocName ?? "";
			string shortName = results.CurrentItem?.ShortName ?? "";
			if (DocMatchesTarget(docName, shortName, fileName, stem))
			{
				int docHitCount = results.DocHitCount;
				var (readOnlyList, text3) = ExtractPagesAndXml(results);
				IReadOnlyList<string> readOnlyList3;
				if (!extractTerms)
				{
					IReadOnlyList<string> readOnlyList2 = Array.Empty<string>();
					readOnlyList3 = readOnlyList2;
				}
				else
				{
					readOnlyList3 = ExtractMatchedTerms(results, i);
				}
				IReadOnlyList<string> readOnlyList4 = readOnlyList3;
				DumpDebug($"FastPath HIT hitCount={docHitCount} pages={readOnlyList.Count} terms={readOnlyList4.Count} xmlLen={text3.Length} totalElapsed={stopwatch.ElapsedMilliseconds}ms");
				return new InBookHitInfo(docHitCount, readOnlyList, readOnlyList4, text3);
			}
		}
		return null;
	}

	private static bool DocMatchesTarget(string docName, string shortName, string fileName, string stem)
	{
		if (string.IsNullOrEmpty(stem))
		{
			return false;
		}
		if (MatchesAny(docName) || MatchesAny(shortName))
		{
			return true;
		}
		if (docName.IndexOf('~') >= 0)
		{
			string text = TryGetLongPathName(docName);
			if (!string.IsNullOrEmpty(text) && MatchesAny(text))
			{
				return true;
			}
		}
		if (shortName.IndexOf('~') >= 0)
		{
			string text2 = TryGetLongPathName(shortName);
			if (!string.IsNullOrEmpty(text2) && MatchesAny(text2))
			{
				return true;
			}
		}
		return false;
		bool MatchesAny(string candidate)
		{
			if (!candidate.EndsWith(fileName, StringComparison.OrdinalIgnoreCase))
			{
				return string.Equals(Path.GetFileNameWithoutExtension(candidate), stem, StringComparison.OrdinalIgnoreCase);
			}
			return true;
		}
	}

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern uint GetLongPathNameW(string lpszShortPath, StringBuilder lpszLongPath, uint cchBuffer);

	private static string? TryGetLongPathName(string shortPath)
	{
		try
		{
			StringBuilder stringBuilder = new StringBuilder(260);
			uint longPathNameW = GetLongPathNameW(shortPath, stringBuilder, (uint)stringBuilder.Capacity);
			if (longPathNameW == 0)
			{
				return null;
			}
			if (longPathNameW > stringBuilder.Capacity)
			{
				stringBuilder = new StringBuilder((int)(longPathNameW + 1));
				if (GetLongPathNameW(shortPath, stringBuilder, (uint)stringBuilder.Capacity) == 0)
				{
					return null;
				}
			}
			return stringBuilder.ToString();
		}
		catch
		{
			return null;
		}
	}

	private static (IReadOnlyList<int> Pages, string Xml) ExtractPagesAndXml(SearchResults results)
	{
		string text;
		try
		{
			text = results.MakePdfWebHighlightFile() ?? "";
		}
		catch (Exception ex)
		{
			DumpDebug("MakePdfWebHighlightFile threw: " + ex);
			return (Pages: Array.Empty<int>(), Xml: "");
		}
		if (string.IsNullOrEmpty(text))
		{
			return (Pages: Array.Empty<int>(), Xml: "");
		}
		DumpXml(text);
		SortedSet<int> sortedSet = new SortedSet<int>();
		foreach (Match item in Regex.Matches(text, "<loc\\b([^>]*)>", RegexOptions.IgnoreCase))
		{
			int? num = ExtractAttr(item.Groups[1].Value, "pg");
			if (num.HasValue)
			{
				sortedSet.Add(num.Value + 1);
			}
		}
		return (Pages: sortedSet.ToList(), Xml: text);
	}

	private static IReadOnlyList<string> ExtractMatchedTerms(SearchResults results, int docIndex)
	{
		try
		{
			using SearchReportJob searchReportJob = results.NewSearchReportJob();
			searchReportJob.OutputToString = true;
			searchReportJob.OutputStringMaxSize = 4194304;
			searchReportJob.BeforeHit = "<<<HBHIT>>>";
			searchReportJob.AfterHit = "<<</HBHIT>>>";
			searchReportJob.WordsOfContext = 1;
			searchReportJob.SelectItems(docIndex, docIndex);
			searchReportJob.Execute();
			string text = searchReportJob.OutputString ?? string.Empty;
			HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
			List<string> list = new List<string>();
			int startIndex = 0;
			while (true)
			{
				int num = text.IndexOf("<<<HBHIT>>>", startIndex, StringComparison.Ordinal);
				if (num < 0)
				{
					break;
				}
				int num2 = num + "<<<HBHIT>>>".Length;
				int num3 = text.IndexOf("<<</HBHIT>>>", num2, StringComparison.Ordinal);
				if (num3 < 0)
				{
					break;
				}
				string text2 = text.Substring(num2, num3 - num2).Trim();
				startIndex = num3 + "<<</HBHIT>>>".Length;
				if (text2.Length > 0 && hashSet.Add(text2))
				{
					list.Add(text2);
				}
			}
			DumpDebug($"ReportJob terms={list.Count} reportLen={text.Length}");
			return list;
		}
		catch (Exception ex)
		{
			DumpDebug("ExtractMatchedTerms failed: " + ex.Message);
			return Array.Empty<string>();
		}
	}

	private static int? ExtractAttr(string attrs, string name)
	{
		Match match = Regex.Match(attrs, "\\b" + name + "\\s*=\\s*[\"']?(?<v>-?\\d+)[\"']?", RegexOptions.IgnoreCase);
		if (match.Success && int.TryParse(match.Groups["v"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
		{
			return result;
		}
		return null;
	}

	public void Dispose()
	{
		SearchJob lastSearchJob;
		lock (_resultsLock)
		{
			lastSearchJob = _lastSearchJob;
			_lastSearchJob = null;
			_lastSearchResults = null;
			_fileIdToIndex.Clear();
		}
		try
		{
			lastSearchJob?.Dispose();
		}
		catch
		{
		}
		lock (_wordListLock)
		{
			foreach (WordListBuilder value in _wordLists.Values)
			{
				try
				{
					value.CloseIndex();
				}
				catch
				{
				}
			}
			_wordLists.Clear();
		}
		_server?.Dispose();
		_server = null;
	}

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern uint GetShortPathNameW(string lpszLongPath, StringBuilder lpszShortPath, uint cchBuffer);

	private static string? TryGetShortPathName(string longPath)
	{
		try
		{
			StringBuilder stringBuilder = new StringBuilder(260);
			uint shortPathNameW = GetShortPathNameW(longPath, stringBuilder, (uint)stringBuilder.Capacity);
			if (shortPathNameW == 0)
			{
				return null;
			}
			if (shortPathNameW > stringBuilder.Capacity)
			{
				stringBuilder = new StringBuilder((int)(shortPathNameW + 1));
				if (GetShortPathNameW(longPath, stringBuilder, (uint)stringBuilder.Capacity) == 0)
				{
					return null;
				}
			}
			string text = stringBuilder.ToString();
			return string.Equals(text, longPath, StringComparison.Ordinal) ? null : text;
		}
		catch
		{
			return null;
		}
	}

	private static void DumpDebug(string content)
	{
		try
		{
			string text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HebrewBooks", "logs");
			Directory.CreateDirectory(text);
			string path = Path.Combine(text, "dtsearch-debug.xml");
			string text2 = "\n--- " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + " ---\n";
			File.AppendAllText(path, text2 + content + "\n");
		}
		catch
		{
		}
	}

	private static void DumpXml(string xml)
	{
		try
		{
			string text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HebrewBooks", "logs");
			Directory.CreateDirectory(text);
			string path = Path.Combine(text, "dtsearch-highlights.xml");
			string text2 = "\n<!-- " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + " -->\n";
			File.AppendAllText(path, text2 + xml + "\n");
		}
		catch
		{
		}
	}
}
