using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

namespace WebBlocker.Filtering
{
    internal sealed class ProxyRequest
    {
        private ProxyRequest(
            string method,
            string host,
            int port,
            string target,
            string requestedUrl,
            bool isConnect,
            string rewrittenHeaders,
            int contentLength,
            bool hasBody)
        {
            Method = method;
            Host = host;
            Port = port;
            Target = target;
            RequestedUrl = requestedUrl;
            IsConnect = isConnect;
            RewrittenHeaders = rewrittenHeaders;
            ContentLength = contentLength;
            HasBody = hasBody;
        }

        public string Method { get; private set; }

        public string Host { get; private set; }

        public int Port { get; private set; }

        public string Target { get; private set; }

        public string RequestedUrl { get; private set; }

        public bool IsConnect { get; private set; }

        public string RewrittenHeaders { get; private set; }

        public int ContentLength { get; private set; }

        public bool HasBody { get; private set; }

        public static ProxyRequest Parse(byte[] rawHeaders, int maximumBodyBytes)
        {
            var text = Encoding.ASCII.GetString(rawHeaders);
            var lines = text.Substring(0, text.Length - 4).Split(new[] { "\r\n" }, StringSplitOptions.None);
            if (lines.Length == 0)
            {
                throw new InvalidDataException("The HTTP request is empty.");
            }

            var requestParts = lines[0].Split(' ');
            if (requestParts.Length != 3 ||
                requestParts.Any(string.IsNullOrEmpty) ||
                (requestParts[2] != "HTTP/1.0" && requestParts[2] != "HTTP/1.1"))
            {
                throw new InvalidDataException("The HTTP request line is invalid.");
            }

            var method = requestParts[0];
            foreach (var character in method)
            {
                if (!IsTokenCharacter(character))
                {
                    throw new InvalidDataException("The HTTP method is invalid.");
                }
            }

            var headers = new List<KeyValuePair<string, string>>();
            var connectionTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in lines.Skip(1))
            {
                if (line.Length == 0 || line[0] == ' ' || line[0] == '\t')
                {
                    throw new InvalidDataException("Folded or empty HTTP header lines are not supported.");
                }

                var colon = line.IndexOf(':');
                if (colon <= 0)
                {
                    throw new InvalidDataException("An HTTP header is malformed.");
                }

                var name = line.Substring(0, colon);
                foreach (var character in name)
                {
                    if (!IsTokenCharacter(character))
                    {
                        throw new InvalidDataException("An HTTP header name is invalid.");
                    }
                }

                var value = line.Substring(colon + 1).Trim(' ', '\t');
                foreach (var character in value)
                {
                    if ((character < 32 && character != '\t') || character == 127)
                    {
                        throw new InvalidDataException("An HTTP header value contains a control character.");
                    }
                }

                if (string.Equals(name, "Connection", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var token in value.Split(','))
                    {
                        var trimmedToken = token.Trim();
                        if (trimmedToken.Length > 0)
                        {
                            connectionTokens.Add(trimmedToken);
                        }
                    }
                }

                headers.Add(new KeyValuePair<string, string>(name, value));
            }

            if (connectionTokens.Contains("Host") || connectionTokens.Contains("Content-Length"))
            {
                throw new InvalidDataException("Connection-specific framing or routing headers are not supported.");
            }

