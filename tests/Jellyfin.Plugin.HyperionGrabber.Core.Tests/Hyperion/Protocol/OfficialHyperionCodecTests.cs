using Jellyfin.Plugin.HyperionGrabber.TestSupport;
using Xunit;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Tests.Hyperion.Protocol;

/// <summary>
/// Self-test of the verification harness: it must accept buffers produced by the official FlatBuffers builder.
/// </summary>
public class OfficialHyperionCodecTests
{
    [Fact]
    public void DecodeRequest_AcceptsBufferFromOfficialBuilder()
    {
        Assert.Equal(new ClearRequest(150), OfficialHyperionCodec.DecodeRequest(OfficialHyperionCodec.EncodeClearRequest(150)));
    }

    [Theory]
    [InlineData(0, 0x0D)] // root offset points at a misaligned table
    [InlineData(0, 0xFF)] // root offset points outside the buffer
    public void DecodeRequest_RejectsCorruptBuffer(int index, byte value)
    {
        var bytes = OfficialHyperionCodec.EncodeClearRequest(150);
        bytes[index] = value;

        Assert.Throws<System.IO.InvalidDataException>(() => OfficialHyperionCodec.DecodeRequest(bytes));
    }
}
