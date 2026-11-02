using System;
using System.Globalization;
using System.Net;

namespace WebBlocker.Shared
{
    public static class DomainName
    {
        public static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("A domain name is required.", "value");
            }

            var candidate = value.Trim();

            // Accept pasted URLs as well as bare host names: strip scheme, user-info,
            // path/query/fragment and an explicit port. The proxy matches host names,
            // so one rule covers both http:// and https:// requests.
            var schemeSeparator = candidate.IndexOf("://", StringComparison.Ordinal);
            if (schemeSeparator >= 0)
            {
                candidate = candidate.Substring(schemeSeparator + 3);
            }
            else if (candidate.StartsWith("//", StringComparison.Ordinal))
            {
                candidate = candidate.Substring(2);
            }

            var userInfoSeparator = candidate.LastIndexOf('@');
            if (userInfoSeparator >= 0)
            {
                candidate = candidate.Substring(userInfoSeparator + 1);
            }

            var pathSeparator = candidate.IndexOfAny(new[] { '/', '?', '#' });
            if (pathSeparator >= 0)
            {
                candidate = candidate.Substring(0, pathSeparator);
            }

            var portSeparator = candidate.LastIndexOf(':');
            if (portSeparator > 0 &&
                candidate.IndexOf(':') == portSeparator &&
                int.TryParse(candidate.Substring(portSeparator + 1), out _))
            {
                candidate = candidate.Substring(0, portSeparator);
            }

            if (candidate.Length == 0)
            {
                throw new ArgumentException("A domain name is required.", "value");
            }

            if (candidate.EndsWith(".", StringComparison.Ordinal))
            {
                candidate = candidate.Substring(0, candidate.Length - 1);
            }

            if (candidate.Length == 0 || candidate.EndsWith(".", StringComparison.Ordinal))
            {
                throw new ArgumentException("The domain name contains an empty label.", "value");
            }

            string asciiName;
            try
            {
                asciiName = new IdnMapping { UseStd3AsciiRules = true }.GetAscii(candidate)
                    .ToLowerInvariant();
            }
            catch (ArgumentException exception)
            {
                throw new ArgumentException("The domain name is not a valid IDN hostname.", "value", exception);
            }

            if (asciiName.Length > 253)
            {
                throw new ArgumentException("The domain name exceeds the DNS length limit.", "value");
            }

            IPAddress address;
            if (IPAddress.TryParse(asciiName, out address))
            {
                throw new ArgumentException("IP addresses are not domain rules.", "value");
            }

            var labels = asciiName.Split('.');
            foreach (var label in labels)
            {
                if (label.Length == 0 || label.Length > 63 ||
                    label[0] == '-' || label[label.Length - 1] == '-')
                {
                    throw new ArgumentException("The domain name contains an invalid label.", "value");
                }

                foreach (var character in label)
                {
                    if (!((character >= 'a' && character <= 'z') ||
                          (character >= '0' && character <= '9') ||
                          character == '-'))
                    {
                        throw new ArgumentException("The domain name contains an invalid character.", "value");
                    }
                }
            }

            return asciiName;
        }
    }
}