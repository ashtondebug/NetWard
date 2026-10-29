using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WebBlocker.Filtering
{
    internal sealed class BufferedNetworkReader
    {
        private readonly NetworkStream _stream;
        private readonly byte[] _buffer = new byte[8192];
        private int _offset;
        private int _count;

        public BufferedNetworkReader(NetworkStream stream)
        {
            _stream = stream;
        }

        public async Task<byte[]> ReadHeadersAsync(int maximumBytes, CancellationToken cancellationToken)
        {
            using (var headers = new MemoryStream())
            {
                var matched = 0;
                while (headers.Length < maximumBytes)
                {
                    if (_offset == _count)
                    {
                        _count = await _stream.ReadAsync(_buffer, 0, _buffer.Length, cancellationToken)
                            .ConfigureAwait(false);
                        _offset = 0;
                        if (_count == 0)
                        {
                            throw new EndOfStreamException("The client closed before sending complete headers.");
                        }
                    }

                    var value = _buffer[_offset++];
                    headers.WriteByte(value);

                    switch (matched)
                    {
                        case 0:
                            matched = value == 13 ? 1 : 0;
                            break;
                        case 1:
                            matched = value == 10 ? 2 : (value == 13 ? 1 : 0);
                            break;
                        case 2:
                            matched = value == 13 ? 3 : 0;
                            break;
                        case 3:
                            if (value == 10)
                            {
                                return headers.ToArray();
                            }

                            matched = value == 13 ? 1 : 0;
                            break;
                    }
                }

                throw new InvalidDataException("The request headers exceed the configured size limit.");
            }
        }

        public async Task<byte[]> ReadExactlyAsync(int length, CancellationToken cancellationToken)
        {
            var result = new byte[length];
            var copied = ReadBuffered(result, 0, length);

            while (copied < length)
            {
                var read = await _stream.ReadAsync(result, copied, length - copied, cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    throw new EndOfStreamException("The client closed before sending the request body.");
                }

                copied += read;
            }

            return result;
        }

        public async Task WriteBufferedToAsync(NetworkStream destination, CancellationToken cancellationToken)
        {
            var remaining = _count - _offset;
            if (remaining > 0)
            {
                await destination.WriteAsync(_buffer, _offset, remaining, cancellationToken)
                    .ConfigureAwait(false);
                _offset = _count;
            }
        }

        private int ReadBuffered(byte[] destination, int offset, int count)
        {
            var available = Math.Min(count, _count - _offset);
            if (available > 0)
            {
                Buffer.BlockCopy(_buffer, _offset, destination, offset, available);
                _offset += available;
            }

            return available;
        }
    }
}
