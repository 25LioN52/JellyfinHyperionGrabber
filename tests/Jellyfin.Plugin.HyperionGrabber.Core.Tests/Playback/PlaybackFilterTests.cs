using System;
using System.Linq;
using Jellyfin.Plugin.HyperionGrabber.Core.Hyperion;
using Jellyfin.Plugin.HyperionGrabber.Core.Playback;
using Xunit;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Tests.Playback;

public class PlaybackFilterTests
{
    private static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Bob = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly PlaybackTarget LivingRoom = Target("Living room", "192.168.1.20", "kodi");

    [Fact]
    public void Disabled_MatchesNothing()
    {
        Assert.False(PlaybackFilter.Disabled.Matches("kodi", Alice));
        Assert.Empty(PlaybackFilter.Disabled.Targets);
    }

    [Fact]
    public void Matches_WhenNotEnabled_ReturnsFalseEvenForSelectedDevice()
    {
        var filter = PlaybackFilter.Create(false, [LivingRoom], []);

        Assert.False(filter.Matches(LivingRoom, "kodi", Alice));
    }

    [Fact]
    public void Matches_WithoutSelectedDevices_ReturnsFalse()
    {
        var target = Target("Living room", "192.168.1.20");
        var filter = PlaybackFilter.Create(true, [target], []);

        Assert.False(filter.Matches(target, "kodi", Alice));
        Assert.Empty(filter.DeviceIds);
    }

    [Fact]
    public void Matches_SelectedDeviceWithoutUserFilter_MatchesEveryUser()
    {
        var filter = PlaybackFilter.Create(true, [LivingRoom], []);

        Assert.True(filter.Matches(LivingRoom, "kodi", Alice));
        Assert.True(filter.Matches(LivingRoom, "kodi", Guid.Empty));
        Assert.False(filter.Matches(LivingRoom, "phone", Alice));
    }

    [Fact]
    public void Matches_DeviceIdsAreCaseSensitive()
    {
        var target = Target("Living room", "192.168.1.20", "Kodi");
        var filter = PlaybackFilter.Create(true, [target], []);

        Assert.False(filter.Matches(target, "kodi", Alice));
    }

    [Fact]
    public void Matches_WithUserFilter_RequiresSelectedDeviceAndUser()
    {
        var filter = PlaybackFilter.Create(true, [LivingRoom], [Alice]);

        Assert.True(filter.Matches(LivingRoom, "kodi", Alice));
        Assert.False(filter.Matches(LivingRoom, "kodi", Bob));
        Assert.False(filter.Matches(LivingRoom, "kodi", Guid.Empty));
        Assert.False(filter.Matches(LivingRoom, "phone", Alice));
    }

    [Fact]
    public void Matches_EachTargetFollowsItsOwnDevices()
    {
        var bedroom = Target("Bedroom", "192.168.1.21", "shield");
        var filter = PlaybackFilter.Create(true, [LivingRoom, bedroom], []);

        Assert.True(filter.Matches(LivingRoom, "kodi", Alice));
        Assert.False(filter.Matches(LivingRoom, "shield", Alice));
        Assert.True(filter.Matches(bedroom, "shield", Alice));
        Assert.True(filter.Matches("shield", Alice));
        Assert.Equal(["kodi", "shield"], filter.DeviceIds.Order());
    }

    [Fact]
    public void Create_IgnoresBlankDeviceIdsAndEmptyUserIds()
    {
        var target = PlaybackTarget.Create(null, null, ["kodi", string.Empty, " ", null]);
        var filter = PlaybackFilter.Create(true, [target], [Guid.Empty]);

        Assert.Equal(["kodi"], target.DeviceIds);
        Assert.Empty(filter.UserIds);
        Assert.True(filter.Matches(target, "kodi", Bob));
    }

    [Fact]
    public void Create_KeepsOnlyTheFirstTargetOfAServerAndPriority()
    {
        // Two targets with the same server and priority would fight over it; the host is compared ignoring case.
        var twin = Target("Twin", "192.168.1.20", "shield");
        var other = PlaybackTarget.Create(null, new HyperionClientOptions { Host = "192.168.1.20", Priority = 160 }, ["phone"]);
        var renamed = PlaybackTarget.Create(null, new HyperionClientOptions { Host = " HYPERION.local " }, []);
        var original = PlaybackTarget.Create(null, new HyperionClientOptions { Host = "hyperion.local" }, []);

        var filter = PlaybackFilter.Create(true, [LivingRoom, twin, other, original, renamed], []);

        Assert.Equal([LivingRoom, other, original], filter.Targets);
    }

    [Fact]
    public void Create_KeepsAtMostTheMaximumNumberOfTargets()
    {
        var targets = Enumerable.Range(1, PlaybackFilter.MaxTargets + 2).Select(i => Target($"TV {i}", $"10.0.0.{i}")).ToList();

        var filter = PlaybackFilter.Create(true, targets, []);

        Assert.Equal(targets.Take(PlaybackFilter.MaxTargets), filter.Targets);
    }

    [Theory]
    [InlineData("Living room", "192.168.1.20", "Living room")]
    [InlineData("  ", "192.168.1.20", "192.168.1.20:19400")]
    [InlineData(null, null, "Hyperion")]
    public void Target_IsNamedAfterItsServerWithoutAName(string? name, string? host, string expected)
    {
        var target = PlaybackTarget.Create(name, host is null ? null : new HyperionClientOptions { Host = host }, []);

        Assert.Equal(expected, target.Name);
    }

    [Fact]
    public void TargetKey_IdentifiesTheServerAndPriority()
    {
        var key = LivingRoom.Key;

        Assert.Equal(new PlaybackTargetKey("192.168.1.20", 19400, 150), key);
        Assert.Equal(key, new PlaybackTargetKey("192.168.1.20", 19400, 150));
        Assert.Equal("192.168.1.20:19400 (priority 150)", key.ToString());
        Assert.Equal(PlaybackTargetKey.None, PlaybackTarget.Create(null, null, []).Key);
        Assert.Equal("no Hyperion server", PlaybackTargetKey.None.ToString());
    }

    private static PlaybackTarget Target(string name, string host, params string[] deviceIds)
        => PlaybackTarget.Create(name, new HyperionClientOptions { Host = host }, deviceIds);
}
