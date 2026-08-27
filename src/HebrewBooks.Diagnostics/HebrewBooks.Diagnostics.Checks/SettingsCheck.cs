using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HebrewBooks.Core.Resources;
using HebrewBooks.Infrastructure.Settings;

namespace HebrewBooks.Diagnostics.Checks;

public sealed class SettingsCheck : IDiagnosticCheck
{
	public string Id => "settings";

	public string Category => CoreStrings.C222;

	public Task<IReadOnlyList<DiagnosticResult>> RunAsync(DiagnosticContext ctx, CancellationToken ct)
	{
		List<DiagnosticResult> list = new List<DiagnosticResult>();
		string path = ctx.Settings.SettingsPath;
		if (!ctx.Settings.Exists)
		{
			list.Add(new DiagnosticResult
			{
				Id = "settings.absent",
				Category = Category,
				Title = CoreStrings.C223,
				Severity = DiagnosticSeverity.Info,
				Detail = CoreStrings.C224,
				Evidence = path
			});
			return Task.FromResult((IReadOnlyList<DiagnosticResult>)list);
		}
		BookshelfOptions bookshelfOptions = null;
		string text = null;
		try
		{
			bookshelfOptions = ctx.Settings.Load();
		}
		catch (Exception ex)
		{
			text = ex.Message;
		}
		if (bookshelfOptions == null)
		{
			list.Add(new DiagnosticResult
			{
				Id = "settings.corrupt",
				Category = Category,
				Title = CoreStrings.C225,
				Severity = DiagnosticSeverity.Error,
				Detail = CoreStrings.C226,
				Evidence = path + ((text == null) ? "" : ("\n" + text)),
				Fix = new DiagnosticFix
				{
					Id = "settings.reset",
					Label = CoreStrings.C227,
					Kind = FixKind.Confirm,
					Run = delegate
					{
						string text4 = path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + ".bak";
						File.Copy(path, text4, overwrite: true);
						ctx.Settings.Save(new BookshelfOptions());
						return Task.FromResult(FixOutcome.Ok(CoreStrings.C216 + text4));
					}
				}
			});
			return Task.FromResult((IReadOnlyList<DiagnosticResult>)list);
		}
		bool? usageTelemetryConsent = bookshelfOptions.UsageTelemetryConsent;
		string text2 = ((!usageTelemetryConsent.HasValue) ? CoreStrings.C228 : ((usageTelemetryConsent != true) ? CoreStrings.C143 : CoreStrings.C142));
		string text3 = text2;
		list.Add(new DiagnosticResult
		{
			Id = "settings.ok",
			Category = Category,
			Title = CoreStrings.C229,
			Severity = DiagnosticSeverity.Ok,
			Detail = $"{CoreStrings.C32}{bookshelfOptions.Paths.InstallType}   {CoreStrings.C217}{(bookshelfOptions.ForceProtectMode ? CoreStrings.C142 : CoreStrings.C143)}   {CoreStrings.C218}{(bookshelfOptions.NetworkInstall ? CoreStrings.C142 : CoreStrings.C143)}",
			Evidence = string.Join("\n", CoreStrings.C82 + path, CoreStrings.C219 + bookshelfOptions.View.Theme, CoreStrings.C220 + (bookshelfOptions.Updates.IncludeBeta ? CoreStrings.C142 : CoreStrings.C143), CoreStrings.C221 + text3)
		});
		return Task.FromResult((IReadOnlyList<DiagnosticResult>)list);
	}
}
