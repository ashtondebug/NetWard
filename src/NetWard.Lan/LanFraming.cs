using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NetWard.Lan
{
    /// <summary>
    /// Newline-delimited UTF-8 JSON frames. One request and one response per connection:
    /// the socket is closed after the response, so no connection state survives a failure.
    /// </summary>
    internal static class LanFraming
    {
        public const int MaximumMessageBytes = 512 * 1024;

        public static async Task<string> ReadMessageAsync(Stream stream, CancellationToken cancellationToken)
        {
            var buffer = new byte[8192];
            using (var accumulated = new MemoryStream())
            {
                while (true)
                {
                    var read = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                    if (read <= 0)
                    {
                        if (accumulated.Length == 0)
                        {
                            return null;
                        }

                        throw new LanProtocolException(LanErrorCodes.BadRequest, "消息在结束符之前被截断。");
                    }

                    for (var index = 0; index < read; index++)
                    {
                        if (buffer[index] == (byte)'\n')
                        {
                            var bytes = accumulated.ToArray();
                            return Encoding.UTF8.GetString(bytes).TrimEnd('\r');
                        }

                        accumulated.WriteByte(buffer[index]);
                        if (accumulated.Length > MaximumMessageBytes)
                        {
                            throw new LanProtocolException(LanErrorCodes.BadRequest, "消息超过大小上限。");
                        }
                    }
                }
            }
        }

        public static async Task WriteMessageAsync(Stream stream, string json, CancellationToken cancellationToken)
        {
            var bytes = Encoding.UTF8.GetBytes(json + "\n");
            await stream.WriteAsync(bytes, 0, bytes.Length, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
