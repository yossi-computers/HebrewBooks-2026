using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using HebrewBooks.Core.Models;

namespace HebrewBooks.Services.Background;

public sealed class BackgroundProcessorService : IAsyncDisposable
{
	public abstract record Job(Guid Id, string Title)
	{
		public abstract Task ExecuteAsync(IProgress<double> progress, CancellationToken ct);

		public virtual Task ExecuteAsync(IProgress<double> progress, IProgress<IndexProgressReport>? detail, CancellationToken ct)
		{
			return ExecuteAsync(progress, ct);
		}
	}

	private const int LaneCount = 2;

	private readonly Channel<Job>[] _channels;

	private readonly Task[] _consumers;

	private readonly CancellationTokenSource _cts = new CancellationTokenSource();

	private readonly CancellationTokenSource?[] _currentJobCts = new CancellationTokenSource[2];

	private readonly Job?[] _currentJob = new Job[2];

	private readonly ConcurrentDictionary<Guid, byte> _cancelledBeforeStart = new ConcurrentDictionary<Guid, byte>();

	public bool IsJobRunning
	{
		get
		{
			if (_currentJobCts[0] == null)
			{
				return _currentJobCts[1] != null;
			}
			return true;
		}
	}

	public event EventHandler<Job>? JobStarted;

	public event EventHandler<JobProgress>? JobProgress;

	public event EventHandler<JobCompletion>? JobCompleted;

	public event EventHandler<IndexProgressReport>? IndexProgress;

	public BackgroundProcessorService()
	{
		_channels = new Channel<Job>[2];
		_consumers = new Task[2];
		for (int i = 0; i < 2; i++)
		{
			_channels[i] = Channel.CreateUnbounded<Job>(new UnboundedChannelOptions
			{
				SingleReader = true,
				SingleWriter = false
			});
			int captured = i;
			_consumers[i] = Task.Run(() => ConsumeAsync(captured));
		}
	}

	public ValueTask EnqueueAsync(Job job, JobLane lane = JobLane.Bulk, CancellationToken ct = default(CancellationToken))
	{
		return _channels[(int)lane].Writer.WriteAsync(job, ct);
	}

	public ValueTask EnqueueAsync(Job job, CancellationToken ct)
	{
		return EnqueueAsync(job, JobLane.Bulk, ct);
	}

	public void CancelCurrentJob()
	{
		CancelLane(JobLane.Bulk);
	}

	public void CancelJob(Guid jobId)
	{
		for (int i = 0; i < 2; i++)
		{
			Job? obj = _currentJob[i];
			if ((object)obj != null && obj.Id == jobId)
			{
				try
				{
					_currentJobCts[i]?.Cancel();
					return;
				}
				catch
				{
					return;
				}
			}
		}
		_cancelledBeforeStart[jobId] = 0;
	}

	private void CancelLane(JobLane lane)
	{
		try
		{
			_currentJobCts[(int)lane]?.Cancel();
		}
		catch
		{
		}
	}

	private async Task ConsumeAsync(int lane)
	{
		_ = 2;
		try
		{
			await foreach (Job job in _channels[lane].Reader.ReadAllAsync(_cts.Token))
			{
				if (_cancelledBeforeStart.TryRemove(job.Id, out var _))
				{
					this.JobCompleted?.Invoke(this, new JobCompletion(job, null, Cancelled: true));
					continue;
				}
				using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
				_currentJobCts[lane] = linked;
				_currentJob[lane] = job;
				this.JobStarted?.Invoke(this, job);
				Progress<double> progress = new Progress<double>(delegate(double p)
				{
					this.JobProgress?.Invoke(this, new JobProgress(job, p));
				});
				Progress<IndexProgressReport> detail = new Progress<IndexProgressReport>(delegate(IndexProgressReport r)
				{
					this.IndexProgress?.Invoke(this, r);
				});
				try
				{
					await job.ExecuteAsync(progress, detail, linked.Token);
					this.JobCompleted?.Invoke(this, new JobCompletion(job, null, Cancelled: false));
				}
				catch (OperationCanceledException)
				{
					this.JobCompleted?.Invoke(this, new JobCompletion(job, null, Cancelled: true));
				}
				catch (Exception error)
				{
					this.JobCompleted?.Invoke(this, new JobCompletion(job, error, Cancelled: false));
				}
				finally
				{
					_currentJobCts[lane] = null;
					_currentJob[lane] = null;
				}
			}
		}
		catch (OperationCanceledException)
		{
		}
	}

	public async ValueTask DisposeAsync()
	{
		Channel<Job>[] channels = _channels;
		for (int i = 0; i < channels.Length; i++)
		{
			channels[i].Writer.TryComplete();
		}
		_cts.Cancel();
		Task[] consumers = _consumers;
		foreach (Task task in consumers)
		{
			try
			{
				await task;
			}
			catch
			{
			}
		}
		_cts.Dispose();
	}
}
