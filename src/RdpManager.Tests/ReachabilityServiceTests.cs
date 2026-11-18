using System.Net;
using System.Net.Sockets;
using RdpManager.Services;
using Xunit;

namespace RdpManager.Tests;

public class ReachabilityServiceTests
{
    [Fact]
    public async Task Open_port_is_reachable_closed_port_is_not()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        Assert.True(await ReachabilityService.IsPortOpenAsync("127.0.0.1", port));

        listener.Stop();
        Assert.False(await ReachabilityService.IsPortOpenAsync("127.0.0.1", port));
    }
}
