using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoUpgrade.Models;

namespace AutoUpgrade.Services;

public class WorkerEngine
{
    private CancellationTokenSource? _cts;
    private readonly ProxyManager _proxyManager;

    public bool IsRunning { get; private set; }

    public event Action<string>? LogMessage;
    public event Action<AccountTask>? TaskUpdated;
    public event Action? EngineCompleted;

    public WorkerEngine(ProxyManager proxyManager)
    {
        _proxyManager = proxyManager;
    }

    public async Task StartAsync(IEnumerable<AccountTask> tasks, int threadCount)
    {
        if (IsRunning) return;

        _cts = new CancellationTokenSource();
        IsRunning = true;
        var token = _cts.Token;

        var taskList = tasks.Where(t => t.StatusType == TaskStatusType.Pending || t.StatusType == TaskStatusType.Error).ToList();
        if (taskList.Count == 0)
        {
            IsRunning = false;
            LogMessage?.Invoke("[INFO] No pending tasks to process.");
            EngineCompleted?.Invoke();
            return;
        }

        if (_proxyManager.Count == 0)
        {
            IsRunning = false;
            LogMessage?.Invoke("[ERROR] Aborted: Proxy is mandatory. Please load at least one proxy.");
            EngineCompleted?.Invoke();
            return;
        }

        LogMessage?.Invoke($"[START] Starting execution for {taskList.Count} task(s) with {threadCount} thread(s)...");

        using var semaphore = new SemaphoreSlim(Math.Max(1, threadCount));
        var workerTasks = new List<Task>();

        foreach (var task in taskList)
        {
            if (token.IsCancellationRequested) break;

            await semaphore.WaitAsync(token).ConfigureAwait(false);

            workerTasks.Add(Task.Run(async () =>
            {
                try
                {
                    if (token.IsCancellationRequested) return;

                    var proxy = _proxyManager.GetNextProxy();
                    await MagnificApiClient.ExecuteUpgradeAsync(
                        task,
                        proxy,
                        msg => LogMessage?.Invoke(msg),
                        token
                    ).ConfigureAwait(false);

                    TaskUpdated?.Invoke(task);
                }
                finally
                {
                    semaphore.Release();
                }
            }, token));
        }

        try
        {
            await Task.WhenAll(workerTasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            LogMessage?.Invoke("[STOP] Execution cancelled by user.");
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"[ERROR] Unexpected engine error: {ex.Message}");
        }
        finally
        {
            IsRunning = false;
            LogMessage?.Invoke("[DONE] All worker threads finished.");
            EngineCompleted?.Invoke();
        }
    }

    public void Stop()
    {
        if (IsRunning && _cts != null)
        {
            LogMessage?.Invoke("[STOP] Stopping all worker threads...");
            _cts.Cancel();
        }
    }
}

