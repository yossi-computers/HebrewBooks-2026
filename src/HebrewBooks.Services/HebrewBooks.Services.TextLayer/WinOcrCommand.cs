using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HebrewBooks.Services.TextLayer;

public sealed record WinOcrCommand(IReadOnlyList<string> Argv)
{
	public string Executable
	{
		get
		{
			if (Argv.Count != 0)
			{
				return Argv[0];
			}
			throw new InvalidOperationException("WinOcrCommand: Argv is empty — set the executable");
		}
	}

	public IEnumerable<string> BaseArgs => Argv.Skip(1);

	public static string InstallRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HebrewBooks", "OCR");

	public static string InstalledLauncherPath => Path.Combine(InstallRoot, "winocr.cmd");

	public static bool IsEngineInstalled => File.Exists(InstalledLauncherPath);

	public static WinOcrCommand Installed()
	{
		return new WinOcrCommand(new string[1] { InstalledLauncherPath });
	}

	public static WinOcrCommand DevDefault()
	{
		return new WinOcrCommand(new string[2] { "python", "C:\\Users\\Moshe\\Documents\\Project\\Win-OCR\\src\\ocr_backend\\pipeline.py" });
	}

	public static WinOcrCommand Resolve()
	{
		if (!IsEngineInstalled)
		{
			return DevDefault();
		}
		return Installed();
	}
}
