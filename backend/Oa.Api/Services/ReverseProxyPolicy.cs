using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;

namespace Oa.Api.Services;

public static class ReverseProxyPolicy
{
    public static IReadOnlyList<string> ConfiguredValues(IConfiguration configuration) =>
        configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>()?
            .Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).Distinct(StringComparer.Ordinal).ToArray() ?? [];

    public static bool TryParse(string value, out IPAddress address)
    {
        if (!IPAddress.TryParse(value, out address!) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
        {
            address = null!;
            return false;
        }
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        return true;
    }

    public static void Configure(ForwardedHeadersOptions options, IConfiguration configuration)
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = 1;
        options.RequireHeaderSymmetry = true;

        var configured = ConfiguredValues(configuration);
        if (configured.Count == 0) return;

        options.KnownProxies.Clear();
        options.KnownNetworks.Clear();
        foreach (var value in configured)
        {
            if (!TryParse(value, out var address)) throw new InvalidOperationException($"ForwardedHeaders:KnownProxies 包含无效地址 {value}。");
            options.KnownProxies.Add(address);
        }
    }
}
