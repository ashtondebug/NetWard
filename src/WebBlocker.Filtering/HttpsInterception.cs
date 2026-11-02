using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WebBlocker.Filtering
{
    /// <summary>
    /// Terminates TLS only for hosts that the policy already blocked, so the browser can render
    /// the normal NetWard block page instead of a generic tunnel error. Allowed hosts are still
    /// tunneled untouched. Requires the local NetWard root certificate to be trusted.
    /// </summary>
    public sealed class HttpsInterception : IDisposable
    {
        private const string AuthorityFileName = "https-ca.pfx";
        private const int MaximumHeaderBytes = 32768;

        private readonly string _dataDirectory;
        private readonly ConcurrentDictionary<string, X509Certificate2> _leafCertificates =
            new ConcurrentDictionary<string, X509Certificate2>(StringComparer.OrdinalIgnoreCase);
        private readonly object _authoritySync = new object();
        private X509Certificate2 _authority;
        private bool _trusted;

        public HttpsInterception(string dataDirectory)
        {
            _dataDirectory = dataDirectory;
        }

        public bool EnsureTrusted()
        {
            lock (_authoritySync)
            {
                if (_trusted)
                {
                    return true;
                }

                var authority = LoadOrCreateAuthority();
                try
                {
                    using (var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser))
                    {
                        store.Open(OpenFlags.ReadWrite);
                        var existing = store.Certificates.Find(
                            X509FindType.FindByThumbprint, authority.Thumbprint, validOnly: false);
                        if (existing.Count == 0)
                        {
                            var publicCertificate = new X509Certificate2(
                                authority.Export(X509ContentType.Cert));
                            store.Add(publicCertificate);
                        }
                    }

                    _authority = authority;
                    _trusted = true;
                    return true;
                }
                catch (CryptographicException)
                {
                    return false;
                }
                catch (UnauthorizedAccessException)
                {
                    return false;
                }
            }
        }

        public async Task ServeBlockedPageAsync(
            TcpClient client,
            string host,
            string pagePath,
            CancellationToken cancellationToken)
        {
            if (!_trusted || string.IsNullOrEmpty(host) || IPAddress.TryParse(host, out _))
            {
                throw new InvalidOperationException("TLS interception is not available for this host.");
            }

            var certificate = GetLeafCertificate(host);
            var stream = client.GetStream();
            var established = Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection Established\r\n\r\n");
            await stream.WriteAsync(established, 0, established.Length, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);

            using (var ssl = new SslStream(stream, leaveInnerStreamOpen: false))
            {
                await ssl.AuthenticateAsServerAsync(
                    certificate,
                    clientCertificateRequired: false,
                    enabledSslProtocols: SslProtocols.Tls12,
                    checkCertificateRevocation: false).ConfigureAwait(false);

                await ReadRequestHeadersAsync(ssl, cancellationToken).ConfigureAwait(false);
                var page = File.ReadAllBytes(pagePath);
                var header = string.Format(
                    "HTTP/1.1 403 Forbidden\r\n" +
                    "Content-Type: text/html; charset=utf-8\r\n" +
                    "Content-Length: {0}\r\n" +
                    "Connection: close\r\n" +
                    "Cache-Control: no-store\r\n\r\n",
                    page.Length);
                var headerBytes = Encoding.ASCII.GetBytes(header);
                await ssl.WriteAsync(headerBytes, 0, headerBytes.Length, cancellationToken).ConfigureAwait(false);
                await ssl.WriteAsync(page, 0, page.Length, cancellationToken).ConfigureAwait(false);
                await ssl.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        public void Dispose()
        {
            foreach (var certificate in _leafCertificates.Values)
            {
                certificate.Dispose();
            }

            _leafCertificates.Clear();
            _authority?.Dispose();
        }

        private X509Certificate2 GetLeafCertificate(string host)
        {
            X509Certificate2 cached;
            if (_leafCertificates.TryGetValue(host, out cached) &&
                cached.NotAfter > DateTime.Now.AddDays(1))
            {
                return cached;
            }

            lock (_authoritySync)
            {
                if (_leafCertificates.TryGetValue(host, out cached) &&
                    cached.NotAfter > DateTime.Now.AddDays(1))
                {
                    return cached;
                }

                var authority = LoadOrCreateAuthority();
                using (var rsa = RSA.Create(2048))
                {
                    var request = new CertificateRequest(
                        "CN=" + host,
                        rsa,
                        HashAlgorithmName.SHA256,
                        RSASignaturePadding.Pkcs1);
                    var san = new SubjectAlternativeNameBuilder();
                    san.AddDnsName(host);
                    request.CertificateExtensions.Add(san.Build());
                    request.CertificateExtensions.Add(
                        new X509BasicConstraintsExtension(false, false, 0, false));
                    request.CertificateExtensions.Add(
                        new X509KeyUsageExtension(
                            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                            critical: false));
                    request.CertificateExtensions.Add(
                        new X509EnhancedKeyUsageExtension(
                            new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") },
                            critical: false));

                    var serial = new byte[16];
                    using (var random = new RNGCryptoServiceProvider())
                    {
                        random.GetBytes(serial);
                    }
                    serial[0] &= 0x7F;

                    var notBefore = DateTimeOffset.UtcNow.AddDays(-1);
                    var notAfter = DateTimeOffset.UtcNow.AddMonths(3);
                    var leaf = request.Create(authority, notBefore, notAfter, serial);
                    using (var withKey = leaf.CopyWithPrivateKey(rsa))
                    {
                        // Schannel requires the private key to live in a user key container,
                        // not an ephemeral RSA object, so round-trip the leaf through PFX.
                        var pfx = withKey.Export(X509ContentType.Pfx, (string)null);
                        var usable = new X509Certificate2(
                            pfx,
                            (string)null,
                            X509KeyStorageFlags.Exportable | X509KeyStorageFlags.UserKeySet);
                        usable.FriendlyName = "NetWard interception for " + host;
                        _leafCertificates.AddOrUpdate(host, usable, (_, old) =>
                        {
                            old.Dispose();
                            return usable;
                        });
                        return usable;
                    }
                }
            }
        }

        private X509Certificate2 LoadOrCreateAuthority()
        {
            if (_authority != null)
            {
                return _authority;
            }

            Directory.CreateDirectory(_dataDirectory);
            var path = Path.Combine(_dataDirectory, AuthorityFileName);
            if (File.Exists(path))
            {
                _authority = new X509Certificate2(
                    path,
                    (string)null,
                    X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
                return _authority;
            }

            using (var rsa = RSA.Create(2048))
            {
                var request = new CertificateRequest(
                    "CN=NetWard Local CA, O=NetWard",
                    rsa,
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1);
                request.CertificateExtensions.Add(
                    new X509BasicConstraintsExtension(true, false, 0, true));
                request.CertificateExtensions.Add(
                    new X509KeyUsageExtension(
                        X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign,
                        critical: true));
                request.CertificateExtensions.Add(
                    new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

                var certificate = request.CreateSelfSigned(
                    DateTimeOffset.UtcNow.AddDays(-1),
                    DateTimeOffset.UtcNow.AddYears(5));
                certificate.FriendlyName = "NetWard Local CA";
                File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx, (string)null));
                _authority = certificate;
                return certificate;
            }
        }

        private static async Task ReadRequestHeadersAsync(Stream stream, CancellationToken cancellationToken)
        {
            var buffer = new byte[4096];
            var total = 0;
            var match = 0;
            while (total < MaximumHeaderBytes)
            {
                var read = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)
                    .ConfigureAwait(false);
                if (read <= 0)
                {
                    return;
                }

                total += read;
                for (var index = 0; index < read; index++)
                {
                    var value = buffer[index];
                    switch (match)
                    {
                        case 0:
                            match = value == '\r' ? 1 : 0;
                            break;
                        case 1:
                            match = value == '\n' ? 2 : (value == '\r' ? 1 : 0);
                            break;
                        case 2:
                            match = value == '\r' ? 3 : 0;
                            break;
                        case 3:
                            if (value == '\n')
                            {
                                return;
                            }

                            match = value == '\r' ? 1 : 0;
                            break;
                    }
                }
            }
        }
    }
}