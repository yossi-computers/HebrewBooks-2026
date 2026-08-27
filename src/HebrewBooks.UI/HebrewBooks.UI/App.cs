using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using HebrewBooks.Core;
using HebrewBooks.Core.Abstractions;
using HebrewBooks.Core.Models;
using HebrewBooks.Data;
using HebrewBooks.Data.Repositories;
using HebrewBooks.Infrastructure.OS;
using HebrewBooks.Infrastructure.Paths;
using HebrewBooks.Infrastructure.Settings;
using HebrewBooks.Search;
using HebrewBooks.Services.Background;
using HebrewBooks.Services.Calendar;
using HebrewBooks.Services.Catalog;
using HebrewBooks.Services.Downloader;
using HebrewBooks.Services.Otzraya;
using HebrewBooks.Services.Pdf;
using HebrewBooks.Services.Personal;
using HebrewBooks.Services.Provisioning;
using HebrewBooks.Services.Search;
using HebrewBooks.Services.TextLayer;
using HebrewBooks.Services.Toc;
using HebrewBooks.Services.Updates;
using HebrewBooks.Services.WorkAreas;
using HebrewBooks.UI.Behaviors;
using HebrewBooks.UI.Controls;
using HebrewBooks.UI.Dialogs;
using HebrewBooks.UI.Navigation;
using HebrewBooks.UI.Resources;
using HebrewBooks.UI.Services;
using HebrewBooks.UI.ViewModels;
using HebrewBooks.UI.Views;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;
using Serilog;
using Serilog.Events;
using Velopack;
using Wpf.Ui;

namespace HebrewBooks.UI;

public partial class App : Application, IStyleConnector
{
	private sealed partial class DirectProgress<T>(Action<T> handler) : IProgress<T>
	{
		public void Report(T value)
		{
			handler(value);
		}
	}

	private IHost? _host;

	private const string RemoteCiteDbUrl = "https://github.com/HebrewBooks-2026/Hebrewbooks-Releases/releases/download/prerequisites/cite.db";

	private const string RemoteCiteVersionUrl = "https://github.com/HebrewBooks-2026/Hebrewbooks-Releases/releases/download/prerequisites/cite.db.version";

	private const string RemoteSynonymsDbUrl = "https://github.com/HebrewBooks-2026/Hebrewbooks-Releases/releases/download/prerequisites/synonyms.db";

	private const string RemoteSynonymsShaUrl = "https://github.com/HebrewBooks-2026/Hebrewbooks-Releases/releases/download/prerequisites/synonyms.db.sha256";

	private const string RemoteShelvesDbUrl = "https://github.com/HebrewBooks-2026/Hebrewbooks-Releases/releases/download/prerequisites/shelves-publisher.db";

	private const string RemoteShelvesShaUrl = "https://github.com/HebrewBooks-2026/Hebrewbooks-Releases/releases/download/prerequisites/shelves-publisher.db.sha256";

	private SingleInstanceManager? _singleInstance;

	private static readonly TimeSpan OrphanSweepMinAge;

	private static bool _componentErrorShown;

	public static readonly Queue<OpenTabsPersistence.SavedTabs> PendingWindowTabs;


	public static IServiceProvider Services { get; private set; }

	public static bool IsProtectMode { get; private set; }

	public static bool IsNetworkInstall { get; private set; }

	public static string ResolvedLanguage { get; private set; }

	public static FlowDirection WindowFlow => LanguageService.FlowFor(ResolvedLanguage);

	public static bool IsTabletMode { get; private set; }

	internal static string SynonymsDbPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HebrewBooks", "synonyms.db");

	private static string PublisherShelvesDbPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HebrewBooks", "shelves-publisher.db");

	public static string? PendingExternalSearch { get; set; }

	public static LibraryViewModel? MainLibraryViewModel { get; set; }

	private static string InstallFlagPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HebrewBooks", ".webview2-install-attempted");

	public static event Action<bool>? TabletModeChanged;

	static App()
	{
		Services = null;
		ResolvedLanguage = "he";
		OrphanSweepMinAge = TimeSpan.FromSeconds(30.0);
		PendingWindowTabs = new Queue<OpenTabsPersistence.SavedTabs>();
		ScrollViewer.PanningModeProperty.OverrideMetadata(typeof(ScrollViewer), new FrameworkPropertyMetadata(PanningMode.VerticalFirst));
		EventManager.RegisterClassHandler(typeof(ScrollViewer), FrameworkElement.LoadedEvent, (RoutedEventHandler)delegate(object s, RoutedEventArgs _)
		{
			if (s is ScrollViewer { PanningMode: PanningMode.None } scrollViewer)
			{
				scrollViewer.PanningMode = PanningMode.VerticalFirst;
			}
		});
		WheelScrollSpeed.Install();
		TouchScroll.Install();
	}

	public static bool IsBetaBuild()
	{
		try
		{
			string text = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
			if (string.IsNullOrEmpty(text))
			{
				return true;
			}
			return text.Contains('-');
		}
		catch
		{
			return true;
		}
	}

	public static string CurrentVersionString()
	{
		try
		{
			return Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "dev";
		}
		catch
		{
			return "dev";
		}
	}

	private static string Sha256OfFile(string path)
	{
		using SHA256 sHA = SHA256.Create();
		using FileStream inputStream = File.OpenRead(path);
		return Convert.ToHexString(sHA.ComputeHash(inputStream)).ToLowerInvariant();
	}

