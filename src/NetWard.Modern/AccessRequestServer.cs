using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NetWard.Modern;

/// <summary>
/// Minimal loopback HTTP endpoint used by the block page to submit an access request.
/// It deliberately avoids HttpListener so it does not need an HTTP.sys URL ACL.
/// </summary>
public sealed class AccessRequestServer : IDisposable
{
    public const int Port = 18765;
    private readonly string _requestsPath;
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new CancellationTokenSource();
    private Task _acceptTask;

    public AccessRequestServer(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        _requestsPath = Path.Combine(dataDirectory, "access-requests.jsonl");
        _listener = new TcpListener(IPAddress.Loopback, Port);
    }

    public void Start()
    {
        _listener.Start();
        _acceptTask = AcceptAsync(_stop.Token);
    }

    public void Dispose()
    {
        _stop.Cancel();
        try { _listener.Stop(); } catch (SocketException) { }
        try { _acceptTask?.Wait(1000); } catch (AggregateException) { }
        _stop.Dispose();
    }

    private async Task AcceptAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
            }
            catch (SocketException)
            {
                if (token.IsCancellationRequested) return;
                continue;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            _ = HandleAsync(client, token);
        }
    }

    private async Task HandleAsync(TcpClient client, CancellationToken token)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                var request = await ReadRequestAsync(stream, token).ConfigureAwait(false);
                if (request == null)
                {
                    return;
                }

                if (request.Method == "OPTIONS")
                {
                    await WriteAsync(stream, 204, "", "text/plain", token).ConfigureAwait(false);
                    return;
                }

                if (request.Method != "POST" || !request.Path.Equals("/request", StringComparison.OrdinalIgnoreCase))
                {
                    await WriteAsync(stream, 404, "not found", "text/plain", token).ConfigureAwait(false);
                    return;
                }

                var json = request.Body.TrimStart('\uFEFF');
                using var document = JsonDocument.Parse(json);
                var url = document.RootElement.TryGetProperty("url", out var urlValue)
                    ? urlValue.GetString() ?? ""
                    : "";
                var reason = document.RootElement.TryGetProperty("reason", out var reasonValue)
                    ? reasonValue.GetString() ?? ""
                    : "";
                if (reason.Length > 1000) reason = reason.Substring(0, 1000);

                string host = "";
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri)) host = uri.Host;

                var record = JsonSerializer.Serialize(new
                {
                    id = Guid.NewGuid().ToString("N"),
                    url,
                    host,
                    reason,
                    createdAtUtc = DateTime.UtcNow,
                    handled = false
                });
                File.AppendAllText(_requestsPath, record + Environment.NewLine, Encoding.UTF8);
                await WriteAsync(stream, 200, "{\"ok\":true}", "application/json", token).ConfigureAwait(false);
            }
            catch (Exception exception) when (!(exception is OutOfMemoryException))
            {
                try
                {
                    File.AppendAllText(
                        Path.Combine(Path.GetDirectoryName(_requestsPath), "access-requests-error.log"),
                        DateTime.UtcNow.ToString("o") + " " + exception + Environment.NewLine,
                        Encoding.UTF8);
                }
                catch (Exception) { }
                try
                {
                    await WriteAsync(client.GetStream(), 500, "{\"ok\":false}", "application/json", token)
                        .ConfigureAwait(false);
                }
                catch (Exception) { }
            }
        }
    }

    private static async Task<Request?> ReadRequestAsync(Stream stream, CancellationToken token)
    {
        var buffer = new byte[8192];
        var total = 0;
        var headerEnd = -1;
        while (total < 65536)
        {
            var read = await stream.ReadAsync(buffer, total, buffer.Length - total, token).ConfigureAwait(false);
            if (read <= 0) return null;
            total += read;
            for (var index = 0; index <= total - 4; index++)
            {
                if (buffer[index] == '\r' && buffer[index + 1] == '\n' &&
                    buffer[index + 2] == '\r' && buffer[index + 3] == '\n')
                {
                    headerEnd = index;
                    break;
                }
            }
            if (headerEnd >= 0) break;
        }
        if (headerEnd < 0) return null;

        var headerText = Encoding.ASCII.GetString(buffer, 0, headerEnd);
        var lines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.None);
        var first = lines[0].Split(' ');
        if (first.Length < 2) return null;
        var method = first[0];
        var path = first[1];

        var contentLength = 0;
        foreach (var line in lines)
        {
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(line.Substring(15).Trim(), out var parsed))
            {
                contentLength = parsed;
            }
        }

        var bodyOffset = headerEnd + 4;
        var bodyBytes = new byte[contentLength];
        var already = total - bodyOffset;
        if (already > 0)
        {
            Array.Copy(buffer, bodyOffset, bodyBytes, 0, Math.Min(already, contentLength));
        }
        var remaining = contentLength - Math.Min(already, contentLength);
        var offset = Math.Max(0, already);
        while (remaining > 0)
        {
            var read = await stream.ReadAsync(bodyBytes, offset, remaining, token).ConfigureAwait(false);
            if (read <= 0) break;
            offset += read;
            remaining -= read;
        }

        return new Request(method, path, Encoding.UTF8.GetString(bodyBytes, 0, Math.Min(offset, bodyBytes.Length)));
    }

    private static async Task WriteAsync(Stream stream, int status, string body, string contentType, CancellationToken token)
    {
        var payload = Encoding.UTF8.GetBytes(body);
        var reason = status == 200 ? "OK" : status == 204 ? "No Content" : status == 404 ? "Not Found" : "Error";
        var header = "HTTP/1.1 " + status + " " + reason + "\r\n" +
                     "Content-Type: " + contentType + "; charset=utf-8\r\n" +
                     "Content-Length: " + payload.Length + "\r\n" +
                     "Access-Control-Allow-Origin: *\r\n" +
                     "Access-Control-Allow-Headers: Content-Type\r\n" +
                     "Access-Control-Allow-Methods: POST, OPTIONS\r\n" +
                     "Connection: close\r\n\r\n";
        var headerBytes = Encoding.ASCII.GetBytes(header);
        await stream.WriteAsync(headerBytes, 0, headerBytes.Length, token).ConfigureAwait(false);
        if (payload.Length > 0)
        {
            await stream.WriteAsync(payload, 0, payload.Length, token).ConfigureAwait(false);
        }
        await stream.FlushAsync(token).ConfigureAwait(false);
    }

    private sealed class Request
    {
        public Request(string method, string path, string body)
        {
            Method = method;
            Path = path;
            Body = body;
        }

        public string Method { get; }
        public string Path { get; }
        public string Body { get; }
    }
}