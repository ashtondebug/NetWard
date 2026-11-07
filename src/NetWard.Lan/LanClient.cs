using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace NetWard.Lan
{
    /// <summary>
    /// Administrator-side client. Every call opens one mutually authenticated TLS connection, sends one
    /// request and closes; there is no background socket and no connection state to leak between calls.
    /// </summary>
    public static class LanClient
    {
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

        public static PairResultPayload Pair(
            string address,
            int port,
            string code,
            DeviceIdentity administrator,
            string administratorName,
            TimeSpan? timeout = null)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                throw new ArgumentException("被控端地址不能为空。", "address");
            }

            if (string.IsNullOrWhiteSpace(code))
            {
                throw new ArgumentException("配对码不能为空。", "code");
            }

            var request = new LanRequest
            {
                RequestId = Guid.NewGuid().ToString("N"),
                Type = LanRequestTypes.Pair,
                Pair = new PairPayload
                {
                    Code = code.Trim(),
                    AdminId = administrator.DeviceId,
                    AdminName = administratorName
                }
            };

            X509Certificate2 serverCertificate;
            var response = Send(address.Trim(), port, administrator, null, request, timeout ?? DefaultTimeout,
                out serverCertificate);

            try
            {
                if (response.Pair == null)
                {
                    throw new LanProtocolException(LanErrorCodes.Internal, "被控端没有返回配对结果。");
                }

                // The certificate used by the TLS session must be the one whose fingerprint was returned.
                // A relay that answered with its own certificate cannot also produce a matching fingerprint.
                var actual = DeviceIdentity.FormatFingerprint(serverCertificate);
                if (!string.Equals(actual, response.Pair.Fingerprint, StringComparison.OrdinalIgnoreCase) ||
                    !DeviceIdentity.ThumbprintEquals(serverCertificate.Thumbprint, response.Pair.Thumbprint))
                {
                    throw new LanProtocolException(
                        LanErrorCodes.Unauthorized,
                        "被控端证书指纹与配对响应不一致（" + actual + " ≠ " + response.Pair.Fingerprint + "），配对已中止。");
                }

                return response.Pair;
            }
            finally
            {
                if (serverCertificate != null)
                {
                    serverCertificate.Dispose();
                }
            }
        }

        public static StatusPayload GetStatus(PairedDevice device, DeviceIdentity administrator, TimeSpan? timeout = null)
        {
            var request = NewRequest(LanRequestTypes.Status);
            var response = SendToDevice(device, administrator, request, timeout);
            return response.Status ?? throw new LanProtocolException(LanErrorCodes.Internal, "被控端没有返回状态。");
        }

        public static PolicyResultPayload PushPolicy(
            PairedDevice device,
            DeviceIdentity administrator,
            string policyJson,
            TimeSpan? timeout = null)
        {
            if (string.IsNullOrWhiteSpace(policyJson))
            {
                throw new ArgumentException("策略内容不能为空。", "policyJson");
            }

            var request = NewRequest(LanRequestTypes.PushPolicy);
            request.PolicyJson = policyJson;
            var response = SendToDevice(device, administrator, request, timeout);
            return response.Policy ?? throw new LanProtocolException(LanErrorCodes.Internal, "被控端没有返回策略结果。");
        }

        public static EventsPayload PullEvents(
            PairedDevice device,
            DeviceIdentity administrator,
            DateTime sinceUtc,
            int limit,
            TimeSpan? timeout = null)
        {
            var request = NewRequest(LanRequestTypes.PullEvents);
            request.EventsQuery = new EventQueryPayload
            {
                SinceUtc = sinceUtc == DateTime.MinValue
                    ? null
                    : sinceUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
                Limit = limit
            };

            var response = SendToDevice(device, administrator, request, timeout);
            return response.Events ?? new EventsPayload { Items = new List<LanEvent>() };
        }

        public static void Unpair(PairedDevice device, DeviceIdentity administrator, TimeSpan? timeout = null)
        {
            var request = NewRequest(LanRequestTypes.Unpair);
            SendToDevice(device, administrator, request, timeout);
        }

        private static LanRequest NewRequest(string type)
        {
            return new LanRequest
            {
                RequestId = Guid.NewGuid().ToString("N"),
                Type = type
            };
        }

        private static LanResponse SendToDevice(
            PairedDevice device,
            DeviceIdentity administrator,
            LanRequest request,
            TimeSpan? timeout)
        {
            if (device == null)
            {
                throw new ArgumentNullException("device");
            }

            if (string.IsNullOrWhiteSpace(device.Thumbprint))
            {
                throw new LanProtocolException(LanErrorCodes.Unauthorized, "设备没有固定证书指纹，请重新配对。");
            }

            X509Certificate2 ignored;
            return Send(device.Address, device.Port, administrator, device.Thumbprint, request,
                timeout ?? DefaultTimeout, out ignored);
        }

        private static LanResponse Send(
            string address,
            int port,
            DeviceIdentity administrator,
            string expectedThumbprint,
            LanRequest request,
            TimeSpan timeout,
            out X509Certificate2 serverCertificate)
        {
            if (administrator == null)
            {
                throw new ArgumentNullException("administrator");
            }

            serverCertificate = null;
            X509Certificate2 captured = null;
            var certificateMismatch = false;
            using (var client = new TcpClient())
            using (var timeoutSource = new CancellationTokenSource(timeout))
            {
                try
                {
                    var connect = client.ConnectAsync(address, port);
                    if (!connect.Wait((int)timeout.TotalMilliseconds))
                    {
                        throw new TimeoutException("连接被控端超时：" + address + ":" + port.ToString(CultureInfo.InvariantCulture));
                    }

                    using (var ssl = new SslStream(
                        client.GetStream(),
                        false,
                        (sender, certificate, chain, errors) =>
                        {
                            // Copy the certificate: the instance owned by SslStream is invalidated when the
                            // session closes, and the pairing flow needs the fingerprint afterwards.
                            captured = certificate == null ? null : new X509Certificate2(certificate);
                            if (expectedThumbprint == null)
                            {
                                // First contact: the pairing code authorises this session; the fingerprint
                                // is pinned afterwards and compared by the caller.
                                return true;
                            }

                            if (captured == null)
                            {
                                return false;
                            }

                            var matches = DeviceIdentity.ThumbprintEquals(captured.Thumbprint, expectedThumbprint);
                            certificateMismatch = !matches;
                            return matches;
                        }))
                    {
                        var certificates = new X509CertificateCollection { administrator.Certificate };
                        var authenticate = ssl.AuthenticateAsClientAsync(
                            address, certificates, SslProtocols.Tls12, false);
                        if (!authenticate.Wait((int)timeout.TotalMilliseconds))
                        {
                            throw new TimeoutException("与被控端建立安全连接超时。");
                        }

                        if (captured == null)
                        {
                            throw new LanProtocolException(LanErrorCodes.Unauthorized, "被控端没有提供证书。");
                        }

                        serverCertificate = captured;
                        LanFraming.WriteMessageAsync(ssl, LanJson.Serialize(request), timeoutSource.Token)
                            .GetAwaiter().GetResult();
                        var responseJson = LanFraming.ReadMessageAsync(ssl, timeoutSource.Token)
                            .GetAwaiter().GetResult();
                        if (responseJson == null)
                        {
                            throw new LanProtocolException(LanErrorCodes.Internal, "被控端关闭了连接而没有返回结果。");
                        }

                        var response = LanJson.Deserialize<LanResponse>(responseJson);
                        if (response == null)
                        {
                            throw new LanProtocolException(LanErrorCodes.Internal, "被控端返回了空结果。");
                        }

                        if (!response.Ok)
                        {
                            throw new LanProtocolException(
                                string.IsNullOrEmpty(response.Error) ? LanErrorCodes.Internal : response.Error,
                                string.IsNullOrEmpty(response.Message) ? "被控端拒绝了请求。" : response.Message);
                        }

                        return response;
                    }
                }
                catch (AggregateException exception)
                {
                    if (certificateMismatch)
                    {
                        throw new LanProtocolException(
                            LanErrorCodes.Unauthorized, "被控端证书与已配对指纹不一致，连接已拒绝。", exception);
                    }

                    var socketError = exception.GetBaseException() as SocketException;
                    if (socketError != null)
                    {
                        throw new LanProtocolException(
                            LanErrorCodes.Internal,
                            "无法连接被控端 " + address + ":" + port.ToString(CultureInfo.InvariantCulture) +
                            "（" + socketError.Message + "）。",
                            exception);
                    }

                    throw new LanProtocolException(LanErrorCodes.Internal, "与被控端通信失败：" + exception.GetBaseException().Message, exception);
                }
                catch (IOException exception)
                {
                    if (certificateMismatch)
                    {
                        throw new LanProtocolException(
                            LanErrorCodes.Unauthorized, "被控端证书与已配对指纹不一致，连接已拒绝。", exception);
                    }

                    throw new LanProtocolException(LanErrorCodes.Internal, "与被控端通信失败：" + exception.Message, exception);
                }
                catch (SocketException exception)
                {
                    throw new LanProtocolException(
                        LanErrorCodes.Internal,
                        "无法连接被控端 " + address + ":" + port.ToString(CultureInfo.InvariantCulture) + "（" + exception.Message + "）。",
                        exception);
                }
            }
        }
    }
}
