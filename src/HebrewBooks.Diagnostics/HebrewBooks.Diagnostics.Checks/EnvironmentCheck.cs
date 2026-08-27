using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using HebrewBooks.Core.Resources;

namespace HebrewBooks.Diagnostics.Checks;

public sealed class EnvironmentCheck : IDiagnosticCheck
{
	public string Id => "environment";

	public string Category => CoreStrings.C140;

	public Task<IReadOnlyList<DiagnosticResult>> RunAsync(DiagnosticContext ctx, CancellationToken ct)
	{
		string value = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "?";
		Architecture processArchitecture = RuntimeInformation.ProcessArchitecture;
		bool flag = processArchitecture != Architecture.X86;
		return Task.FromResult((IReadOnlyList<DiagnosticResult>)new DiagnosticResult[1]
		{
			new DiagnosticResult
			{
				Id = "environment.info",
				Category = Category,
				Title = (flag ? CoreStrings.C141 : CoreStrings.C140),
				Severity = ((!flag) ? DiagnosticSeverity.Info : DiagnosticSeverity.Warning),
				Detail = $"{CoreStrings.C28}{value}{CoreStrings.C136}{processArchitecture}",
				Evidence = string.Join("\n", ".NET: " + RuntimeInformation.FrameworkDescription, CoreStrings.C137 + RuntimeInformation.OSDescription, CoreStrings.C138 + (Environment.Is64BitProcess ? CoreStrings.C142 : CoreStrings.C143), CoreStrings.C139 + Environment.MachineName)
			}
		});
	}
}
