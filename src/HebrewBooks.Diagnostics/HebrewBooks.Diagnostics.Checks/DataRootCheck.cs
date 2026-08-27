using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using HebrewBooks.Core.Resources;
using HebrewBooks.Infrastructure.Settings;

namespace HebrewBooks.Diagnostics.Checks;

public sealed class DataRootCheck : IDiagnosticCheck
{
	public string Id => "dataroot";

	public string Category => CoreStrings.C87;

	public Task<IReadOnlyList<DiagnosticResult>> RunAsync(DiagnosticContext ctx, CancellationToken ct)
	{
		if (ctx.Paths == null)
		{
			return Task.FromResult((IReadOnlyList<DiagnosticResult>)new DiagnosticResult[1]
			{
				new DiagnosticResult
				{
					Id = "dataroot.missing",
					Category = Category,
					Title = CoreStrings.C88,
					Severity = DiagnosticSeverity.Error,
					Detail = CoreStrings.C89 + CoreStrings.C90,
					Evidence = ctx.DataRootError,
					Fix = new DiagnosticFix
					{
						Id = "dataroot.rescan",
						Label = CoreStrings.C91,
						Kind = FixKind.Safe,
						Run = delegate
						{
							ctx.Settings.Update(delegate(BookshelfOptions o)
							{
								o.Paths.ForceRescan = true;
							});
							return Task.FromResult(FixOutcome.Ok(CoreStrings.C92));
						}
					}
				}
			});
		}
		string dataDriveRoot = ctx.Paths.DataDriveRoot;
		string text = DescribeDrive(dataDriveRoot);
		DiagnosticResult[] array = new DiagnosticResult[1];
		DiagnosticResult diagnosticResult = new DiagnosticResult
		{
			Id = "dataroot.ok",
			Category = Category,
			Title = CoreStrings.C93,
			Severity = DiagnosticSeverity.Ok,
			Detail = CoreStrings.C81 + dataDriveRoot
		};
		string[] obj = new string[4]
		{
			CoreStrings.C82 + dataDriveRoot,
			text,
			null,
			null
		};
		string text2;
		if (ctx.Options.Paths.DataVolumeSerial == 0)
		{
			text2 = CoreStrings.C94;
		}
		else
		{
			IFormatProvider invariantCulture = CultureInfo.InvariantCulture;
			DefaultInterpolatedStringHandler handler = new DefaultInterpolatedStringHandler(0, 2, invariantCulture);
			handler.AppendFormatted(CoreStrings.C83);
			handler.AppendFormatted(ctx.Options.Paths.DataVolumeSerial, "X8");
			text2 = string.Create(invariantCulture, ref handler);
		}
		obj[2] = text2;
		obj[3] = (ctx.Options.NetworkInstall ? CoreStrings.C95 : CoreStrings.C96);
		diagnosticResult.Evidence = string.Join("\n", obj);
		array[0] = diagnosticResult;
		return Task.FromResult((IReadOnlyList<DiagnosticResult>)array);
	}

	private static string DescribeDrive(string root)
	{
		try
		{
			DriveInfo driveInfo = new DriveInfo(Path.GetPathRoot(root) ?? root);
			double value = (double)driveInfo.AvailableFreeSpace / 1024.0 / 1024.0 / 1024.0;
			IFormatProvider invariantCulture = CultureInfo.InvariantCulture;
			DefaultInterpolatedStringHandler handler = new DefaultInterpolatedStringHandler(3, 6, invariantCulture);
			handler.AppendFormatted(CoreStrings.C84);
			handler.AppendFormatted(driveInfo.Name);
			handler.AppendFormatted(CoreStrings.C85);
			handler.AppendFormatted(driveInfo.DriveType);
			handler.AppendFormatted(CoreStrings.C86);
			handler.AppendFormatted(value, "F1");
			handler.AppendLiteral(" GB");
			return string.Create(invariantCulture, ref handler);
		}
		catch
		{
			return CoreStrings.C97;
		}
	}
}
