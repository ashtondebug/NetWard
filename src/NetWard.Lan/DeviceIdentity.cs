using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace NetWard.Lan
{
    /// <summary>
    /// Stable, locally generated device identity: a self-signed RSA certificate that is used as both the
    /// TLS server certificate and the TLS client certificate. Devices authenticate each other by pinning
    /// the certificate thumbprint during pairing; no certificate authority and no shared secret exist.
    /// </summary>
    public sealed class DeviceIdentity : IDisposable
    {
        public const string CertificateFileName = "identity.pfx";
        public const string IdentityFileName = "identity.json";

        private DeviceIdentity(string deviceId, X509Certificate2 certificate)
        {
            DeviceId = deviceId;
            Certificate = certificate;
            Fingerprint = FormatFingerprint(certificate);
        }

        /// <summary>Stable device identifier; generated once and stored next to the key.</summary>
        public string DeviceId { get; private set; }

        public X509Certificate2 Certificate { get; private set; }

        /// <summary>Short human-comparable fingerprint (16 hex digits) shown on both screens.</summary>
        public string Fingerprint { get; private set; }

        public string Thumbprint
        {
            get { return Certificate.Thumbprint; }
        }

        public static DeviceIdentity LoadOrCreate(string directory, string displayName)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new ArgumentException("A directory is required.", "directory");
            }

            Directory.CreateDirectory(directory);
            var certificatePath = Path.Combine(directory, CertificateFileName);
            var identityPath = Path.Combine(directory, IdentityFileName);

            if (File.Exists(certificatePath) && File.Exists(identityPath))
            {
                return LoadExisting(certificatePath, identityPath);
            }

            var deviceId = Guid.NewGuid().ToString("N");
            var certificate = CreateSelfSignedCertificate(displayName);
            try
            {
                File.WriteAllBytes(certificatePath, certificate.Export(X509ContentType.Pfx));
                WriteAtomic(
                    identityPath,
                    "{\"DeviceId\":\"" + deviceId + "\",\"CreatedAtUtc\":\"" +
                    DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) + "\"}");
            }
            catch
            {
                certificate.Dispose();
                throw;
            }

            return new DeviceIdentity(deviceId, certificate);
        }

        private static DeviceIdentity LoadExisting(string certificatePath, string identityPath)
        {
            string deviceId;
            try
            {
                deviceId = ReadDeviceId(identityPath);
            }
            catch (Exception exception) when (!(exception is OutOfMemoryException))
            {
                throw new LanIdentityException(
                    "设备身份文件无法读取：" + identityPath + "。删除该目录后会生成新的设备身份，但需要重新配对。", exception);
            }

            X509Certificate2 certificate;
            try
            {
                var bytes = File.ReadAllBytes(certificatePath);
#if NET9_0_OR_GREATER
                certificate = X509CertificateLoader.LoadPkcs12(bytes, null, X509KeyStorageFlags.Exportable);
#else
                certificate = new X509Certificate2(bytes, (string)null, X509KeyStorageFlags.Exportable);
#endif
            }
            catch (Exception exception) when (!(exception is OutOfMemoryException))
            {
                throw new LanIdentityException(
                    "设备证书无法加载：" + certificatePath + "。证书可能已损坏或被移动，删除后需要重新配对。", exception);
            }

            if (!certificate.HasPrivateKey)
            {
                certificate.Dispose();
                throw new LanIdentityException("设备证书不包含私钥：" + certificatePath);
            }

            return new DeviceIdentity(deviceId, certificate);
        }

        private static string ReadDeviceId(string identityPath)
        {
            var json = File.ReadAllText(identityPath, Encoding.UTF8);
            var marker = "\"DeviceId\":\"";
            var start = json.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0)
            {
                throw new InvalidDataException("The identity file has no device id.");
            }

            start += marker.Length;
            var end = json.IndexOf('"', start);
            if (end <= start)
            {
                throw new InvalidDataException("The identity file has an invalid device id.");
            }

            var value = json.Substring(start, end - start);
            Guid parsed;
            if (!Guid.TryParseExact(value, "N", out parsed))
            {
                throw new InvalidDataException("The identity file has an invalid device id.");
            }

            return value;
        }

        private static X509Certificate2 CreateSelfSignedCertificate(string displayName)
        {
            var subject = "CN=NetWard " + (string.IsNullOrWhiteSpace(displayName) ? "Device" : displayName.Trim());
            if (subject.Length > 200)
            {
                subject = subject.Substring(0, 200);
            }

            using (var rsa = RSA.Create(2048))
            {
                var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                request.CertificateExtensions.Add(
                    new X509BasicConstraintsExtension(false, false, 0, false));
                request.CertificateExtensions.Add(
                    new X509KeyUsageExtension(
                        X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));

                // Both usages are required: the certificate is the TLS server certificate on one side of
                // the connection and the TLS client certificate on the other.
                var usages = new OidCollection
                {
                    new Oid("1.3.6.1.5.5.7.3.1"), // serverAuth
                    new Oid("1.3.6.1.5.5.7.3.2")  // clientAuth
                };
                request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(usages, false));
                request.CertificateExtensions.Add(
                    new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

                var certificate = request.CreateSelfSigned(
                    DateTimeOffset.UtcNow.AddDays(-1),
                    DateTimeOffset.UtcNow.AddYears(10));

                // Round-trip through PKCS#12 so the private key is usable by SslStream on both runtimes.
                var export = certificate.Export(X509ContentType.Pfx);
                certificate.Dispose();
#if NET9_0_OR_GREATER
                return X509CertificateLoader.LoadPkcs12(export, null, X509KeyStorageFlags.Exportable);
#else
                return new X509Certificate2(export, (string)null, X509KeyStorageFlags.Exportable);
#endif
            }
        }

        /// <summary>Short fingerprint of any certificate, formatted as four groups of four hex digits.</summary>
        public static string FormatFingerprint(X509Certificate2 certificate)
        {
            if (certificate == null)
            {
                throw new ArgumentNullException("certificate");
            }

            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(certificate.RawData);
                var builder = new StringBuilder(19);
                for (var index = 0; index < 8; index++)
                {
                    if (index > 0 && index % 2 == 0)
                    {
                        builder.Append('-');
                    }

                    builder.Append(hash[index].ToString("X2", CultureInfo.InvariantCulture));
                }

                return builder.ToString();
            }
        }

        /// <summary>
        /// Constant-time comparison of certificate thumbprints. Used for pinning; never throws for a
        /// null or malformed value, it simply returns false.
        /// </summary>
        public static bool ThumbprintEquals(string left, string right)
        {
            if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
            {
                return false;
            }

            var a = left.Trim();
            var b = right.Trim();
            if (a.Length != b.Length)
            {
                return false;
            }

            var difference = 0;
            for (var index = 0; index < a.Length; index++)
            {
                difference |= char.ToUpperInvariant(a[index]) ^ char.ToUpperInvariant(b[index]);
            }

            return difference == 0;
        }

        private static void WriteAtomic(string path, string content)
        {
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, content, new UTF8Encoding(false));
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            File.Move(temporary, path);
        }

        public void Dispose()
        {
            if (Certificate != null)
            {
                Certificate.Dispose();
                Certificate = null;
            }
        }
    }

    /// <summary>Raised when the local identity files exist but cannot be used.</summary>
    public sealed class LanIdentityException : Exception
    {
        public LanIdentityException(string message)
            : base(message)
        {
        }

        public LanIdentityException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
