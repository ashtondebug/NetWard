using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace NetWard.Modern;

/// <summary>
/// Hosts the local proxy by starting the engine bridge as a child process. The child exits when this
/// process exits (--parent-pid) or when it is stopped here, so no background listener is left behind.
/// </summary>
public sealed class ProxyHostService : IDisposable
{
    private readonly string _bridgePath;
    private readonly string _databasePath;
    private Process _process;

    public ProxyHostService(string databasePath)
    {
        _databasePath = databasePath;
        _bridgePath = Path.Combine(AppContext.BaseDirectory, "engine", "NetWard.EngineBridge.exe");
    }

    public bool IsRunning => _process != null && !_process.HasExited;

    public int Port { get; private set; }

    public long PolicyVersion { get; private set; }

    public async Task<int> StartAsync()
    {
        if (IsRunning)
        {
            return Port;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = _bridgePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--database");
        startInfo.ArgumentList.Add(_databasePath);
        startInfo.ArgumentList.Add("--port");
        startInfo.ArgumentList.Add("0");
        startInfo.ArgumentList.Add("--parent-pid");
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString());

        var process = new Process { StartInfo = startInfo };
        var ready = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data == null)
            {
                return;
            }

            const string policyMarker = "POLICY version=";
            if (args.Data.StartsWith(policyMarker, StringComparison.Ordinal) &&
                long.TryParse(args.Data.Substring(policyMarker.Length).Trim(), out var policyVersion))
            {
                PolicyVersion = policyVersion;
                return;
            }

            const string marker = "READY port=";
            if (args.Data.StartsWith(marker, StringComparison.Ordinal))
            {
                var value = args.Data.Substring(marker.Length).Trim();
                if (int.TryParse(value, out var port))
                {
                    ready.TrySetResult(port);
                }
            }
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data != null && args.Data.Length > 0)
            {
                ready.TrySetException(new InvalidOperationException(args.Data));
            }
        };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        _process = process;

        var completed = await Task.WhenAny(ready.Task, Task.Delay(15000)).ConfigureAwait(false);
        if (completed != ready.Task)
        {
            Stop();
            throw new TimeoutException("代理未在 15 秒内启动。请确认已保存策略。");
        }

        Port = await ready.Task.ConfigureAwait(false);
        return Port;
    }

    public void Stop()
    {
        if (_process == null)
        {
            return;
        }

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill();
                _process.WaitForExit(5000);
            }
        }
        catch (Exception)
        {
        }
        finally
        {
            _process.Dispose();
            _process = null;
            Port = 0;
            PolicyVersion = 0;
        }
    }

    public void Dispose()
    {
        Stop();
    }
}