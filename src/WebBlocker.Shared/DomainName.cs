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