	private static void SeedAndRefreshSynonymsDb()
	{
		string active = SynonymsDbPath;
		string activeShaPath = active + ".sha256";
		string path = active + ".seedsha";
		string text = Path.Combine(AppContext.BaseDirectory, "synonyms.db");
		try
		{
			if (File.Exists(text))
			{
				string text2 = Sha256OfFile(text);
				string text3 = (File.Exists(path) ? File.ReadAllText(path).Trim() : "");
				if (!File.Exists(active) || text3 != text2)
				{
					Directory.CreateDirectory(Path.GetDirectoryName(active));
					File.Copy(text, active, overwrite: true);
					File.WriteAllText(activeShaPath, text2);
					File.WriteAllText(path, text2);
					Log.Information("synonyms.db: seeded writable copy from bundled ({Sha})", text2.Substring(0, 8));
				}
			}
		}
		catch (Exception exception)
		{
			Log.Warning(exception, "synonyms.db: could not seed writable copy");
		}
		if (IsProtectMode || IsNetworkInstall)
		{
			return;
		}
		Task.Run(async delegate
		{
			_ = 5;
			try
			{
				using HttpClient http = new HttpClient
				{
					Timeout = TimeSpan.FromMinutes(5.0)
				};
				using HttpResponseMessage shaResp = await http.GetAsync("https://github.com/HebrewBooks-2026/Hebrewbooks-Releases/releases/download/prerequisites/synonyms.db.sha256").ConfigureAwait(continueOnCapturedContext: false);
				if (!shaResp.IsSuccessStatusCode)
				{
					return;
				}
				string remoteSha = (await shaResp.Content.ReadAsStringAsync().ConfigureAwait(continueOnCapturedContext: false)).Trim().ToLowerInvariant();
				if (remoteSha.Length != 64)
				{
					return;
				}
				string text4 = (File.Exists(activeShaPath) ? File.ReadAllText(activeShaPath).Trim().ToLowerInvariant() : (File.Exists(active) ? Sha256OfFile(active) : ""));
				if (remoteSha == text4)
				{
					return;
				}
				using HttpResponseMessage dbResp = await http.GetAsync("https://github.com/HebrewBooks-2026/Hebrewbooks-Releases/releases/download/prerequisites/synonyms.db", HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(continueOnCapturedContext: false);
				dbResp.EnsureSuccessStatusCode();
				Directory.CreateDirectory(Path.GetDirectoryName(active));
				string tmp = active + ".download";
				long total = dbResp.Content.Headers.ContentLength ?? (-1);
				long length;
				using (Stream src = await dbResp.Content.ReadAsStreamAsync().ConfigureAwait(continueOnCapturedContext: false))
				{
					using FileStream dst = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
					await src.CopyToAsync(dst).ConfigureAwait(continueOnCapturedContext: false);
					await dst.FlushAsync().ConfigureAwait(continueOnCapturedContext: false);
					length = dst.Length;
				}
				if (total > 0 && length != total)
				{
					try
					{
						File.Delete(tmp);
						return;
					}
					catch
					{
						return;
					}
				}
				if (Sha256OfFile(tmp) != remoteSha)
				{
					try
					{
						File.Delete(tmp);
					}
					catch
					{
					}
					Log.Warning("synonyms.db: hash mismatch after download — discarded");
					return;
				}
				if (File.Exists(active))
				{
					File.Replace(tmp, active, null);
				}
				else
				{
					File.Move(tmp, active);
				}
				File.WriteAllText(activeShaPath, remoteSha);
				Log.Information("synonyms.db: refreshed from GitHub ({Sha}, {Len} bytes) — active next launch", remoteSha.Substring(0, 8), length);
			}
			catch (Exception ex)
			{
				Log.Information("synonyms.db: GitHub refresh skipped ({Msg})", ex.Message);
			}
		});
	}

	private static void RefreshPublisherShelvesDb()
	{
		string active = PublisherShelvesDbPath;
		string activeShaPath = active + ".sha256";
		string path = active + ".seedsha";
		string text = Path.Combine(AppContext.BaseDirectory, "shelves-publisher.db");
		try
		{
			if (File.Exists(text))
			{
				string text2 = Sha256OfFile(text);
				string text3 = (File.Exists(path) ? File.ReadAllText(path).Trim() : "");
				if (!File.Exists(active) || text3 != text2)
				{
					Directory.CreateDirectory(Path.GetDirectoryName(active));
					File.Copy(text, active, overwrite: true);
					File.WriteAllText(activeShaPath, text2);
					File.WriteAllText(path, text2);
					Log.Information("shelves-publisher.db: seeded writable copy from bundled ({Sha})", text2.Substring(0, 8));
				}
			}
		}
		catch (Exception exception)
		{
			Log.Warning(exception, "shelves-publisher.db: could not seed writable copy");
		}
		if (IsProtectMode || IsNetworkInstall)
		{
			return;
		}
		Task.Run(async delegate
		{
			_ = 5;
			try
			{
				using HttpClient http = new HttpClient
				{
					Timeout = TimeSpan.FromMinutes(5.0)
				};
				using HttpResponseMessage shaResp = await http.GetAsync("https://github.com/HebrewBooks-2026/Hebrewbooks-Releases/releases/download/prerequisites/shelves-publisher.db.sha256").ConfigureAwait(continueOnCapturedContext: false);
				if (!shaResp.IsSuccessStatusCode)
				{
					return;
				}
				string remoteSha = (await shaResp.Content.ReadAsStringAsync().ConfigureAwait(continueOnCapturedContext: false)).Trim().ToLowerInvariant();
				if (remoteSha.Length != 64)
				{
					return;
				}
				string text4 = (File.Exists(activeShaPath) ? File.ReadAllText(activeShaPath).Trim().ToLowerInvariant() : (File.Exists(active) ? Sha256OfFile(active) : ""));
				if (remoteSha == text4)
				{
					return;
				}
				using HttpResponseMessage dbResp = await http.GetAsync("https://github.com/HebrewBooks-2026/Hebrewbooks-Releases/releases/download/prerequisites/shelves-publisher.db", HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(continueOnCapturedContext: false);
				dbResp.EnsureSuccessStatusCode();
				Directory.CreateDirectory(Path.GetDirectoryName(active));
				string tmp = active + ".download";
				long total = dbResp.Content.Headers.ContentLength ?? (-1);
				long length;
				using (Stream src = await dbResp.Content.ReadAsStreamAsync().ConfigureAwait(continueOnCapturedContext: false))
				{
					using FileStream dst = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
					await src.CopyToAsync(dst).ConfigureAwait(continueOnCapturedContext: false);
					await dst.FlushAsync().ConfigureAwait(continueOnCapturedContext: false);
					length = dst.Length;
				}
				if (total > 0 && length != total)
				{
					try
					{
						File.Delete(tmp);
						return;
					}
					catch
					{
						return;
					}
				}
				if (Sha256OfFile(tmp) != remoteSha)
				{
					try
					{
						File.Delete(tmp);
					}
					catch
					{
					}
					Log.Warning("shelves-publisher.db: hash mismatch after download — discarded");
					return;
				}
				if (File.Exists(active))
				{
					File.Replace(tmp, active, null);
				}
				else
				{
					File.Move(tmp, active);
				}
				File.WriteAllText(activeShaPath, remoteSha);
				Log.Information("shelves-publisher.db: refreshed from GitHub ({Sha}, {Len} bytes)", remoteSha.Substring(0, 8), length);
			}
			catch (Exception ex)
			{
				Log.Information("shelves-publisher.db: refresh skipped ({Msg})", ex.Message);
			}
		});
	}

	protected override void OnStartup(StartupEventArgs e)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		VelopackApp.Build().OnAfterInstallFastCallback(delegate
		{
			RegisterProtocolForVelopack();
		}).OnAfterUpdateFastCallback(delegate
		{
			RegisterProtocolForVelopack();
		})
			.OnBeforeUninstallFastCallback(delegate
			{
				OnVelopackUninstall();
			})
			.Run();
		EnsureWebView2Runtime();
		string text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HebrewBooks", "logs");
		Directory.CreateDirectory(text);
		Log.Logger = new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Debug().WriteTo.File(Path.Combine(text, "bookshelf-.log"), LogEventLevel.Information, "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}", null, retainedFileCountLimit: 14, fileSizeLimitBytes: 1073741824L, levelSwitch: null, buffered: false, shared: false, flushToDiskInterval: null, rollingInterval: RollingInterval.Day).CreateLogger();
		Log.Information("TIMING: startup serilog ready +{Ms}ms (pid={Pid})", stopwatch.ElapsedMilliseconds, Environment.ProcessId);
		try
		{
			KillStaleOrphanInstances();
		}
		catch (Exception exception)
		{
			Log.Warning(exception, "orphan-instance sweep failed");
		}
		_singleInstance = new SingleInstanceManager();
		if (!(e.Args.Contains("--relaunch") ? _singleInstance.TryAcquireWaiting(15000) : _singleInstance.TryAcquire()))
		{
			_singleInstance.SendToPrimary(string.Join("\n", e.Args));
			Log.Information("SingleInstance: secondary launch forwarded {Count} arg(s); exiting", e.Args.Length);
			_singleInstance.Dispose();
			Log.CloseAndFlush();
			Environment.Exit(0);
		}
		_singleInstance.StartServer(HandleForwardedArgs);
		try
		{
			KillOurWebView2Processes();
		}
		catch (Exception exception2)
		{
			Log.Warning(exception2, "WebView2 orphan sweep on startup failed");
		}
		IsProtectMode = ProtectMode.ArgsRequest(e.Args);
		string propertyValue = (IsProtectMode ? "cli-arg" : "none");
		if (!IsProtectMode && ProtectMode.MarkerPresent())
		{
			IsProtectMode = true;
			propertyValue = "marker-file";
		}
		try
		{
			BookshelfOptions bookshelfOptions = new JsonSettingsStore().Load();
			IsNetworkInstall = bookshelfOptions.NetworkInstall;
			if (bookshelfOptions.NetworkInstall && bookshelfOptions.ForceProtectMode)
			{
				if (!IsProtectMode)
				{
					propertyValue = "settings";
				}
				IsProtectMode = true;
			}
		}
		catch (Exception exception3)
		{
			Log.Warning(exception3, "Failed to read ForceProtectMode from settings.json");
		}
		Log.Information("protect-mode: {Mode} (source={Source}); network-install: {Net}", IsProtectMode ? "ON" : "off", propertyValue, IsNetworkInstall ? "ON" : "off");
		base.DispatcherUnhandledException += delegate(object _, DispatcherUnhandledExceptionEventArgs e2)
		{
			if (IsBenignFrameworkRace(e2.Exception))
			{
				Log.Debug(e2.Exception, "Ignored benign WPF-UI TitleBar HWND race");
			}
			else
			{
				Log.Fatal(e2.Exception, "Dispatcher unhandled exception");
				TryReportMissingComponent(e2.Exception);
			}
			e2.Handled = true;
		};
		AppDomain.CurrentDomain.UnhandledException += delegate(object _, UnhandledExceptionEventArgs e2)
		{
			Exception obj3 = e2.ExceptionObject as Exception;
			Log.Fatal(obj3, "AppDomain unhandled exception");
			TryReportMissingComponent(obj3);
		};
		TaskScheduler.UnobservedTaskException += delegate(object? _, UnobservedTaskExceptionEventArgs e2)
		{
			Log.Error(e2.Exception, "Unobserved task exception");
			TryReportMissingComponent(e2.Exception);
			e2.SetObserved();
		};
		ResolvedLanguage = LanguageService.Apply(new JsonSettingsStore().Load().Language);
		SplashHost splash = new SplashHost();
		try
		{
			splash.Show();
		}
		catch (Exception exception4)
		{
			Log.Warning(exception4, "Splash failed to show (non-fatal)");
		}
		Log.Information("TIMING: startup splash shown +{Ms}ms", stopwatch.ElapsedMilliseconds);
		try
		{
			splash.SetStatus(SharedStrings.S492);
			_host = BuildHost(e.Args);
			Services = _host.Services;
			Log.Information("TIMING: startup IHost built +{Ms}ms", stopwatch.ElapsedMilliseconds);
			JsonSettingsStore requiredService = Services.GetRequiredService<JsonSettingsStore>();
			new IniMigration(requiredService).MigrateIfNeeded();
			BookshelfOptions bookshelfOptions2 = requiredService.Load();
			if (!string.IsNullOrWhiteSpace(bookshelfOptions2.Search.HighlightColor))
			{
				PdfJsHost.BroadcastHighlightColor(bookshelfOptions2.Search.HighlightColor);
			}
			PdfJsHost.BroadcastPageRailEnabled(bookshelfOptions2.View.ShowPageRail);
			PdfJsHost.BroadcastRegionCopyDpi(bookshelfOptions2.View.RegionCopyDpi);
			PdfJsHost.BroadcastFuzziness(Math.Clamp(bookshelfOptions2.Search.Fuzziness, 0, 10));
			DataRootResolver requiredService2 = Services.GetRequiredService<DataRootResolver>();
			splash.SetStatus(SharedStrings.S506);
			Log.Information("TIMING: startup pre data-root resolve +{Ms}ms", stopwatch.ElapsedMilliseconds);
			base.ShutdownMode = ShutdownMode.OnExplicitShutdown;
			string text2 = requiredService2.Resolve(e.Args, delegate
			{
				try
				{
					splash.Close();
				}
				catch
				{
				}
				SetupWizardWindow requiredService4 = Services.GetRequiredService<SetupWizardWindow>();
				requiredService4.ShowDialog();
				return requiredService4.Result;
			});
			try
			{
				if (requiredService.Load().UseOnlineService)
				{
					string text3 = requiredService2.FindConnectedLibraryDrive(text2);
					if (text3 != null)
					{
						try
						{
							splash.Close();
						}
						catch
						{
						}
						if (HebrewMessageBox.Show(SharedStrings.S507, SharedStrings.S508, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
						{
							requiredService2.PersistRoot(text3);
							requiredService.Update(delegate(BookshelfOptions o)
							{
								o.UseOnlineService = false;
							});
							text2 = text3;
							Log.Information("Switched from online mode to connected library drive: {Root}", text3);
						}
					}
				}
			}
			catch (Exception exception5)
			{
				Log.Warning(exception5, "online→drive switch check failed (continuing in current mode)");
			}
			PathResolver pathResolver = new PathResolver(text2, requiredService.Load());
			((PathResolverHolder)Services.GetRequiredService<IPathResolver>()).Set(pathResolver);
			Log.Information("TIMING: startup data-root resolved +{Ms}ms (root={Root})", stopwatch.ElapsedMilliseconds, text2);
			try
			{
				BookshelfOptions bookshelfOptions3 = requiredService.Load();
				string text4 = bookshelfOptions3.EffectiveCatalogMaster();
				if (!string.IsNullOrWhiteSpace(text4))
				{
					splash.SetStatus(SharedStrings.S509);
					CatalogSyncService.SyncInfo syncInfo = new CatalogSyncService().SyncIfNeeded(text4, pathResolver.CatalogDbPath);
					switch (syncInfo.Result)
					{
					case CatalogSyncService.SyncResult.Copied:
						Log.Information("CatalogSync: copied master → local ({Bytes:N0} bytes, {Ms}ms) master={Master}", syncInfo.BytesCopied, syncInfo.ElapsedMs, text4);
						break;
					case CatalogSyncService.SyncResult.MasterNotFound:
						Log.Information("CatalogSync: master not found at {Master} — keeping local copy", text4);
						break;
					case CatalogSyncService.SyncResult.Failed:
						Log.Warning("CatalogSync: failed ({Msg}) — keeping local copy", syncInfo.FailureMessage);
						break;
					case CatalogSyncService.SyncResult.Skipped:
						Log.Debug("CatalogSync: skipped (local up-to-date)");
						break;
					}
				}
				else
				{
					Log.Information("CatalogSync: not run (NetworkInstall={Net}, master={Master})", bookshelfOptions3.NetworkInstall, "unset");
				}
			}
			catch (Exception exception6)
			{
				Log.Warning(exception6, "CatalogSync wrapper failed (non-fatal)");
			}
			try
			{
				BookshelfOptions bookshelfOptions4 = requiredService.Load();
				if (!bookshelfOptions4.NetworkInstall && !string.Equals(bookshelfOptions4.Paths.InstallType, "Empty", StringComparison.OrdinalIgnoreCase) && File.Exists(pathResolver.CatalogDbPath) && TryDetectCatalogCorruption(pathResolver.CatalogDbPath, bookshelfOptions4.NetworkInstall, out Exception corruption))
				{
					Log.Error(corruption, "Startup: catalog file is corrupt (malformed) — offering re-download");
					if (!RecoverCorruptCatalogInteractive(ref splash, text2, pathResolver, bookshelfOptions4, stopwatch))
					{
						Shutdown();
						return;
					}
				}
			}
			catch (Exception exception7)
			{
				Log.Warning(exception7, "Startup catalog corruption probe failed (non-fatal)");
			}
			try
			{
				string hebAramPath = pathResolver.HebAramPath;
				if (!File.Exists(hebAramPath))
				{
					string text5 = Path.Combine(AppContext.BaseDirectory, "HebAram.DB");
					if (File.Exists(text5))
					{
						Directory.CreateDirectory(Path.GetDirectoryName(hebAramPath));
						File.Copy(text5, hebAramPath, overwrite: false);
						Log.Information("HebAram: seeded USB copy {Usb} from bundled {Bundled}", hebAramPath, text5);
					}
				}
			}
			catch (Exception exception8)
			{
				Log.Warning(exception8, "HebAram: could not seed USB copy");
			}
			string hebAramUsbPath = pathResolver.HebAramPath;
			if (!IsProtectMode)
			{
				Task.Run(async delegate
				{
					try
					{
						using HttpClient http = new HttpClient
						{
							Timeout = TimeSpan.FromSeconds(20.0)
						};
						byte[] array = await http.GetByteArrayAsync("https://raw.githubusercontent.com/HebrewBooks-2026/Hebrewbooks-Releases/main/HebAram.DB").ConfigureAwait(continueOnCapturedContext: false);
						if (array == null || array.Length <= 0)
						{
							return;
						}
						byte[] array2 = (File.Exists(hebAramUsbPath) ? File.ReadAllBytes(hebAramUsbPath) : null);
						if (array2 != null && array.AsSpan().SequenceEqual(array2))
						{
							return;
						}
						string directoryName = Path.GetDirectoryName(hebAramUsbPath);
						if (!string.IsNullOrEmpty(directoryName))
						{
							Directory.CreateDirectory(directoryName);
						}
						string text9 = hebAramUsbPath + ".download";
						File.WriteAllBytes(text9, array);
						if (File.Exists(hebAramUsbPath))
						{
							File.Replace(text9, hebAramUsbPath, null);
						}
						else
						{
							File.Move(text9, hebAramUsbPath);
						}
						Log.Information("HebAram: refreshed from GitHub ({Len} bytes) → {Path}", array.Length, hebAramUsbPath);
					}
					catch (Exception ex2)
					{
						Log.Information("HebAram: GitHub refresh skipped ({Msg})", ex2.Message);
					}
				});
			}
			try
			{
				string citeDbPath = pathResolver.CiteDbPath;
				string text6 = citeDbPath + ".version";
				string text7 = Path.Combine(AppContext.BaseDirectory, "cite.db");
				string text8 = text7 + ".version";
				if (File.Exists(text7) && (!File.Exists(citeDbPath) || ReadCiteVer(text6) < ReadCiteVer(text8)))
				{
					Directory.CreateDirectory(Path.GetDirectoryName(citeDbPath));
					File.Copy(text7, citeDbPath, overwrite: true);
					if (File.Exists(text8))
					{
						File.Copy(text8, text6, overwrite: true);
					}
					Log.Information("cite.db: seeded/updated USB copy from bundled (v{V})", ReadCiteVer(text8));
				}
			}
			catch (Exception exception9)
			{
				Log.Warning(exception9, "cite.db: could not seed USB copy");
			}
			string citeUsbPath = pathResolver.CiteDbPath;
			if (!IsProtectMode)
			{
				Task.Run(async delegate
				{
					_ = 5;
					try
					{
						using HttpClient http = new HttpClient
						{
							Timeout = Timeout.InfiniteTimeSpan
						};
						using HttpResponseMessage verResp = await http.GetAsync("https://github.com/HebrewBooks-2026/Hebrewbooks-Releases/releases/download/prerequisites/cite.db.version").ConfigureAwait(continueOnCapturedContext: false);
						if (!verResp.IsSuccessStatusCode || !int.TryParse((await verResp.Content.ReadAsStringAsync().ConfigureAwait(continueOnCapturedContext: false)).Trim(), out var remoteVer))
						{
							return;
						}
						string usbVerPath = citeUsbPath + ".version";
						if (remoteVer <= ReadCiteVer(usbVerPath))
						{
							return;
						}
						using HttpResponseMessage dbResp = await http.GetAsync("https://github.com/HebrewBooks-2026/Hebrewbooks-Releases/releases/download/prerequisites/cite.db", HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(continueOnCapturedContext: false);
						dbResp.EnsureSuccessStatusCode();
						string directoryName = Path.GetDirectoryName(citeUsbPath);
						if (!string.IsNullOrEmpty(directoryName))
						{
							Directory.CreateDirectory(directoryName);
						}
						string tmp = citeUsbPath + ".download";
						long totalBytes = dbResp.Content.Headers.ContentLength ?? (-1);
						long length;
						using (Stream src = await dbResp.Content.ReadAsStreamAsync().ConfigureAwait(continueOnCapturedContext: false))
						{
							using FileStream dst = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
							await src.CopyToAsync(dst).ConfigureAwait(continueOnCapturedContext: false);
							await dst.FlushAsync().ConfigureAwait(continueOnCapturedContext: false);
							length = dst.Length;
						}
						if (totalBytes > 0 && length != totalBytes)
						{
							try
							{
								File.Delete(tmp);
							}
							catch
							{
							}
							Log.Warning("cite.db: download truncated ({Got}/{Expected} bytes) — keeping current v{V}", length, totalBytes, ReadCiteVer(usbVerPath));
							return;
						}
						if (File.Exists(citeUsbPath))
						{
							File.Replace(tmp, citeUsbPath, null);
						}
						else
						{
							File.Move(tmp, citeUsbPath);
						}
						File.WriteAllText(usbVerPath, remoteVer.ToString());
						Log.Information("cite.db: refreshed from GitHub to v{V} ({Len} bytes) → {Path}", remoteVer, length, citeUsbPath);
					}
					catch (Exception ex2)
					{
						Log.Information("cite.db: GitHub refresh skipped ({Msg})", ex2.Message);
					}
				});
			}
			SeedAndRefreshSynonymsDb();
			RefreshPublisherShelvesDb();
			base.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, (Action)delegate
			{
				Task.Run(async delegate
				{
					try
					{
						PersonalCatalogIndexer.ScanResult scanResult = await Services.GetRequiredService<PersonalCatalogIndexer>().ScanAsync();
						Log.Information("Personal corpus background scan complete: seen={Seen} inserted={Ins} updated={Upd} removed={Rem} skipped={Skip}", scanResult.FilesSeen, scanResult.Inserted, scanResult.Updated, scanResult.Removed, scanResult.Skipped);
						if (scanResult.PruneSkipped)
						{
							Log.Warning("Personal corpus: prune SKIPPED — folder missing or zero files seen while catalog holds personal rows. Kept rows intact (likely an unavailable/relettered drive, not an emptied corpus).");
						}
					}
					catch (Exception exception14)
					{
						Log.Warning(exception14, "Personal corpus background scan failed");
					}
				});
			});
			base.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, (Action)delegate
			{
				try
				{
					BookshelfOptions bookshelfOptions5 = Services.GetRequiredService<JsonSettingsStore>().Load();
					if (bookshelfOptions5.Paths.ProvisionPending)
					{
						ProvisioningService requiredService4 = Services.GetRequiredService<ProvisioningService>();
						IPathResolver requiredService5 = Services.GetRequiredService<IPathResolver>();
						ProvisionPlan provisionPlan = requiredService4.ComputePendingPlan(requiredService5.DataDriveRoot, bookshelfOptions5.Paths.InstallType, bookshelfOptions5.Paths.BuildIndexLocally);
						if (provisionPlan.HasWork)
						{
							BackgroundProcessorService requiredService6 = Services.GetRequiredService<BackgroundProcessorService>();
							ISearchEngine engine = null;
							IndexSpec localIndexSpec = null;
							if (provisionPlan.BuildIndexLocally)
							{
								engine = Services.GetRequiredService<ISearchEngine>();
								localIndexSpec = new IndexSpec(requiredService5.IndexesRoot, new string[1] { requiredService5.PdfsRoot }, UseNativeEnumeration: true);
							}
							requiredService6.EnqueueAsync(new ProvisionDownloadJob(requiredService5.DataDriveRoot, provisionPlan, requiredService4, bookshelfOptions5.Paths.InstallType, engine, localIndexSpec));
							Log.Information("Provisioning continuation enqueued (index={Idx} books={Books} buildLocal={Bl})", provisionPlan.Index, provisionPlan.Books, provisionPlan.BuildIndexLocally);
						}
					}
				}
				catch (Exception exception14)
				{
					Log.Warning(exception14, "Provisioning continuation failed to enqueue");
				}
			});
			base.OnStartup(e);
			Services.GetRequiredService<HebrewBooks.UI.Services.ThemeService>().ApplyFromSettings();
			splash.SetStatus(SharedStrings.S510);
			MainWindow window = Services.GetRequiredService<MainWindow>();
			Log.Information("TIMING: startup MainWindow resolved +{Ms}ms", stopwatch.ElapsedMilliseconds);
			window.ContentRendered += delegate
			{
				try
				{
					splash.Close();
				}
				catch
				{
				}
				try
				{
					if (window.WindowState == WindowState.Minimized)
					{
						window.WindowState = WindowState.Normal;
					}
					window.Activate();
					window.Topmost = true;
					window.Topmost = false;
					window.Focus();
				}
				catch
				{
				}
			};
			window.Show();
			Log.Information("TIMING: startup window shown +{Ms}ms", stopwatch.ElapsedMilliseconds);
			base.MainWindow = window;
			base.ShutdownMode = ShutdownMode.OnMainWindowClose;
			if (!IsProtectMode && !IsNetworkInstall)
			{
				try
				{
					string processPath = Environment.ProcessPath;
					if (!string.IsNullOrEmpty(processPath))
					{
						FileAssociationService fileAssociationService = new FileAssociationService();
						if (!fileAssociationService.IsUrlProtocolRegisteredFor("hebrewbooks", processPath))
						{
							fileAssociationService.RegisterUrlProtocol("hebrewbooks", processPath, SharedStrings.S511);
						}
					}
				}
				catch (Exception exception10)
				{
					Log.Warning(exception10, "DeepLink: protocol registration failed");
				}
				string launchUri = DeepLink.FindUri(e.Args);
				if (launchUri != null)
				{
					base.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, (Action)delegate
					{
						TryHandleDeepLink(launchUri);
					});
				}
			}
			try
			{
				TabletModeService requiredService3 = Services.GetRequiredService<TabletModeService>();
				requiredService3.Attach(window);
				IsTabletMode = requiredService3.IsTabletMode;
				requiredService3.Changed += delegate(bool on)
				{
					IsTabletMode = on;
					try
					{
						App.TabletModeChanged?.Invoke(on);
					}
					catch (Exception exception14)
					{
						Log.Warning(exception14, "TabletModeChanged handler threw");
					}
				};
			}
			catch (Exception exception11)
			{
				Log.Warning(exception11, "TabletMode: detection setup failed (non-fatal)");
			}
			try
			{
				if (!Services.GetRequiredService<JsonSettingsStore>().Load().View.UnifiedSearchLayout)
				{
					base.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, (Action)delegate
					{
						try
						{
							Services.GetRequiredService<SearchPage>().EnsureViewerPrewarmed();
						}
						catch (Exception exception14)
						{
							Log.Warning(exception14, "SearchPage prewarm kickoff failed");
						}
					});
				}
			}
			catch (Exception exception12)
			{
				Log.Warning(exception12, "search-layout prewarm check failed");
			}
			base.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, (Action)delegate
			{
				Services.GetRequiredService<MainViewModel>().StartupUpdateCheckAsync();
			});
			if (!IsProtectMode)
			{
				base.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, (Action)delegate
				{
					Task.Run(async delegate
					{
						_ = 1;
						try
						{
							await Task.Delay(TimeSpan.FromSeconds(5.0)).ConfigureAwait(continueOnCapturedContext: false);
							int num = await Services.GetRequiredService<TextLayerUpdateService>().RunFullCycleAsync().ConfigureAwait(continueOnCapturedContext: false);
							if (num > 0)
							{
								Log.Information("TextLayerUpdate: applied {Applied} sidecars on startup", num);
							}
						}
						catch (Exception exception14)
						{
							Log.Warning(exception14, "TextLayerUpdate startup cycle failed");
						}
					});
				});
				base.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, (Action)delegate
				{
					Task.Run(async delegate
					{
						_ = 1;
						try
						{
							await Task.Delay(TimeSpan.FromSeconds(20.0)).ConfigureAwait(continueOnCapturedContext: false);
							await Services.GetRequiredService<TocHarvestService>().HarvestAsync().ConfigureAwait(continueOnCapturedContext: false);
						}
						catch (Exception exception14)
						{
							Log.Warning(exception14, "TocHarvest startup run failed");
						}
					});
				});
			}
			if (!IsProtectMode && !IsNetworkInstall)
			{
				base.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, (Action)delegate
				{
					try
					{
						JsonSettingsStore requiredService4 = Services.GetRequiredService<JsonSettingsStore>();
						BookshelfOptions bookshelfOptions5 = requiredService4.Load();
						string ver = CurrentVersionString();
						if (bookshelfOptions5.UsageTelemetryConsent != true && (bookshelfOptions5.UsageTelemetryConsent != false || !(bookshelfOptions5.UsageTelemetryConsentAskedVersion == ver)))
						{
							MessageBoxResult messageBoxResult = HebrewMessageBox.Show(SharedStrings.S512 + SharedStrings.S9051 + SharedStrings.S9052 + SharedStrings.S515 + SharedStrings.S516 + SharedStrings.S517, SharedStrings.S518, MessageBoxButton.YesNo, MessageBoxImage.Question);
							bool granted = messageBoxResult == MessageBoxResult.Yes;
							requiredService4.Update(delegate(BookshelfOptions o)
							{
								o.UsageTelemetryConsent = granted;
								o.UsageTelemetryConsentAskedVersion = ver;
							});
							Log.Information("UsageTelemetry consent set to {Granted} (v{Ver})", granted, ver);
							if (granted)
							{
								Task.Run(() => Services.GetRequiredService<PopularitySnapshot>().RefreshAsync());
							}
						}
					}
					catch (Exception exception14)
					{
						Log.Warning(exception14, "UsageTelemetry consent prompt failed");
					}
				});
			}
			if (IsProtectMode || IsNetworkInstall)
			{
				return;
			}
			base.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, (Action)delegate
			{
				Task.Run(async delegate
				{
					_ = 1;
					try
					{
						await Task.Delay(TimeSpan.FromSeconds(8.0)).ConfigureAwait(continueOnCapturedContext: false);
						await Services.GetRequiredService<UsageTelemetryService>().SendIfDueAsync().ConfigureAwait(continueOnCapturedContext: false);
					}
					catch (Exception exception14)
					{
						Log.Warning(exception14, "UsageTelemetry startup flush failed");
					}
				});
			});
			base.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, (Action)delegate
			{
				Task.Run(async delegate
				{
					_ = 1;
					try
					{
						await Task.Delay(TimeSpan.FromSeconds(10.0)).ConfigureAwait(continueOnCapturedContext: false);
						await Services.GetRequiredService<PopularitySnapshot>().RefreshAsync().ConfigureAwait(continueOnCapturedContext: false);
					}
					catch (Exception exception14)
					{
						Log.Warning(exception14, "Popularity refresh failed");
					}
				});
			});
		}
		catch (DataRootNotFoundException exception13)
		{
			try
			{
				splash.Close();
			}
			catch
			{
			}
			Log.Information(exception13, "Data root not found — user exited the setup wizard; shutting down");
			Shutdown();
		}
		catch (Exception ex)
		{
			splash.Close();
			Log.Fatal(ex, "Fatal error during startup");
			HebrewMessageBox.Show(ex.ToString(), SharedStrings.ErrorTitle, MessageBoxButton.OK, MessageBoxImage.Hand);
			Shutdown();
		}
		static int ReadCiteVer(string p)
		{
			try
			{
				int result;
				return (File.Exists(p) && int.TryParse(File.ReadAllText(p).Trim(), out result)) ? result : 0;
			}
			catch
			{
				return 0;
			}
		}
	}

	private static bool TryDetectCatalogCorruption(string dbPath, bool networkInstall, out Exception? corruption)
	{
		corruption = null;
		try
		{
			using SqliteConnection sqliteConnection = new SqliteConnectionFactory(dbPath, networkInstall).Open();
			using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
			sqliteCommand.CommandText = "SELECT COUNT(*) FROM Katalog";
			sqliteCommand.ExecuteScalar();
			return false;
		}
		catch (SqliteException ex) when (((Func<bool>)delegate
		{
			// Could not convert BlockContainer to single expression
			int sqliteErrorCode = ex.SqliteErrorCode;
			return ((sqliteErrorCode == 11 || sqliteErrorCode == 26) ? true : false) || ex.Message.Contains("malformed", StringComparison.OrdinalIgnoreCase);
		}).Invoke())
		{
			corruption = ex;
			return true;
		}
		catch (Exception exception)
		{
			Log.Warning(exception, "Catalog corruption probe: non-corruption error (ignored; normal flow handles it)");
			return false;
		}
	}

	private bool RecoverCorruptCatalogInteractive(ref SplashHost splash, string dataRoot, IPathResolver paths, BookshelfOptions opts, Stopwatch startupSw)
	{
		try
		{
			splash.Close();
		}
		catch
		{
		}
		if (HebrewMessageBox.Show(SharedStrings.S9053 + SharedStrings.S9054 + SharedStrings.S521, SharedStrings.S522, MessageBoxButton.YesNo, MessageBoxImage.Exclamation) != MessageBoxResult.Yes)
		{
			HebrewMessageBox.Show(SharedStrings.S9055 + SharedStrings.S524, SharedStrings.S525, MessageBoxButton.OK, MessageBoxImage.Asterisk);
			return false;
		}
		splash = new SplashHost();
		try
		{
			splash.Show();
		}
		catch
		{
		}
		splash.SetStatus(SharedStrings.S526);
		try
		{
			string catalogDbPath = paths.CatalogDbPath;
			string[] array = new string[3]
			{
				catalogDbPath,
				catalogDbPath + "-wal",
				catalogDbPath + "-shm"
			};
			foreach (string path in array)
			{
				try
				{
					if (File.Exists(path))
					{
						File.Delete(path);
					}
				}
				catch
				{
				}
			}
			ProvisioningService requiredService = Services.GetRequiredService<ProvisioningService>();
			SplashHost splashRef = splash;
			DirectProgress<(long, long, int, int)> progress = new DirectProgress<(long, long, int, int)>(delegate((long Bytes, long Total, int Files, int TotalFiles) t)
			{
				int value = (int)((t.Total > 0) ? (100 * t.Bytes / t.Total) : 0);
				try
				{
					splashRef.SetStatus($"{SharedStrings.S2000}{value}%");
				}
				catch
				{
				}
			});
			requiredService.ProvisionCatalogBlockingAsync(dataRoot, progress).GetAwaiter().GetResult();
		}
		catch (Exception ex)
		{
			Log.Error(ex, "Corrupt-catalog re-download failed");
			try
			{
				splash.Close();
			}
			catch
			{
			}
			HebrewMessageBox.Show(SharedStrings.S9056 + ex.Message + SharedStrings.S529, SharedStrings.ErrorTitle, MessageBoxButton.OK, MessageBoxImage.Hand);
			return false;
		}
		if (TryDetectCatalogCorruption(paths.CatalogDbPath, opts.NetworkInstall, out Exception _))
		{
			try
			{
				splash.Close();
			}
			catch
			{
			}
			HebrewMessageBox.Show(SharedStrings.S530, SharedStrings.ErrorTitle, MessageBoxButton.OK, MessageBoxImage.Hand);
			return false;
		}
		Log.Information("Corrupt catalog re-downloaded successfully +{Ms}ms", startupSw.ElapsedMilliseconds);
		return true;
	}

	protected override void OnExit(ExitEventArgs e)
	{
		Thread thread = new Thread((ThreadStart)delegate
		{
			Thread.Sleep(2000);
			try
			{
				Log.CloseAndFlush();
			}
			catch
			{
			}
			Environment.Exit(0);
		});
		thread.IsBackground = true;
		thread.Name = "ShutdownWatchdog";
		thread.Start();
		try
		{
			KillOurWebView2Processes();
		}
		catch (Exception exception)
		{
			Log.Warning(exception, "WebView2 process sweep on exit failed");
		}
		try
		{
			if (Services != null)
			{
				JsonSettingsStore obj = (JsonSettingsStore)Services.GetService(typeof(JsonSettingsStore));
				if (obj == null || obj.Load().View.PersistSessionState)
				{
					LibraryViewModel libraryViewModel = (LibraryViewModel)Services.GetService(typeof(LibraryViewModel));
					IBookLastPageRepository bookLastPageRepository = (IBookLastPageRepository)Services.GetService(typeof(IBookLastPageRepository));
					if (libraryViewModel != null && bookLastPageRepository != null && libraryViewModel.HasOpenBook && !libraryViewModel.IsTextMode && !string.IsNullOrEmpty(libraryViewModel.SelectedBook?.FileID) && libraryViewModel.CurrentPage > 0)
					{
						bookLastPageRepository.Save(libraryViewModel.SelectedBook.FileID, libraryViewModel.CurrentPage);
					}
				}
			}
		}
		catch (Exception exception2)
		{
			Log.Warning(exception2, "BookLastPage shutdown save failed");
		}
		try
		{
			((UsageTelemetryService)(Services?.GetService(typeof(UsageTelemetryService))))?.FinalizeCurrent();
		}
		catch (Exception exception3)
		{
			Log.Warning(exception3, "UsageTelemetry shutdown finalize failed");
		}
		try
		{
			_singleInstance?.Dispose();
		}
		catch (Exception exception4)
		{
			Log.Warning(exception4, "SingleInstance dispose failed");
		}
		try
		{
			DisposeAllWebView2Hosts();
		}
		catch (Exception exception5)
		{
			Log.Warning(exception5, "WebView2 dispose-on-exit failed");
		}
		try
		{
			if (_host is IAsyncDisposable asyncDisposable)
			{
				asyncDisposable.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2.0));
			}
			else
			{
				_host?.Dispose();
			}
		}
		catch
		{
		}
		Log.CloseAndFlush();
		base.OnExit(e);
		try
		{
			Environment.Exit(0);
		}
		catch
		{
		}
	}

	private static void KillStaleOrphanInstances()
	{
		int id = Process.GetCurrentProcess().Id;
		string processPath = Environment.ProcessPath;
		if (string.IsNullOrEmpty(processPath))
		{
			return;
		}
		DateTime now = DateTime.Now;
		Process[] processesByName = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(processPath));
		int num = 0;
		int num2 = 0;
		Process[] array = processesByName;
		foreach (Process process in array)
		{
			try
			{
				if (process.Id == id)
				{
					continue;
				}
				string text = null;
				try
				{
					text = process.MainModule?.FileName;
				}
				catch
				{
				}
				if (text == null || !string.Equals(text, processPath, StringComparison.OrdinalIgnoreCase) || process.MainWindowHandle != IntPtr.Zero)
				{
					continue;
				}
				TimeSpan timeSpan;
				try
				{
					timeSpan = now - process.StartTime;
				}
				catch
				{
					num2++;
					goto end_IL_0041;
				}
				if (timeSpan < OrphanSweepMinAge)
				{
					num2++;
					continue;
				}
				process.Kill(entireProcessTree: true);
				try
				{
					process.WaitForExit(3000);
				}
				catch
				{
				}
				Log.Information("startup: killed zombie HebrewBooks.exe pid={Pid} (age {Age:N0}s, no window)", process.Id, timeSpan.TotalSeconds);
				num++;
				end_IL_0041:;
			}
			catch
			{
			}
			finally
			{
				process.Dispose();
			}
		}
		if (num > 0 || num2 > 0)
		{
			Log.Information("startup: orphan sweep killed {Killed}, spared {Spared} (too young / age unreadable)", num, num2);
		}
	}

	private static bool IsBenignFrameworkRace(Exception? ex)
	{
		if (!(ex is ArgumentException))
		{
			return false;
		}
		string message = ex.Message;
		if (message == null || !message.Contains("Hwnd of zero", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		return ex.StackTrace?.Contains("Wpf.Ui.Controls.TitleBar", StringComparison.Ordinal) ?? false;
	}

	private static void TryReportMissingComponent(Exception? ex)
	{
		if (_componentErrorShown)
		{
			return;
		}
		string text = FindMissingModule(ex);
		string msg;
		if (text != null)
		{
			_componentErrorShown = true;
			msg = SharedStrings.S2001 + text + "\n\n" + SharedStrings.S532 + SharedStrings.S533 + SharedStrings.S534 + SharedStrings.S535 + SharedStrings.S536;
			Dispatcher dispatcher = Application.Current?.Dispatcher;
			if (dispatcher == null || dispatcher.CheckAccess())
			{
				Show();
			}
			else
			{
				dispatcher.BeginInvoke(new Action(Show));
			}
		}
		void Show()
		{
			try
			{
				HebrewMessageBox.Show(msg, SharedStrings.ErrorTitle, MessageBoxButton.OK, MessageBoxImage.Hand);
			}
			catch (Exception exception)
			{
				Log.Warning(exception, "Failed to show missing-component dialog");
			}
		}
	}

	private static string? FindMissingModule(Exception? ex)
	{
		Exception ex2 = ex;
		while (ex2 != null)
		{
			Exception ex3 = ex2;
			if (!(ex3 is DllNotFoundException))
			{
				if (!(ex3 is BadImageFormatException ex4))
				{
					if (ex3 is FileNotFoundException ex5)
					{
						string? fileName = ex5.FileName;
						if ((fileName != null && fileName.Contains(".dll", StringComparison.OrdinalIgnoreCase)) || ex5.Message.Contains("Could not load file or assembly", StringComparison.OrdinalIgnoreCase))
						{
							return ex5.FileName ?? ExtractModuleName(ex5.Message) ?? "(assembly)";
						}
					}
					ex2 = ex2.InnerException;
					continue;
				}
				return (ex4.FileName ?? ExtractModuleName(ex4.Message) ?? "(unknown)") + SharedStrings.S9057;
			}
			return ExtractModuleName(ex2.Message) ?? "(native DLL)";
		}
		return null;
	}

	private static string? ExtractModuleName(string message)
	{
		Match match = Regex.Match(message, "'([^']+?\\.dll)'", RegexOptions.IgnoreCase);
		if (!match.Success)
		{
			return null;
		}
		return match.Groups[1].Value;
	}

	private void HandleForwardedArgs(string payload)
	{
		base.Dispatcher.BeginInvoke((Action)delegate
		{
			try
			{
				Window window = (Services?.GetService(typeof(MainWindow)) as Window) ?? base.MainWindow;
				if (window != null)
				{
					if (window.WindowState == WindowState.Minimized)
					{
						window.WindowState = WindowState.Normal;
					}
					window.Activate();
					window.Topmost = true;
					window.Topmost = false;
					window.Focus();
					string text = DeepLink.FindUri(payload.Split('\n'));
					if (text != null)
					{
						TryHandleDeepLink(text);
					}
				}
			}
			catch (Exception exception)
			{
				Log.Warning(exception, "SingleInstance: handling forwarded launch failed");
			}
		});
	}

	internal async Task OpenShelfTargetAsync(string? fileId, int page)
	{
		if (!string.IsNullOrEmpty(fileId) && int.TryParse(fileId, out var result) && result > 0)
		{
			await OpenDeepLinkAsync(result, page);
			return;
		}
		try
		{
			ICatalogRepository catalogRepository = default(ICatalogRepository);
			IPathResolver paths = default(IPathResolver);
			int num;
			if (Services != null && !string.IsNullOrEmpty(fileId))
			{
				catalogRepository = Services.GetService(typeof(ICatalogRepository)) as ICatalogRepository;
				if (catalogRepository != null)
				{
					object service = Services.GetService(typeof(IPathResolver));
					paths = service as IPathResolver;
					num = ((paths != null) ? 1 : 0);
					goto IL_0118;
				}
			}
			num = 0;
			goto IL_0118;
			IL_0118:
			bool flag = (byte)num != 0;
			Book book = default(Book);
			if (flag)
			{
				book = await catalogRepository.GetByFileIdAsync(fileId);
				flag = (object)book != null;
			}
			if (flag)
			{
				if (!(Services.GetService(typeof(MainWindow)) is Window))
				{
					_ = base.MainWindow;
				}
				PdfViewerWindow pdfViewerWindow = (PdfViewerWindow)Services.GetService(typeof(PdfViewerWindow));
				pdfViewerWindow.Show();
				if (string.Equals(book.SourceType, "Text", StringComparison.Ordinal))
				{
					await pdfViewerWindow.OpenTextAsync(book);
					return;
				}
				if (string.Equals(book.SourceType, "Personal", StringComparison.Ordinal))
				{
					string text = ((!string.IsNullOrEmpty(book.RelativePath)) ? book.RelativePath : book.FileID);
					string text2 = (string.IsNullOrEmpty(text) ? null : paths.PersonalFilePath(text));
					if (text2 == null || !File.Exists(text2))
					{
						pdfViewerWindow.Close();
						HebrewMessageBox.Show(SharedStrings.S2002 + text2, "מדף", MessageBoxButton.OK, MessageBoxImage.Asterisk);
					}
					else
					{
						await pdfViewerWindow.OpenAsync(book.FileID ?? text, book.BookName ?? "", text2, null, null, page);
					}
					return;
				}
				pdfViewerWindow.Close();
			}
		}
		catch (Exception exception)
		{
			Log.Warning(exception, "Shelf: failed to open {FileId} p{Page}", fileId, page);
		}
		HebrewMessageBox.Show(SharedStrings.S540, SharedStrings.S539, MessageBoxButton.OK, MessageBoxImage.Asterisk);
	}

	private void TryHandleDeepLink(string uri)
	{
		string text = DeepLink.TryParseSearch(uri);
		int fileId;
		int page;
		if (text != null)
		{
			HandleSearchDeepLink(text);
		}
		else if (!DeepLink.TryParse(uri, out fileId, out page))
		{
			Log.Information("DeepLink: ignoring unrecognised URI {Uri}", uri);
		}
		else
		{
			OpenDeepLinkAsync(fileId, page);
		}
	}

	private void HandleSearchDeepLink(string query)
	{
		PendingExternalSearch = query;
		if (!(base.MainWindow is MainWindow mainWindow))
		{
			Log.Information("DeepLink: search queued (main window not ready) — {Query}", query);
			return;
		}
		if (mainWindow.WindowState == WindowState.Minimized)
		{
			mainWindow.WindowState = WindowState.Normal;
		}
		mainWindow.Activate();
		mainWindow.RunSearchDeepLink();
		Log.Information("DeepLink: search — {Query}", query);
	}

	private async Task OpenDeepLinkAsync(int fileId, int page)
	{
		_ = 2;
		try
		{
			if (Services == null)
			{
				return;
			}
			ICatalogRepository catalogRepository = Services.GetService(typeof(ICatalogRepository)) as ICatalogRepository;
			IPathResolver paths = Services.GetService(typeof(IPathResolver)) as IPathResolver;
			if (catalogRepository == null || paths == null)
			{
				return;
			}
			string idText = fileId.ToString(CultureInfo.InvariantCulture);
			Book book = await catalogRepository.GetByFileIdAsync(idText);
			if ((object)book == null || !string.Equals(book.SourceType, "PDF", StringComparison.Ordinal))
			{
				HebrewMessageBox.Show($"{SharedStrings.S2003}{fileId}{SharedStrings.S2004}", SharedStrings.S542, MessageBoxButton.OK, MessageBoxImage.Asterisk);
				return;
			}
			Window owner = (Services.GetService(typeof(MainWindow)) as Window) ?? base.MainWindow;
			string pdfPath = paths.PdfPath(fileId, book.Folder);
			if (!File.Exists(pdfPath))
			{
				OnDemandBookService onDemandBookService = Services.GetService(typeof(OnDemandBookService)) as OnDemandBookService;
				bool flag = onDemandBookService != null;
				if (flag)
				{
					flag = await onDemandBookService.EnsureLocalAsync(book, owner);
				}
				if (!flag || !File.Exists(pdfPath))
				{
					HebrewMessageBox.Show($"{SharedStrings.S2005}{fileId}{SharedStrings.S2006}", "פתיחת קישור", MessageBoxButton.OK, MessageBoxImage.Asterisk);
					return;
				}
			}
			PdfViewerWindow obj = (PdfViewerWindow)Services.GetService(typeof(PdfViewerWindow));
			obj.Show();
			await obj.OpenAsync(idText, book.BookName ?? "", pdfPath, null, null, page);
			Log.Information("DeepLink: opened book {FileId} at page {Page}", fileId, page);
		}
		catch (Exception exception)
		{
			Log.Warning(exception, "DeepLink: failed to open {FileId} p{Page}", fileId, page);
		}
	}

	private static void RegisterProtocolForVelopack()
	{
		try
		{
			string processPath = Environment.ProcessPath;
			if (!string.IsNullOrEmpty(processPath))
			{
				new FileAssociationService().RegisterUrlProtocol("hebrewbooks", processPath, SharedStrings.S511);
			}
		}
		catch
		{
		}
	}

	private static void OnVelopackUninstall()
	{
		try
		{
			new FileAssociationService().UnregisterUrlProtocol("hebrewbooks");
		}
		catch
		{
		}
		try
		{
			string deleteDataMarkerPath = UninstallCleanup.DeleteDataMarkerPath;
			if (File.Exists(deleteDataMarkerPath))
			{
				try
				{
					string text = File.ReadAllText(deleteDataMarkerPath).Trim();
					if (UninstallCleanup.IsLocalDeletableDataRoot(text))
					{
						Directory.Delete(text, recursive: true);
					}
				}
				catch
				{
				}
			}
			string appDataDir = UninstallCleanup.AppDataDir;
			if (Directory.Exists(appDataDir))
			{
				Directory.Delete(appDataDir, recursive: true);
			}
		}
		catch
		{
		}
	}

	private static void KillOurWebView2Processes()
	{
		string value;
		try
		{
			value = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HebrewBooks", "WebView2Data");
		}
		catch
		{
			return;
		}
		int num = 0;
		try
		{
			using ManagementObjectSearcher managementObjectSearcher = new ManagementObjectSearcher("SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name = 'msedgewebview2.exe'");
			foreach (ManagementBaseObject item in managementObjectSearcher.Get())
			{
				try
				{
					string text = item["CommandLine"] as string;
					if (!string.IsNullOrEmpty(text) && text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0)
					{
						using Process process = Process.GetProcessById(Convert.ToInt32(item["ProcessId"]));
						process.Kill(entireProcessTree: true);
						num++;
					}
				}
				catch
				{
				}
				finally
				{
					item.Dispose();
				}
			}
		}
		catch (Exception exception)
		{
			Log.Warning(exception, "WebView2 process sweep (WMI) failed");
			return;
		}
		if (num > 0)
		{
			Log.Information("WebView2 sweep: killed {N} orphaned msedgewebview2.exe tree(s)", num);
		}
	}

	private static void DisposeAllWebView2Hosts()
	{
		foreach (Window window in Application.Current.Windows)
		{
			try
			{
				DisposeWebView2In(window);
			}
			catch (Exception exception)
			{
				Log.Warning(exception, "WebView2 dispose: window walk failed");
			}
		}
	}

	public static void DisposeWebView2In(DependencyObject root)
	{
		if (root is WebView2 webView)
		{
			try
			{
				webView.Dispose();
				return;
			}
			catch
			{
				return;
			}
		}
		int childrenCount = VisualTreeHelper.GetChildrenCount(root);
		for (int i = 0; i < childrenCount; i++)
		{
			DisposeWebView2In(VisualTreeHelper.GetChild(root, i));
		}
	}

	private static string? PromptForFolder()
	{
		if (HebrewMessageBox.Show(SharedStrings.S544 + SharedStrings.S545 + SharedStrings.S546, SharedStrings.S547, MessageBoxButton.YesNo, MessageBoxImage.Exclamation) != MessageBoxResult.Yes)
		{
			return null;
		}
		OpenFolderDialog openFolderDialog = new OpenFolderDialog
		{
			Title = SharedStrings.S548
		};
		if (openFolderDialog.ShowDialog() != true)
		{
			return null;
		}
		return openFolderDialog.FolderName;
	}

	private static IHost BuildHost(string[] args)
	{
		return Host.CreateDefaultBuilder(args).UseSerilog().ConfigureServices(delegate(HostBuilderContext _, IServiceCollection services)
		{
			services.AddSingleton((Func<IServiceProvider, IProtectMode>)((IServiceProvider serviceProvider) => new ProtectMode(IsProtectMode, delegate
			{
				try
				{
					return new JsonSettingsStore().Load().UseOnlineService;
				}
				catch
				{
					return false;
				}
			})));
			services.AddSingleton<ISessionContext, SessionContext>();
			services.AddSingleton<ISearchScopeContext, SearchScopeContext>();
			services.AddSingleton<JsonSettingsStore>();
			services.AddSingleton<DataRootResolver>();
			services.AddSingleton<PathResolverHolder>();
			services.AddSingleton((Func<IServiceProvider, IPathResolver>)((IServiceProvider sp) => sp.GetRequiredService<PathResolverHolder>()));
			services.AddSingleton((Func<IServiceProvider, ISqliteConnectionFactory>)delegate(IServiceProvider sp)
			{
				SqliteConnectionFactory sqliteConnectionFactory = new SqliteConnectionFactory(sp.GetRequiredService<IPathResolver>().CatalogDbPath, IsNetworkInstall);
				sqliteConnectionFactory.EnsureSchema();
				return sqliteConnectionFactory;
			});
			services.AddSingleton<ICatalogRepository, CatalogRepository>();
			services.AddSingleton<IMadafRepository, UserShelfRepository>();
			services.AddSingleton<IShelfTreeRepository, ShelfTreeRepository>();
			services.AddSingleton<IBookLastPageRepository, BookLastPageRepository>();
			services.AddSingleton<IFavoritesRepository, FavoritesRepository>();
			services.AddSingleton((IServiceProvider serviceProvider) => new EngineOptions());
			services.AddSingleton<ISearchEngine, DtSearchNetEngine>();
			services.AddSingleton<ITelemetryConsent, TelemetryConsent>();
			services.AddSingleton<PopularitySnapshot>();
			services.AddSingleton<SearchOrchestrator>();
			services.AddSingleton<SearchHistoryStore>();
			services.AddSingleton<SearchResultsCacheStore>();
			services.AddSingleton((IServiceProvider sp) => new BookDeletionService(sp.GetRequiredService<ICatalogRepository>(), sp.GetRequiredService<IPathResolver>(), sp.GetRequiredService<ISearchEngine>(), !IsNetworkInstall));
			services.AddSingleton(delegate(IServiceProvider sp)
			{
				try
				{
					RasheyTevotMap rasheyTevotMap = RasheyTevotMap.LoadFromFile(sp.GetRequiredService<IPathResolver>().RasheyTevotPath);
					return (rasheyTevotMap == RasheyTevotMap.Empty) ? new RasheyTevotMap(new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)) : rasheyTevotMap;
				}
				catch (Exception exception)
				{
					Log.Warning(exception, "Failed to load RasheyTevot map");
					return new RasheyTevotMap(new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal));
				}
			});
			services.AddSingleton((IServiceProvider sp) => new HebAramMap(delegate
			{
				IPathResolver requiredService = sp.GetRequiredService<IPathResolver>();
				string text = HebAramMap.ReadFileText(requiredService.HebAramPath);
				if (text != null)
				{
					Log.Information("HebAram: loaded USB file {Path}", requiredService.HebAramPath);
					return text;
				}
				string text2 = Path.Combine(AppContext.BaseDirectory, "HebAram.DB");
				string text3 = HebAramMap.ReadFileText(text2);
				Log.Information("HebAram: {Result} bundled file {Path}", (text3 == null) ? "missing" : "loaded", text2);
				return text3;
			}));
			services.AddSingleton((IServiceProvider sp) => new SynonymLookup(SynonymsDbPath, sp.GetService<ILogger<SynonymLookup>>()));
			services.AddSingleton<CatalogService>();
			services.AddSingleton<IWorkAreaService, WorkAreaService>();
			services.AddSingleton<BackgroundProcessorService>();
			services.AddSingleton<R2MirrorClient>();
			services.AddSingleton<IPdfLinearizer, QpdfLinearizer>();
			services.AddSingleton<BookDownloadService>();
			services.AddSingleton<DiskSpaceService>();
			services.AddSingleton<ProvisioningService>();
			services.AddSingleton((IServiceProvider serviceProvider) => WinOcrCommand.Resolve());
			services.AddSingleton<TextlayerStatusStore>();
			services.AddSingleton<TextLayerService>();
			services.AddSingleton<WinOcrPageRenderer>();
			services.AddTransient<RepairBookOcrViewModel>();
			services.AddTransient<RepairBookOcrWindow>();
			services.AddHttpClient<TextLayerUpdateService>(delegate(HttpClient c)
			{
				c.Timeout = TimeSpan.FromSeconds(60.0);
			});
			services.AddSingleton<OtzrayaCatalogIndexer>();
			services.AddSingleton<PersonalCatalogIndexer>();
			services.AddSingleton<TocBundleService>();
			services.AddHttpClient<OtzrayaSyncService>(delegate(HttpClient c)
			{
				c.Timeout = TimeSpan.FromSeconds(60.0);
			});
			services.AddHttpClient<PublishedSyncService>(delegate(HttpClient c)
			{
				c.Timeout = TimeSpan.FromMinutes(5.0);
			});
			services.AddSingleton<HebrewDateService>();
			services.AddSingleton<FileAssociationService>();
			services.AddSingleton<HebrewBooks.UI.Services.ThemeService>();
			services.AddHttpClient<UpdateService>();
			services.AddSingleton<AppUpdateService>();
			services.AddSingleton<OcrEngineInstaller>();
			services.AddSingleton<OnDemandBookService>();
			services.AddSingleton<TabletModeService>();
			services.AddSingleton<TextLayerContributor>();
			services.AddSingleton<TocContributor>();
			services.AddSingleton<TocHarvestService>();
			services.AddSingleton<UsageTelemetryService>();
			services.AddSingleton<PerformanceAdvisor>();
			services.AddSingleton<MainViewModel>();
			services.AddTransient<LibraryViewModel>();
			services.AddTransient<SearchViewModel>();
			services.AddTransient<SettingsViewModel>();
			services.AddTransient<HelpViewModel>();
			services.AddTransient<AddBookViewModel>();
			services.AddTransient<EditBookViewModel>();
			services.AddTransient<PdfViewerViewModel>();
			services.AddTransient<MadafManagerViewModel>();
			services.AddTransient<DownloaderViewModel>();
			services.AddTransient<TocEditorViewModel>();
			services.AddTransient<PersonalCorpusViewModel>();
			services.AddTransient<DeleteBookViewModel>();
			services.AddSingleton<IPageService, HBPageService>();
			services.AddSingleton<LibraryPage>();
			services.AddSingleton<SearchPage>();
			services.AddSingleton<SettingsPage>();
			services.AddSingleton<HelpPage>();
			services.AddSingleton<MadafManagerPage>();
			services.AddSingleton<DownloaderPage>();
			services.AddSingleton<DiagnosticsPage>();
			services.AddSingleton<MainWindow>();
			services.AddTransient<AddBookWindow>();
			services.AddTransient<EditBookWindow>();
			services.AddTransient<PdfViewerWindow>();
			services.AddTransient<TocEditorWindow>();
			services.AddTransient<PersonalCorpusWindow>();
			services.AddTransient<UploadToServerWindow>();
			services.AddTransient<DeleteBookWindow>();
			services.AddTransient<SetupWizardViewModel>();
			services.AddTransient<SetupWizardWindow>();
			services.AddSingleton<DonationsClient>();
			services.AddSingleton<DonateStripViewModel>();
			services.AddTransient<DedicationWindow>();
		})
			.Build();
	}

	private static void EnsureWebView2Runtime()
	{
		if (IsWebView2Installed())
		{
			ClearInstallFlag();
			return;
		}
		string text = Path.Combine(AppContext.BaseDirectory, "WebView2RuntimeInstaller.exe");
		if (!File.Exists(text) || File.Exists(InstallFlagPath) || HebrewMessageBox.Show(SharedStrings.S549 + SharedStrings.S550 + SharedStrings.S551, SharedStrings.S552, MessageBoxButton.OKCancel, MessageBoxImage.Asterisk) != MessageBoxResult.OK)
		{
			return;
		}
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(InstallFlagPath));
			File.WriteAllText(InstallFlagPath, DateTime.Now.ToString("o"));
			Process.Start(new ProcessStartInfo
			{
				FileName = text,
				Arguments = "/silent /install",
				UseShellExecute = false,
				CreateNoWindow = true
			})?.WaitForExit();
			string fileName = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "HebrewBooks.exe");
			Process.Start(new ProcessStartInfo
			{
				FileName = fileName,
				UseShellExecute = true
			});
			Environment.Exit(0);
		}
		catch (Exception ex)
		{
			try
			{
				HebrewMessageBox.Show(SharedStrings.S2007 + ex.Message, SharedStrings.S554, MessageBoxButton.OK, MessageBoxImage.Hand);
			}
			catch
			{
			}
		}
	}

	private static bool IsWebView2Installed()
	{
		try
		{
			return !string.IsNullOrEmpty(CoreWebView2Environment.GetAvailableBrowserVersionString());
		}
		catch
		{
			return false;
		}
	}

	private static void ClearInstallFlag()
	{
		try
		{
			if (File.Exists(InstallFlagPath))
			{
				File.Delete(InstallFlagPath);
			}
		}
		catch
		{
		}
	}

	private void OnHitPagePillClicked(object sender, MouseButtonEventArgs e)
	{
		if (sender is ListBoxItem { DataContext: var dataContext } listBoxItem && dataContext is int parameter)
		{
			ItemsControl itemsControl = ItemsControl.ItemsControlFromItemContainer(listBoxItem);
			if (itemsControl is ListBox && itemsControl.DataContext is IHitStripNavigator hitStripNavigator)
			{
				hitStripNavigator.GoToHitPageCommand.Execute(parameter);
			}
		}
	}

	private void OnHitPagePillBringIntoView(object sender, RequestBringIntoViewEventArgs e)
	{
		if (sender is ListBoxItem container && ItemsControl.ItemsControlFromItemContainer(container) is ListBox lb && ListBoxScrollIntoView.IsPressing(lb))
		{
			e.Handled = true;
		}
	}

	private void OnHitPageComboItemClicked(object sender, MouseButtonEventArgs e)
	{
		if (sender is ComboBoxItem { DataContext: var dataContext } comboBoxItem && dataContext is int parameter)
		{
			ItemsControl itemsControl = ItemsControl.ItemsControlFromItemContainer(comboBoxItem);
			if (itemsControl is ComboBox && itemsControl.DataContext is IHitStripNavigator hitStripNavigator)
			{
				hitStripNavigator.GoToHitPageCommand.Execute(parameter);
			}
		}
	}



}
