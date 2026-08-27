using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace HebrewBooks.UI.Services;

public sealed class SingleInstanceManager : IDisposable
{
	private const string MutexName = "Local\\HebrewBooks.SingleInstance.v1";

	private const string PipeName = "HebrewBooks.SingleInstance.v1";

	private Mutex? _mutex;

	private CancellationTokenSource? _serverCts;

	public bool IsPrimary { get; private set; }

	public bool TryAcquire()
	{
		try
		{
			_mutex = new Mutex(initiallyOwned: true, "Local\\HebrewBooks.SingleInstance.v1", out var createdNew);
			IsPrimary = createdNew;
			return createdNew;
		}
		catch (AbandonedMutexException)
		{
			IsPrimary = true;
			return true;
		}
		catch (Exception exception)
		{
			Log.Warning(exception, "SingleInstance: mutex acquire failed; running without the guard");
			IsPrimary = true;
			return true;
		}
	}

	public bool TryAcquireWaiting(int timeoutMs)
	{
		try
		{
			_mutex = new Mutex(initiallyOwned: false, "Local\\HebrewBooks.SingleInstance.v1", out var createdNew);
			if (createdNew)
			{
				IsPrimary = true;
				return true;
			}
			bool flag;
			try
			{
				flag = _mutex.WaitOne(timeoutMs);
			}
			catch (AbandonedMutexException)
			{
				flag = true;
			}
			IsPrimary = flag;
			return flag;
		}
		catch (Exception exception)
		{
			Log.Warning(exception, "SingleInstance: waiting acquire failed; running without the guard");
			IsPrimary = true;
			return true;
		}
	}

	public void SendToPrimary(string payload)
	{
		try
		{
			using NamedPipeClientStream namedPipeClientStream = new NamedPipeClientStream(".", "HebrewBooks.SingleInstance.v1", PipeDirection.Out);
			namedPipeClientStream.Connect(2000);
			using StreamWriter streamWriter = new StreamWriter(namedPipeClientStream)
			{
				AutoFlush = true
			};
			streamWriter.WriteLine(payload ?? string.Empty);
		}
		catch (Exception exception)
		{
			Log.Warning(exception, "SingleInstance: failed to forward to primary instance");
		}
	}

	public void StartServer(Action<string> onMessage)
	{
		_serverCts = new CancellationTokenSource();
		CancellationToken ct = _serverCts.Token;
		Task.Run(async delegate
		{
			while (!ct.IsCancellationRequested)
			{
				try
				{
					using NamedPipeServerStream server = new NamedPipeServerStream("HebrewBooks.SingleInstance.v1", PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
					await server.WaitForConnectionAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
					using StreamReader r = new StreamReader(server);
					string text = await r.ReadLineAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
					if (text != null)
					{
						onMessage(text);
					}
				}
				catch (OperationCanceledException)
				{
					break;
				}
				catch (Exception exception)
				{
					Log.Warning(exception, "SingleInstance: pipe server iteration failed");
				}
			}
		}, ct);
	}

	public void Dispose()
	{
		try
		{
			_serverCts?.Cancel();
		}
		catch
		{
		}
		try
		{
			_mutex?.Dispose();
		}
		catch
		{
		}
	}
}
