using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NetWard.Lan;

namespace NetWard.Modern.Administrator;

/// <summary>
/// Administrator-side bridge between the UI, the paired-device list and the LAN client. It owns the
/// administrator device identity, keeps the device list on disk and forwards pulled events into the same
/// SQLite audit database the local views use.
/// </summary>
public sealed class DeviceLinkService
{
    private const int SyncBatchSize = 200;
    private const int MaximumSyncBatches = 10;

    private readonly DeviceStore _store;
    private readonly DeviceIdentity _identity;
    private readonly AdminEngineClient _client;

    public DeviceLinkService(AdminEngineClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WebBlocker",
            "lan",
            "admin");
        _identity = DeviceIdentity.LoadOrCreate(directory, Environment.MachineName);
        _store = new DeviceStore(directory);
    }

    /// <summary>Fingerprint the endpoint operator can compare with the endpoint screen.</summary>
    public string Fingerprint => _identity.Fingerprint;

    public string AdministratorId => _identity.DeviceId;

    public IList<PairedDevice> LoadDevices()
    {
        return _store.Load();
    }

    public PairedDevice Pair(string address, int port, string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("请填写被控端显示的配对码。", nameof(code));
        }

        var result = LanClient.Pair(address, port, code.Trim(), _identity, Environment.MachineName);
        var device = new PairedDevice
        {
            DeviceId = result.DeviceId,
            Name = string.IsNullOrWhiteSpace(result.DeviceName) ? address : result.DeviceName,
            Address = address.Trim(),
            Port = port,
            Thumbprint = result.Thumbprint,
            Fingerprint = result.Fingerprint,
            PairedAtUtc = result.PairedAtUtc,
            LastSeenUtc = DeviceStore.FormatUtc(DateTime.UtcNow)
        };

        _store.AddOrReplace(device);
        return device;
    }

    public StatusPayload RefreshStatus(PairedDevice device)
    {
        var status = LanClient.GetStatus(device, _identity, TimeSpan.FromSeconds(10));
        Touch(device);
        return status;
    }

    public long PushPolicy(PairedDevice device, AdminPolicy policy)
    {
        var json = AdminEngineClient.BuildPolicyJson(policy);
        var result = LanClient.PushPolicy(device, _identity, json, TimeSpan.FromSeconds(20));
        device.LastPolicyVersion = result.Version;
        Touch(device);
        return result.Version;
    }

    /// <summary>
    /// Pulls events newer than the last synchronized timestamp in ascending order and imports them.
    /// The endpoint keeps the events, the administrator keeps a per-device copy, and duplicate ids are
    /// ignored by the database so a retry is always safe.
    /// </summary>
    public async Task<int> SyncEventsAsync(PairedDevice device)
    {
        var since = DeviceStore.ParseUtc(device.LastEventUtc, DateTime.MinValue);
        var imported = 0;
        var advanced = false;

        for (var batch = 0; batch < MaximumSyncBatches; batch++)
        {
            var payload = LanClient.PullEvents(device, _identity, since, SyncBatchSize, TimeSpan.FromSeconds(20));
            var items = payload.Items ?? new List<LanEvent>();
            if (items.Count == 0)
            {
                break;
            }

            var newest = DateTime.MinValue;
            foreach (var item in items)
            {
                var occurred = DeviceStore.ParseUtc(item.OccurredAtUtc, DateTime.MinValue);
                if (occurred > newest)
                {
                    newest = occurred;
                }
            }

            imported += (int)await _client.ImportRemoteEventsAsync(device.DeviceId, BuildEventsJson(items)).ConfigureAwait(false);

            if (newest > since)
            {
                since = newest;
                advanced = true;
            }

            if (items.Count < SyncBatchSize)
            {
                break;
            }
        }

        if (advanced)
        {
            device.LastEventUtc = DeviceStore.FormatUtc(since);
        }

        Touch(device);
        return imported;
    }

    public void Unpair(PairedDevice device)
    {
        try
        {
            LanClient.Unpair(device, _identity, TimeSpan.FromSeconds(10));
        }
        catch (LanProtocolException)
        {
            // The endpoint may be offline or may already have forgotten this administrator; removing the
            // local record is still the correct outcome for revocation.
        }

        _store.Remove(device.DeviceId);
    }

    public Task<List<AdminEventRow>> GetRemoteEventsAsync(PairedDevice device, int limit)
    {
        return _client.GetRemoteEventsAsync(device.DeviceId, limit);
    }

    private void Touch(PairedDevice device)
    {
        device.LastSeenUtc = DeviceStore.FormatUtc(DateTime.UtcNow);
        _store.AddOrReplace(device);
    }

    private static string BuildEventsJson(IList<LanEvent> items)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteStartArray("events");
            foreach (var item in items)
            {
                writer.WriteStartObject();
                writer.WriteString("id", item.Id);
                writer.WriteString("occurredAtUtc", item.OccurredAtUtc);
                writer.WriteString("domain", item.Domain);
                writer.WriteString("method", item.Method);
                writer.WriteNumber("result", item.Result);
                if (string.IsNullOrEmpty(item.RequestedUrl))
                {
                    writer.WriteNull("requestedUrl");
                }
                else
                {
                    writer.WriteString("requestedUrl", item.RequestedUrl);
                }

                writer.WriteNumber("port", item.Port);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public void Dispose()
    {
        _identity.Dispose();
    }
}
