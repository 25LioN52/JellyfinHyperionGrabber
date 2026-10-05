using System;
using Jellyfin.Plugin.HyperionGrabber.Core.Playback;
using Xunit;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Tests.Playback;

public class PlaybackFilterTests
{
    private static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Bob = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Disabled_MatchesNothing()
    {
        Assert.False(PlaybackFilter.Disabled.Matches("kodi", Alice));
    }

    [Fact]
    public void Matches_WhenNotEnabled_ReturnsFalseEvenForSelectedDevice()
    {
        var filter = PlaybackFilter.Create(false, ["kodi"], []);

        Assert.False(filter.Matches("kodi", Alice));
    }

    [Fact]
    public void Matches_WithoutSelectedDevices_ReturnsFalse()
    {
        var filter = PlaybackFilter.Create(true, [], []);

        Assert.False(filter.Matches("kodi", Alice));
    }

    [Fact]
    public void Matches_SelectedDeviceWithoutUserFilter_MatchesEveryUser()
    {
        var filter = PlaybackFilter.Create(true, ["kodi"], []);

        Assert.True(filter.Matches("kodi", Alice));
        Assert.True(filter.Matches("kodi", Guid.Empty));
        Assert.False(filter.Matches("phone", Alice));
    }

    [Fact]
    public void Matches_DeviceIdsAreCaseSensitive()
    {
        var filter = PlaybackFilter.Create(true, ["Kodi"], []);

        Assert.False(filter.Matches("kodi", Alice));
    }

    [Fact]
    public void Matches_WithUserFilter_RequiresSelectedDeviceAndUser()
    {
        var filter = PlaybackFilter.Create(true, ["kodi"], [Alice]);

        Assert.True(filter.Matches("kodi", Alice));
        Assert.False(filter.Matches("kodi", Bob));
        Assert.False(filter.Matches("kodi", Guid.Empty));
        Assert.False(filter.Matches("phone", Alice));
    }

    [Fact]
    public void Create_IgnoresBlankDeviceIdsAndEmptyUserIds()
    {
        var filter = PlaybackFilter.Create(true, ["kodi", string.Empty, " ", null], [Guid.Empty]);

        Assert.Equal(["kodi"], filter.DeviceIds);
        Assert.Empty(filter.UserIds);
        Assert.True(filter.Matches("kodi", Bob));
    }
}