            var hostHeaders = headers
                .Where(header => string.Equals(header.Key, "Host", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (hostHeaders.Length != 1)
            {
                throw new InvalidDataException("Exactly one Host header is required.");
            }

            if (headers.Any(header =>
                    string.Equals(header.Key, "Upgrade", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(header.Key, "Transfer-Encoding", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(header.Key, "Expect", StringComparison.OrdinalIgnoreCase)))
            {
                throw new NotSupportedException("Upgrade, chunked request bodies, and Expect are not supported.");
            }

            var lengths = headers
                .Where(header => string.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (lengths.Length > 1)
            {
                throw new InvalidDataException("Multiple Content-Length headers are not supported.");
            }

            var contentLength = 0;
            if (lengths.Length == 1 &&
                (!int.TryParse(lengths[0].Value, NumberStyles.None, CultureInfo.InvariantCulture, out contentLength) ||
                 contentLength < 0 ||
                 contentLength > maximumBodyBytes))
            {
                throw new InvalidDataException("The request body length is invalid or exceeds the configured limit.");
            }

            var isConnect = string.Equals(method, "CONNECT", StringComparison.Ordinal);
            string host;
            int port;
            string target;
            string requestedUrl;
            Uri absoluteUri = null;

            if (isConnect)
            {
                ParseConnectTarget(requestParts[1], out host, out port);
                target = null;
                requestedUrl = null;
                if (contentLength != 0)
                {
                    throw new InvalidDataException("CONNECT requests cannot contain a request body.");
                }
            }
            else
            {
                if (!Uri.TryCreate(requestParts[1], UriKind.Absolute, out absoluteUri) ||
                    !string.Equals(absoluteUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                    absoluteUri.UserInfo.Length != 0 ||
                    absoluteUri.Fragment.Length != 0)
                {
                    throw new InvalidDataException("Only absolute-form HTTP requests are supported.");
                }

                try
                {
                    host = WebBlocker.Shared.DomainName.Normalize(absoluteUri.DnsSafeHost);
                }
                catch (ArgumentException exception)
                {
                    throw new InvalidDataException("The request target host is invalid.", exception);
                }

                port = absoluteUri.Port;
                requestedUrl = absoluteUri.OriginalString;
                target = absoluteUri.GetComponents(UriComponents.PathAndQuery, UriFormat.UriEscaped);
                if (target.Length == 0 || target[0] != '/')
                {
                    target = "/" + target;
                }
            }

            string headerHost;
            int headerPort;
            ParseHostHeader(hostHeaders[0].Value, isConnect ? port : 80, out headerHost, out headerPort);
            if (!string.Equals(host, headerHost, StringComparison.Ordinal) || port != headerPort)
            {
                throw new InvalidDataException("The Host header does not match the request target.");
            }

            var rewritten = new StringBuilder();
            rewritten.Append(method).Append(' ').Append(isConnect ? requestParts[1] : target)
                .Append(' ').Append(requestParts[2]).Append("\r\n");

            foreach (var header in headers)
            {
                if (string.Equals(header.Key, "Proxy-Authorization", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(header.Key, "Proxy-Connection", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(header.Key, "Keep-Alive", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(header.Key, "Connection", StringComparison.OrdinalIgnoreCase) ||
                    connectionTokens.Contains(header.Key))
                {
                    continue;
                }

                rewritten.Append(header.Key).Append(": ").Append(header.Value).Append("\r\n");
            }

            if (!isConnect)
            {
                rewritten.Append("Connection: close\r\n");
            }

            rewritten.Append("\r\n");
            return new ProxyRequest(method, host, port, target, requestedUrl, isConnect,
                rewritten.ToString(), contentLength, contentLength > 0);
        }

        private static void ParseConnectTarget(string value, out string host, out int port)
        {
            var separator = value.LastIndexOf(':');
            if (separator <= 0 || separator == value.Length - 1 ||
                value.IndexOf(':') != separator)
            {
                throw new InvalidDataException("CONNECT requires a hostname and explicit TCP port.");
            }

            try
            {
                host = WebBlocker.Shared.DomainName.Normalize(value.Substring(0, separator));
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException("The CONNECT hostname is invalid.", exception);
            }

            if (!int.TryParse(value.Substring(separator + 1), NumberStyles.None,
                    CultureInfo.InvariantCulture, out port) || port < 1 || port > 65535)
            {
                throw new InvalidDataException("The CONNECT port is invalid.");
            }
        }

        private static void ParseHostHeader(string value, int defaultPort, out string host, out int port)
        {
            if (Uri.CheckHostName(value) != UriHostNameType.Unknown)
            {
                try
                {
                    host = WebBlocker.Shared.DomainName.Normalize(value);
                    port = defaultPort;
                    return;
                }
                catch (ArgumentException)
                {
                }
            }

            Uri uri;
            if (!Uri.TryCreate("http://" + value + "/", UriKind.Absolute, out uri) ||
                uri.UserInfo.Length != 0 ||
                uri.AbsolutePath != "/" ||
                uri.Query.Length != 0 ||
                uri.Fragment.Length != 0)
            {
                throw new InvalidDataException("The Host header is invalid.");
            }

            try
            {
                host = WebBlocker.Shared.DomainName.Normalize(uri.DnsSafeHost);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException("The Host header hostname is invalid.", exception);
            }

            port = uri.IsDefaultPort ? defaultPort : uri.Port;
        }

        private static bool IsTokenCharacter(char character)
        {
            return (character >= '0' && character <= '9') ||
                   (character >= 'A' && character <= 'Z') ||
                   (character >= 'a' && character <= 'z') ||
                   "!#$%&'*+-.^_`|~".IndexOf(character) >= 0;
        }
    }
}
