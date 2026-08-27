using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace HebrewBooks.Infrastructure.Paths;

internal static class DriveProbe
{
	[Flags]
	private enum ErrorModes : uint
	{
		SEM_FAILCRITICALERRORS = 1u,
		SEM_NOOPENFILEERRORBOX = 0x8000u
	}

	private sealed class Restore(ErrorModes old) : IDisposable
	{
		public void Dispose()
		{
			try
			{
				SetThreadErrorMode(old, out var _);
			}
			catch
			{
			}
		}
	}

	private sealed class NoOp : IDisposable
	{
		public static readonly NoOp Instance = new NoOp();

		public void Dispose()
		{
		}
	}

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool SetThreadErrorMode(ErrorModes newMode, out ErrorModes oldMode);

	public static IDisposable EnterFailFast()
	{
		try
		{
			if (SetThreadErrorMode(ErrorModes.SEM_FAILCRITICALERRORS | ErrorModes.SEM_NOOPENFILEERRORBOX, out var oldMode))
			{
				return new Restore(oldMode);
			}
		}
		catch
		{
		}
		return NoOp.Instance;
	}

	public static T RunWithTimeout<T>(Func<T> probe, T timedOutValue, int timeoutMs, string what, ILogger? log)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		Task<T> task = Task.Run(delegate
		{
			using (EnterFailFast())
			{
				return probe();
			}
		});
		try
		{
			if (!task.Wait(timeoutMs))
			{
				log?.LogWarning("DriveProbe: {What} TIMED OUT after {Ms}ms — skipping this drive", what, timeoutMs);
				return timedOutValue;
			}
		}
		catch
		{
			return timedOutValue;
		}
		if (stopwatch.ElapsedMilliseconds > 1000)
		{
			log?.LogWarning("DriveProbe: {What} was slow: {Ms}ms", what, stopwatch.ElapsedMilliseconds);
		}
		return task.Result;
	}
}
