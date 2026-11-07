using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NetWard.Lan;

namespace NetWard.Modern;

/// <summary>
/// Endpoint side of the LAN link: owns the device identity, the pairing state and the TLS listener.
/// Every request is served through the existing engine bridge, so remote policy writes and event reads
/// use exactly the same engine, database and validation as the local UI.
/// </summary>
public sealed class AgentLanService : ILanAgentHandler, IDisposable
{
    private readonly EngineClient _engine;
    private readonly string _databasePath;
    private readonly string _directory;
    private readonly DeviceIdentity _identity;
    private readonly AgentLanStore _store;
    private readonly DateTime _startedAtUtc = DateTime.UtcNow;
    private LanServer _server;

    public AgentLanService(EngineClient engine, string databasePath)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _databasePath = databasePath;
        _directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WebBlocker",
            "lan",
            "agent");
        _identity = DeviceIdentity.LoadOrCreate(_directory, Environment.MachineName);
        _store = new AgentLanStore(_directory);
    }

    public event EventHandler<LanSessionEventArgs> SessionHandled;

    public string DeviceName => Environment.MachineName;

    public string DeviceId => _identity.DeviceId;

    public string Fingerprint => _identity.Fingerprint;

    public bool IsRunning => _server != null && _server.IsRunning;

    public int Port => _server != null ? _server.Port : LanServer.DefaultPort;

    public int PairedAdminCount => _store.CountAdmins();

    public DateTime LastSessionUtc { get; private set; } = DateTime.MinValue;

    public string LastSessionDetail { get; private set; } = "尚无管理端通信";

    /// <summary>Returns the current pairing code, rotating it when it expired or was locked.</summary>
    public string GetPairingCode()
    {
        return _store.EnsurePairingCode(DateTime.UtcNow);
    }

    /// <summary>Forces a new pairing code; used by the operator before showing the screen to an administrator.</summary>
    public string RotatePairingCode()
    {
        return _store.RotatePairingCode(DateTime.UtcNow);
    }

    public void Start()
    {
        if (_server != null)
        {
            return;
        }

        var server = new LanServer(LanServer.DefaultPort, _identity, _store, this);
        server.SessionHandled += OnSessionHandled;
        try
        {
            server.Start();
        }
        catch
        {
            server.Dispose();
            throw;
        }

        _server = server;
    }

    public void Stop()
    {
        var server = _server;
        _server = null;
        if (server != null)
        {
            server.SessionHandled -= OnSessionHandled;
            server.Dispose();
        }
    }

    public string DeviceLabel => DeviceName;

    StatusPayload ILanAgentHandler.GetStatus()
    {
        return BuildStatusAsync().GetAwaiter().GetResult();
    }

    PolicyResultPayload ILanAgentHandler.ApplyPolicy(string policyJson)
    {
        var version = SaveRemotePolicyAsync(policyJson).GetAwaiter().GetResult();
        var status = BuildStatusAsync().GetAwaiter().GetResult();
        return new PolicyResultPayload { Version = version, Rules = status.Rules };
    }

    EventsPayload ILanAgentHandler.GetEvents(string sinceUtc, int limit)
    {
        return ReadEventsSinceAsync(sinceUtc, limit).GetAwaiter().GetResult();
    }

    private async Task<StatusPayload> BuildStatusAsync()
    {
        var json = await _engine.ExecuteAsync("status-json", "--database", _databasePath).ConfigureAwait(false);
        var status = new StatusPayload
        {
            DeviceId = DeviceId,
            DeviceName = DeviceName,
            Fingerprint = Fingerprint,
            ProxyRunning = true,
            UptimeSeconds = (long)Math.Max(0, (DateTime.UtcNow - _startedAtUtc).TotalSeconds),
            PairedAdmins = PairedAdminCount,
            AgentVersion = "Modern Alpha"
        };

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        status.PolicyVersion = root.TryGetProperty("version", out var version) ? version.GetInt64() : 0;
        status.Rules = root.TryGetProperty("rules", out var rules) ? rules.GetInt32() : 0;
        status.Categories = root.TryGetProperty("categories", out var categories) ? categories.GetInt32() : 0;
        status.CategoryRules = root.TryGetProperty("categoryRules", out var categoryRules) ? categoryRules.GetInt32() : 0;
        status.Events = root.TryGetProperty("events", out var events) ? events.GetInt64() : 0;
        status.PolicyMode = root.TryGetProperty("mode", out var mode) ? mode.GetString() : "none";
        return status;
    }

    private async Task<long> SaveRemotePolicyAsync(string policyJson)
    {
        if (string.IsNullOrWhiteSpace(policyJson))
        {
            throw new LanProtocolException(LanErrorCodes.BadRequest, "策略内容为空。");
        }

        var temporary = Path.Combine(Path.GetTempPath(), "netward-remote-policy-" + Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(temporary, policyJson, new UTF8Encoding(false)).ConfigureAwait(false);
        try
        {
            var json = await _engine.ExecuteAsync(
                "save-policy", "--database", _databasePath, "--input", temporary).ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);
            return document.RootElement.GetProperty("version").GetInt64();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            throw new LanProtocolException(
                LanErrorCodes.PolicyRejected, "策略被拒绝：" + exception.Message, exception);
        }
        finally
        {
            try
            {
                File.Delete(temporary);
            }
            catch (IOException)
            {
            }
        }
    }

    private async Task<EventsPayload> ReadEventsSinceAsync(string sinceUtc, int limit)
    {
        var arguments = new List<string>
        {
            "events-json", "--database", _databasePath, "--limit", limit.ToString(CultureInfo.InvariantCulture),
            "--ascending"
        };
        if (!string.IsNullOrWhiteSpace(sinceUtc))
        {
            arguments.Add("--since");
            arguments.Add(sinceUtc);
        }

        var json = await _engine.ExecuteAsync(arguments.ToArray()).ConfigureAwait(false);
        var payload = new EventsPayload { Items = new List<LanEvent>() };
        using var document = JsonDocument.Parse(json);
        foreach (var item in document.RootElement.GetProperty("events").EnumerateArray())
        {
            payload.Items.Add(new LanEvent
            {
                Id = item.GetProperty("id").GetString(),
                OccurredAtUtc = item.GetProperty("occurredAtUtc").GetString(),
                Domain = item.GetProperty("domain").GetString(),
                Method = item.GetProperty("method").GetString(),
                Result = item.GetProperty("result").GetInt32(),
                RequestedUrl = item.TryGetProperty("requestedUrl", out var url) ? url.GetString() : null,
                Port = item.TryGetProperty("port", out var port) ? port.GetInt32() : 0
            });
        }

        return payload;
    }

    private void OnSessionHandled(object sender, LanSessionEventArgs args)
    {
        LastSessionUtc = args.OccurredAtUtc;
        LastSessionDetail = (args.Success ? "成功：" : "失败：") + args.Detail;
        SessionHandled?.Invoke(this, args);
    }

    public void Dispose()
    {
        Stop();
        _identity.Dispose();
    }
}
