using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using HebrewBooks.Core.Abstractions;
using HebrewBooks.Core.Models;
using HebrewBooks.Diagnostics;
using HebrewBooks.Diagnostics.Checks;
using HebrewBooks.Diagnostics.Ui;
using HebrewBooks.Infrastructure.Settings;
using HebrewBooks.Search;
using HebrewBooks.Search.Incremental;
using HebrewBooks.Services.Background;
using HebrewBooks.UI.Resources;

namespace HebrewBooks.UI.Views;

public partial class DiagnosticsPage : Page
{


	public DiagnosticsPage(IPathResolver paths, JsonSettingsStore settings, ISearchEngine engine, BackgroundProcessorService background)
	{
		InitializeComponent();
		DiagnosticsViewModel vm = new DiagnosticsViewModel((Action<DiagnosticResult> onResult, CancellationToken ct) => RunReportAsync(paths, settings, engine, onResult, ct), (DiagnosticFix fix, CancellationToken ct) => ApplyIndexFixAsync(fix, paths, engine, background, ct));
		View.DataContext = vm;
		base.Loaded += async delegate
		{
			if (!vm.HasRun)
			{
				await vm.RunAsync();
			}
		};
	}

	private static async Task<DiagnosticReport> RunReportAsync(IPathResolver paths, JsonSettingsStore settings, ISearchEngine engine, Action<DiagnosticResult> onResult, CancellationToken ct)
	{
		DiagnosticContext context = DiagnosticContext.FromResolved(settings, paths);
		Func<CancellationToken, Task<EngineProbeResult>> engineProbe = async delegate(CancellationToken c)
		{
			try
			{
				await engine.OpenIndexAsync(paths.IndexesRoot, c);
				return new EngineProbeResult(Loaded: true, SharedStrings.S985, null);
			}
			catch (Exception ex)
			{
				if (engine is DtSearchNetEngine { IsServerLoaded: not false })
				{
					return new EngineProbeResult(Loaded: true, SharedStrings.S986, ex.ToString());
				}
				return new EngineProbeResult(Loaded: false, null, ex.ToString());
			}
		};
		Func<DiagnosticContext, CancellationToken, Task<IReadOnlyList<IndexContentProbe>>> contentProbe = (DiagnosticContext c, CancellationToken token) => ProbeIndexContentAsync(paths, engine, token);
		return await DiagnosticEngine.CreateDefault(null, engineProbe, contentProbe).RunAsync(context, ct, onResult);
	}

	private static async Task<IReadOnlyList<IndexContentProbe>> ProbeIndexContentAsync(IPathResolver paths, ISearchEngine engine, CancellationToken ct)
	{
		(string, string, string)[] array = new(string, string, string)[3]
		{
			(SharedStrings.S987, "index.pdf", paths.IndexesRoot),
			(SharedStrings.S988, "index.otzraya", paths.OtzrayaIndexPath),
			(SharedStrings.S55, "index.personal", paths.PersonalIndexPath)
		};
		List<IndexContentProbe> probes = new List<IndexContentProbe>();
		(string Corpus, string IdBase, string IndexPath)[] array2 = array;
		for (int i = 0; i < array2.Length; i++)
		{
			var (corpus, idBase, indexPath) = array2[i];
			ct.ThrowIfCancellationRequested();
			if (string.IsNullOrEmpty(indexPath) || !Directory.Exists(indexPath))
			{
				continue;
			}
			long docs = IndexManifest.Load(indexPath)?.Entries.Count ?? 0;
			if (docs <= 0)
			{
				continue;
			}
			string error = null;
			int probeHits;
			try
			{
				DtSearchNetEngine dt = engine as DtSearchNetEngine;
				int num = ((dt == null) ? (-1) : (await Task.Run(() => dt.ProbeIndexContentHits(indexPath), ct).ConfigureAwait(continueOnCapturedContext: false)));
				probeHits = num;
			}
			catch (Exception ex)
			{
				probeHits = -1;
				error = ex.ToString();
			}
			probes.Add(new IndexContentProbe(corpus, idBase, indexPath, docs, probeHits, error));
		}
		return probes;
	}

	private static async Task<FixOutcome> ApplyIndexFixAsync(DiagnosticFix fix, IPathResolver paths, ISearchEngine engine, BackgroundProcessorService background, CancellationToken ct)
	{
		IndexSpec indexSpec = fix.Id switch
		{
			"index.pdf.build" => new IndexSpec(paths.IndexesRoot, new string[1] { paths.PdfsRoot }, UseNativeEnumeration: true), 
			"index.otzraya.build" => new IndexSpec(paths.OtzrayaIndexPath, new string[1] { paths.OtzrayaRoot }, UseNativeEnumeration: false, paths.OtzrayaRoot), 
			"index.personal.build" => new IndexSpec(paths.PersonalIndexPath, new string[1] { paths.PersonalRoot }, UseNativeEnumeration: false, paths.PersonalRoot), 
			_ => null, 
		};
		if ((object)indexSpec == null)
		{
			return FixOutcome.Fail(SharedStrings.S989);
		}
		await background.EnqueueAsync(new IndexBuildJob(indexSpec, engine), ct);
		return FixOutcome.Ok(SharedStrings.S990);
	}


}
