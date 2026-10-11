using System;
using Jellyfin.Plugin.HyperionGrabber.Core.Kodi;
using Xunit;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Tests.Kodi;

public class KodiClockOptionsTests
{
    [Theory]
    [InlineData("Kodi", true)]
    [InlineData("Kodi JellyCon", true)]
    [InlineData("kodi", true)]
    [InlineData("Jellyfin Web", false)]
    [InlineData("Jellyfin Android TV", false)]
    [InlineData(null, false)]
    public void IsKodi(string? client, bool expected) => Assert.Equal(expected, KodiClockOptions.IsKodi(client));

    [Theory]
    [InlineData("192.168.1.50", "192.168.1.50")]
    [InlineData("::ffff:192.168.1.50", "192.168.1.50")]
    [InlineData("192.168.1.50:51234", "192.168.1.50")]
    [InlineData("fe80::1", "fe80::1")]
    [InlineData("[fe80::1]:51234", "fe80::1")]
    [InlineData("kodi.local", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ParseAddress(string? remoteEndPoint, string? expected) => Assert.Equal(expected, KodiClockOptions.ParseAddress(remoteEndPoint));

    [Theory]
    [InlineData("http://jellyfin:8096/Videos/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/stream?static=true&MediaSourceId=x", true)]
    [InlineData("http://jellyfin:8096/emby/Videos/AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA/stream.mkv", true)]
    [InlineData("http://jellyfin:8096/Videos/bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb/stream", false)]
    [InlineData("http://jellyfin:8096/Items/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/Download", true)]
    [InlineData("plugin://plugin.video.jellyfin/?mode=play&id=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", false)]
    [InlineData("smb://nas/movies/Big Buck Bunny.mkv", false)]
    [InlineData(null, false)]
    public void ItemIdInFile(string? file, bool found)
    {
        var id = KodiClock.ItemIdInFile(file);

        Assert.Equal(found, id == Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
    }
}
