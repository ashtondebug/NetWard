using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace WebBlocker.Filtering
{
    public interface IProxyDnsResolver
    {
        Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken);
    }

    internal sealed class SystemProxyDnsResolver : IProxyDnsResolver
    {
        public async Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var addresses = await Dns.GetHostAddressesAsync(host).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return addresses;
        }
    }
}
