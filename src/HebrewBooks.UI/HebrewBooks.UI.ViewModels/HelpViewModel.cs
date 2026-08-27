using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;
using CommunityToolkit.Mvvm.Input;
using HebrewBooks.Core;
using HebrewBooks.Core.Abstractions;
using HebrewBooks.Infrastructure.Paths;
using HebrewBooks.Infrastructure.Settings;
using HebrewBooks.Services.Downloader;
using HebrewBooks.Services.Provisioning;
using HebrewBooks.UI.Dialogs;
using HebrewBooks.UI.Resources;
using Serilog;

namespace HebrewBooks.UI.ViewModels;

public partial class HelpViewModel : ObservableObject
{
	[ObservableProperty]
	private string _placeholder = SharedStrings.HelpPlaceholder;

	private readonly IPathResolver _paths;

	private readonly JsonSettingsStore _settings;

	private const string SupportEmail = "HebrewBooks2026@gmail.com";




	public HelpViewModel(IPathResolver paths, JsonSettingsStore settings)
	{
		_paths = paths;
		_settings = settings;
	}

	[RelayCommand]
	private void SendDiagnosticsReport()
	{
		if (App.IsProtectMode)
		{
			return;
		}
		string text = null;
		try
		{
			string text2 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HebrewBooks", "logs");
			Assembly executingAssembly = Assembly.GetExecutingAssembly();
			string text3 = executingAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? executingAssembly.GetName().Version?.ToString() ?? "?";
			int num = text3.IndexOf('+');
			if (num > 0)
			{
				text3 = text3.Substring(0, num);
			}
			StringBuilder stringBuilder = new StringBuilder();
			stringBuilder.AppendLine("HebrewBooks diagnostics report");
			stringBuilder.AppendLine("==============================");
			StringBuilder stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder3 = stringBuilder2;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(17, 1, stringBuilder2);
			handler.AppendLiteral("Generated (UTC): ");
			handler.AppendFormatted(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
			stringBuilder3.AppendLine(ref handler);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder4 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(17, 1, stringBuilder2);
			handler.AppendLiteral("App version    : ");
			handler.AppendFormatted(text3);
			stringBuilder4.AppendLine(ref handler);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder5 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(20, 2, stringBuilder2);
			handler.AppendLiteral("OS             : ");
			handler.AppendFormatted(Environment.OSVersion);
			handler.AppendLiteral(" (");
			handler.AppendFormatted(Environment.Is64BitProcess ? "x64 proc" : "x86 proc");
			handler.AppendLiteral(")");
			stringBuilder5.AppendLine(ref handler);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder6 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(17, 1, stringBuilder2);
			handler.AppendLiteral("Machine        : ");
			handler.AppendFormatted(Environment.MachineName);
			stringBuilder6.AppendLine(ref handler);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder7 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(17, 1, stringBuilder2);
			handler.AppendLiteral("Data drive root: ");
			handler.AppendFormatted(_paths.DataDriveRoot);
			stringBuilder7.AppendLine(ref handler);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder8 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(20, 2, stringBuilder2);
			handler.AppendLiteral("Catalog DB     : ");
			handler.AppendFormatted(_paths.CatalogDbPath);
			handler.AppendLiteral(" (");
			handler.AppendFormatted(File.Exists(_paths.CatalogDbPath) ? "exists" : "MISSING");
			handler.AppendLiteral(")");
			stringBuilder8.AppendLine(ref handler);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder9 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(21, 2, stringBuilder2);
			handler.AppendLiteral("PDF index      : ");
			handler.AppendFormatted(_paths.IndexesRoot);
			handler.AppendLiteral(" -> ");
			handler.AppendFormatted(IxInfo(_paths.IndexesRoot));
			stringBuilder9.AppendLine(ref handler);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder10 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(21, 2, stringBuilder2);
			handler.AppendLiteral("Otzraya index  : ");
			handler.AppendFormatted(_paths.OtzrayaIndexPath);
			handler.AppendLiteral(" -> ");
			handler.AppendFormatted(IxInfo(_paths.OtzrayaIndexPath));
			stringBuilder10.AppendLine(ref handler);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder11 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(21, 2, stringBuilder2);
			handler.AppendLiteral("Personal index : ");
			handler.AppendFormatted(_paths.PersonalIndexPath);
			handler.AppendLiteral(" -> ");
			handler.AppendFormatted(IxInfo(_paths.PersonalIndexPath));
			stringBuilder11.AppendLine(ref handler);
			try
			{
				BookshelfOptions bookshelfOptions = _settings.Load();
				stringBuilder2 = stringBuilder;
				StringBuilder stringBuilder12 = stringBuilder2;
				handler = new StringBuilder.AppendInterpolatedStringHandler(17, 1, stringBuilder2);
				handler.AppendLiteral("Protect mode   : ");
				handler.AppendFormatted(App.IsProtectMode ? "ON (kiosk)" : "off");
				stringBuilder12.AppendLine(ref handler);
				stringBuilder2 = stringBuilder;
				StringBuilder stringBuilder13 = stringBuilder2;
				handler = new StringBuilder.AppendInterpolatedStringHandler(20, 2, stringBuilder2);
				handler.AppendLiteral("Protect marker : ");
				handler.AppendFormatted(ProtectMode.MarkerPresent() ? "present" : "absent");
				handler.AppendLiteral(" ");
				handler.AppendLiteral("(");
				handler.AppendFormatted(string.Join(" | ", ProtectMode.MarkerPaths()));
				handler.AppendLiteral(")");
				stringBuilder13.AppendLine(ref handler);
				stringBuilder2 = stringBuilder;
				StringBuilder stringBuilder14 = stringBuilder2;
				handler = new StringBuilder.AppendInterpolatedStringHandler(17, 1, stringBuilder2);
				handler.AppendLiteral("Online service : ");
				handler.AppendFormatted(bookshelfOptions.UseOnlineService ? bookshelfOptions.EffectiveOnlineServiceUrl() : "off");
				stringBuilder14.AppendLine(ref handler);
				stringBuilder2 = stringBuilder;
				StringBuilder stringBuilder15 = stringBuilder2;
				handler = new StringBuilder.AppendInterpolatedStringHandler(17, 1, stringBuilder2);
				handler.AppendLiteral("NetworkInstall : ");
				handler.AppendFormatted(bookshelfOptions.NetworkInstall);
				stringBuilder15.AppendLine(ref handler);
				stringBuilder2 = stringBuilder;
				StringBuilder stringBuilder16 = stringBuilder2;
				handler = new StringBuilder.AppendInterpolatedStringHandler(17, 1, stringBuilder2);
				handler.AppendLiteral("Network base   : ");
				handler.AppendFormatted(bookshelfOptions.Paths.NetworkBasePath ?? "(none)");
				stringBuilder16.AppendLine(ref handler);
				stringBuilder2 = stringBuilder;
				StringBuilder stringBuilder17 = stringBuilder2;
				handler = new StringBuilder.AppendInterpolatedStringHandler(17, 1, stringBuilder2);
				handler.AppendLiteral("Search service : ");
				handler.AppendFormatted(bookshelfOptions.EffectiveSearchServiceUrl() ?? "(local search)");
				stringBuilder17.AppendLine(ref handler);
				stringBuilder2 = stringBuilder;
				StringBuilder stringBuilder18 = stringBuilder2;
				handler = new StringBuilder.AppendInterpolatedStringHandler(19, 1, stringBuilder2);
				handler.AppendLiteral("Books (effective): ");
				handler.AppendFormatted(_paths.PdfsRoot);
				stringBuilder18.AppendLine(ref handler);
				stringBuilder.AppendLine(FastIndexLine(bookshelfOptions.Paths.FastIndexesDir));
				string text4 = bookshelfOptions.EffectiveCatalogMaster();
				stringBuilder.AppendLine(string.IsNullOrWhiteSpace(text4) ? "Catalog master : (none configured)" : $"Catalog master : {text4} ({FileLine(text4)})");
				stringBuilder2 = stringBuilder;
				StringBuilder stringBuilder19 = stringBuilder2;
				handler = new StringBuilder.AppendInterpolatedStringHandler(17, 1, stringBuilder2);
				handler.AppendLiteral("Local catalog  : ");
				handler.AppendFormatted(FileLine(_paths.CatalogDbPath));
				stringBuilder19.AppendLine(ref handler);
				stringBuilder2 = stringBuilder;
				StringBuilder stringBuilder20 = stringBuilder2;
				handler = new StringBuilder.AppendInterpolatedStringHandler(57, 3, stringBuilder2);
				handler.AppendLiteral("InstallType    : ");
				handler.AppendFormatted(bookshelfOptions.Paths.InstallType);
				handler.AppendLiteral(" (buildIndexLocally=");
				handler.AppendFormatted(bookshelfOptions.Paths.BuildIndexLocally);
				handler.AppendLiteral(", provisionPending=");
				handler.AppendFormatted(bookshelfOptions.Paths.ProvisionPending);
				handler.AppendLiteral(")");
				stringBuilder20.AppendLine(ref handler);
				try
				{
					ProvisioningService.LibraryDownloadStatus libraryDownloadStatus = new ProvisioningService(new R2MirrorClient()).DescribeStatus(_paths.DataDriveRoot, bookshelfOptions.Paths.InstallType, bookshelfOptions.Paths.BuildIndexLocally);
					stringBuilder.AppendLine(libraryDownloadStatus.IsComplete ? $"Library download: complete (books on disk {libraryDownloadStatus.BooksOnDisk:N0}, index {(libraryDownloadStatus.IndexPresent ? "present" : "MISSING")})" : $"Library download: INCOMPLETE -> index={libraryDownloadStatus.Pending.Index} books={libraryDownloadStatus.Pending.Books} buildLocal={libraryDownloadStatus.Pending.BuildIndexLocally} (books on disk {libraryDownloadStatus.BooksOnDisk:N0}, marker={libraryDownloadStatus.Marked})");
				}
				catch (Exception ex)
				{
					stringBuilder2 = stringBuilder;
					StringBuilder stringBuilder21 = stringBuilder2;
					handler = new StringBuilder.AppendInterpolatedStringHandler(25, 1, stringBuilder2);
					handler.AppendLiteral("Library download: error: ");
					handler.AppendFormatted(ex.Message);
					stringBuilder21.AppendLine(ref handler);
				}
				stringBuilder2 = stringBuilder;
				StringBuilder stringBuilder22 = stringBuilder2;
				handler = new StringBuilder.AppendInterpolatedStringHandler(21, 2, stringBuilder2);
				handler.AppendLiteral("Otzraya files  : ");
				handler.AppendFormatted(_paths.OtzrayaRoot);
				handler.AppendLiteral(" -> ");
				handler.AppendFormatted(DirInfo(_paths.OtzrayaRoot, "*.txt"));
				stringBuilder22.AppendLine(ref handler);
				stringBuilder2 = stringBuilder;
				StringBuilder stringBuilder23 = stringBuilder2;
				handler = new StringBuilder.AppendInterpolatedStringHandler(21, 2, stringBuilder2);
				handler.AppendLiteral("Personal files : ");
				handler.AppendFormatted(_paths.PersonalRoot);
				handler.AppendLiteral(" -> ");
				handler.AppendFormatted(DirInfo(_paths.PersonalRoot, "*"));
				stringBuilder23.AppendLine(ref handler);
			}
			catch (Exception ex2)
			{
				stringBuilder2 = stringBuilder;
				StringBuilder stringBuilder24 = stringBuilder2;
				handler = new StringBuilder.AppendInterpolatedStringHandler(29, 1, stringBuilder2);
				handler.AppendLiteral("Network-install info: error: ");
				handler.AppendFormatted(ex2.Message);
				stringBuilder24.AppendLine(ref handler);
			}
			text = Path.Combine(Path.GetTempPath(), "HebrewBooks-Diagnostics-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".zip");
			using (ZipArchive zipArchive = ZipFile.Open(text, ZipArchiveMode.Create))
			{
				using (StreamWriter streamWriter = new StreamWriter(zipArchive.CreateEntry("report.txt").Open()))
				{
					streamWriter.Write(stringBuilder.ToString());
				}
				if (Directory.Exists(text2))
				{
					List<string> list = new List<string>();
					list.AddRange(Directory.EnumerateFiles(text2, "bookshelf-*.log").OrderByDescending(File.GetLastWriteTimeUtc).Take(3));
					string[] array = new string[2] { "dtsearch-debug.xml", "dtsearch-highlights.xml" };
					foreach (string path in array)
					{
						string text5 = Path.Combine(text2, path);
						if (File.Exists(text5))
						{
							list.Add(text5);
						}
					}
					foreach (string item3 in list)
					{
						try
						{
							using Stream destination = zipArchive.CreateEntry(Path.GetFileName(item3)).Open();
							using FileStream fileStream = new FileStream(item3, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
							fileStream.CopyTo(destination);
						}
						catch
						{
						}
					}
				}
			}
			string text6 = text3;
			string stringToEscape = SharedStrings.S2138 + text6;
			string stringToEscape2 = SharedStrings.S731 + "-----\n" + SharedStrings.S732 + text + "\n" + SharedStrings.S9070 + "HebrewBooks2026@gmail.com)";
			string fileName = $"mailto:{"HebrewBooks2026@gmail.com"}?subject={Uri.EscapeDataString(stringToEscape)}&body={Uri.EscapeDataString(stringToEscape2)}";
			try
			{
				Process.Start("explorer.exe", "/select,\"" + text + "\"");
			}
			catch
			{
			}
			try
			{
				Process.Start(new ProcessStartInfo(fileName)
				{
					UseShellExecute = true
				});
			}
			catch
			{
			}
			Log.Information("Diagnostics report created: {Zip}", text);
			HebrewMessageBox.Show(SharedStrings.S734 + SharedStrings.S9071 + "HebrewBooks2026@gmail.com" + SharedStrings.S736 + SharedStrings.S737 + text, SharedStrings.S738, MessageBoxButton.OK, MessageBoxImage.Asterisk);
		}
		catch (Exception ex3)
		{
			Log.Error(ex3, "Diagnostics report failed");
			HebrewMessageBox.Show(SharedStrings.S9072 + ex3.Message + ((text == null) ? "" : (SharedStrings.S740 + text)), SharedStrings.S738, MessageBoxButton.OK, MessageBoxImage.Hand);
		}
		static string DirInfo(string dir, string pattern)
		{
			try
			{
				if (Directory.Exists(dir))
				{
					return $"exists, {Directory.EnumerateFiles(dir, pattern, SearchOption.AllDirectories).Take(1000).Count()}+ file(s)";
				}
				return "MISSING";
			}
			catch (Exception ex4)
			{
				return "error: " + ex4.Message;
			}
		}
		static string FastIndexLine(string? raw)
		{
			if (string.IsNullOrWhiteSpace(raw))
			{
				return "Fast index dir : (not configured)";
			}
			(PathResolver.FastIndexStatus Status, string? Path) tuple = PathResolver.InspectFastIndexDir(raw);
			PathResolver.FastIndexStatus item = tuple.Status;
			string item2 = tuple.Path;
			string text7 = $"Fast index dir : [{raw}] -> {item}";
			if (item == PathResolver.FastIndexStatus.Usable && item2 != raw)
			{
				text7 = text7 + " as [" + item2 + "]";
			}
			if (item != PathResolver.FastIndexStatus.Usable || item2 != raw)
			{
				text7 = text7 + "  codepoints: " + string.Join(" ", raw.Select(delegate(char c)
				{
					int num2 = c;
					return num2.ToString("X2");
				}));
			}
			return text7;
		}
		static string FileLine(string text7)
		{
			try
			{
				if (File.Exists(text7))
				{
					FileInfo fileInfo = new FileInfo(text7);
					return $"{(double)fileInfo.Length / 1024.0 / 1024.0:F2} MB, modified {fileInfo.LastWriteTimeUtc:yyyy-MM-dd HH:mm:ss}Z";
				}
				return "MISSING";
			}
			catch (Exception ex4)
			{
				return "error: " + ex4.Message;
			}
		}
		static string IxInfo(string dir)
		{
			try
			{
				if (Directory.Exists(dir))
				{
					return $"exists, {Directory.EnumerateFiles(dir, "*.ix").Count()} .ix segment(s)";
				}
				return "MISSING";
			}
			catch (Exception ex4)
			{
				return "error: " + ex4.Message;
			}
		}
	}
}
