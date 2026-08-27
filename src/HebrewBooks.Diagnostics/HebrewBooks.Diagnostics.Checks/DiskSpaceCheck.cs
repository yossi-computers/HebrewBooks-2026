using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using HebrewBooks.Core.Resources;

namespace HebrewBooks.Diagnostics.Checks;

public sealed class DiskSpaceCheck : IDiagnosticCheck
{
	private const double DataWarnGb = 2.0;

	private const double AppDataWarnGb = 1.0;

	public string Id => "disk";

	public string Category => CoreStrings.C102;

	public Task<IReadOnlyList<DiagnosticResult>> RunAsync(DiagnosticContext ctx, CancellationToken ct)
	{
		List<DiagnosticResult> list = new List<DiagnosticResult>();
		string directoryName = Path.GetDirectoryName(ctx.Settings.SettingsPath);
		AddVolume(list, "disk.appdata", CoreStrings.C103, directoryName, 1.0);
		if (ctx.Paths != null)
		{
			AddVolume(list, "disk.data", CoreStrings.C104, ctx.Paths.DataDriveRoot, 2.0);
		}
		return Task.FromResult((IReadOnlyList<DiagnosticResult>)list);
	}

	private void AddVolume(List<DiagnosticResult> results, string id, string label, string? path, double warnGb)
	{
		if (string.IsNullOrEmpty(path))
		{
			return;
		}
		try
		{
			DriveInfo driveInfo = new DriveInfo(Path.GetPathRoot(path) ?? path);
			double num = (double)driveInfo.AvailableFreeSpace / 1024.0 / 1024.0 / 1024.0;
			bool flag = num < warnGb;
			object obj;
			DefaultInterpolatedStringHandler handler;
			if (!flag)
			{
				obj = "";
			}
			else
			{
				handler = new DefaultInterpolatedStringHandler(4, 2);
				handler.AppendFormatted(CoreStrings.C98);
				handler.AppendFormatted(warnGb, "F0");
				handler.AppendLiteral(" GB)");
				obj = handler.ToStringAndClear();
			}
			string value = (string)obj;
			DiagnosticResult obj2 = new DiagnosticResult
			{
				Id = id,
				Category = Category,
				Title = (flag ? (CoreStrings.C99 + label) : (CoreStrings.C100 + label)),
				Severity = (flag ? DiagnosticSeverity.Warning : DiagnosticSeverity.Ok)
			};
			IFormatProvider invariantCulture = CultureInfo.InvariantCulture;
			handler = new DefaultInterpolatedStringHandler(3, 4, invariantCulture);
			handler.AppendFormatted(driveInfo.Name);
			handler.AppendFormatted(CoreStrings.C101);
			handler.AppendFormatted(num, "F1");
			handler.AppendLiteral(" GB");
			handler.AppendFormatted(value);
			obj2.Detail = string.Create(invariantCulture, ref handler);
			results.Add(obj2);
		}
		catch
		{
		}
	}
}
