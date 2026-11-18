using System.Windows.Input;
using RdpManager.Models;
using RdpManager.Rdp;
using Xunit;

namespace RdpManager.Tests;

public class VncKeyMapTests
{
    [Theory]
    [InlineData(Key.Enter, 0xFF0Du)]
    [InlineData(Key.Back, 0xFF08u)]
    [InlineData(Key.LeftCtrl, 0xFFE3u)]
    [InlineData(Key.F12, 0xFFC9u)]
    [InlineData(Key.Delete, 0xFFFFu)]
    public void Special_keys_map_to_x11_keysyms(Key key, uint expected)
    {
        Assert.True(VncKeyMap.TryGetSpecial(key, out var keysym));
        Assert.Equal(expected, keysym);
    }

    [Fact]
    public void Letter_keys_are_not_special_they_come_as_text()
        => Assert.False(VncKeyMap.TryGetSpecial(Key.A, out _));

    [Theory]
    [InlineData(Key.C, 'c')]
    [InlineData(Key.D5, '5')]
    public void Shortcut_keys_map_to_lowercase_ascii(Key key, char expected)
    {
        Assert.True(VncKeyMap.TryGetShortcut(key, out var keysym));
        Assert.Equal(expected, (char)keysym);
    }

    [Theory]
    [InlineData('a', 0x61u)]
    [InlineData('@', 0x40u)]
    [InlineData('ç', 0xE7u)]        // Latin-1: dogrudan
    [InlineData('ş', 0x0100015Fu)]  // Latin-1 disi: 0x01000000 + Unicode
    [InlineData('€', 0x010020ACu)]
    public void Characters_map_to_keysyms(char c, uint expected)
        => Assert.Equal(expected, VncKeyMap.FromChar(c));

    [Theory]
    [InlineData("RDP", 3389)]
    [InlineData("VNC", 5900)]
    [InlineData("ssh", 22)]
    public void Protocols_have_default_ports(string protocol, int port)
        => Assert.Equal(port, RdpConnection.DefaultPort(protocol));
}
