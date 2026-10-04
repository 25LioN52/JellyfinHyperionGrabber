using System;
using Jellyfin.Plugin.HyperionGrabber.Core.Hyperion;
using Jellyfin.Plugin.HyperionGrabber.Core.Hyperion.Protocol;
using Jellyfin.Plugin.HyperionGrabber.TestSupport;
using Xunit;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Tests.Hyperion.Protocol;

/// <summary>
/// Replies are built with the official FlatBuffers builder; malformed input must only ever raise HyperionProtocolException.
/// </summary>
public class HyperionReplyReaderTests
{
    [Fact]
    public void Parse_RegistrationReply()
    {
        var reply = HyperionReplyReader.Parse(OfficialHyperionCodec.EncodeReply(registered: 150));

        Assert.Equal(new HyperionReply(null, -1, 150), reply);
        Assert.True(reply.IsRegistration);
        Assert.False(reply.IsError);
    }

    [Fact]
    public void Parse_ErrorReply()
    {
        var reply = HyperionReplyReader.Parse(OfficialHyperionCodec.EncodeReply(error: "The priority 50 is not in the priority range between 100 and 199"));

        Assert.True(reply.IsError);
        Assert.Equal("The priority 50 is not in the priority range between 100 and 199", reply.Error);
        Assert.False(reply.IsRegistration);
    }

    [Fact]
    public void Parse_EmptySuccessReply_UsesSchemaDefaults()
    {
        var reply = HyperionReplyReader.Parse(OfficialHyperionCodec.EncodeReply());

        Assert.Equal(new HyperionReply(null, -1, -1), reply);
    }

    [Fact]
    public void Parse_VideoModeReply()
    {
        Assert.Equal(2, HyperionReplyReader.Parse(OfficialHyperionCodec.EncodeReply(video: 2)).Video);
    }

    [Fact]
    public void Parse_TruncatedReplies_FailOnlyWithProtocolException()
    {
        var valid = OfficialHyperionCodec.EncodeReply(error: "some error text", registered: 120);

        for (var length = 0; length < valid.Length; length++)
        {
            AssertParsesOrThrowsProtocolException(valid.AsSpan(0, length).ToArray());
        }
    }

    [Fact]
    public void Parse_RandomBytes_FailOnlyWithProtocolException()
    {
        var random = new Random(20261004);
        for (var iteration = 0; iteration < 5000; iteration++)
        {
            var bytes = new byte[random.Next(0, 64)];
            random.NextBytes(bytes);
            AssertParsesOrThrowsProtocolException(bytes);
        }
    }

    [Fact]
    public void Parse_RootOffsetOutsideBuffer_Throws()
    {
        Assert.Throws<HyperionProtocolException>(() => HyperionReplyReader.Parse([0xF0, 0xFF, 0xFF, 0xFF, 0, 0, 0, 0]));
    }

    private static void AssertParsesOrThrowsProtocolException(byte[] bytes)
    {
        try
        {
            HyperionReplyReader.Parse(bytes);
        }
        catch (HyperionProtocolException)
        {
            // Expected for malformed input.
        }
    }
}
