using System;
using System.Buffers.Binary;
using Jellyfin.Plugin.HyperionGrabber.Core.Hyperion;
using Jellyfin.Plugin.HyperionGrabber.Core.Hyperion.Protocol;
using Jellyfin.Plugin.HyperionGrabber.TestSupport;
using Xunit;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Tests.Hyperion.Protocol;

/// <summary>
/// Our hand-written encoder must produce buffers that the official FlatBuffers runtime verifies and decodes.
/// </summary>
public class HyperionRequestWriterTests
{
    [Theory]
    [InlineData("Jellyfin", 150)]
    [InlineData("J", 100)]
    [InlineData("Jellyfin – Wohnzimmer \U0001F4FA", 199)]
    public void WriteRegister_IsVerifiedAndDecodedByOfficialRuntime(string origin, int priority)
    {
        var request = EncodeAndDecode(b => HyperionRequestWriter.WriteRegister(b, origin, priority), HyperionRequestWriter.GetMaxRegisterLength(origin));

        var register = Assert.IsType<RegisterRequest>(request);
        Assert.Equal(origin, register.Origin);
        Assert.Equal(priority, register.Priority);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(7, 3)] // odd sizes exercise alignment padding
    [InlineData(16, 9)]
    [InlineData(64, 36)]
    [InlineData(160, 90)]
    public void WriteImage_IsVerifiedAndDecodedByOfficialRuntime(int width, int height)
    {
        var pixels = TestHelpers.RandomBytes(width * height * 3, seed: (width * 1000) + height);

        var request = EncodeAndDecode(
            b => HyperionRequestWriter.WriteImage(b, pixels, width, height, HyperionDefaults.InfiniteDuration),
            HyperionRequestWriter.GetMaxImageLength(pixels.Length));

        var image = Assert.IsType<ImageRequest>(request);
        Assert.Equal(width, image.Width);
        Assert.Equal(height, image.Height);
        Assert.Equal(pixels, image.Data);
        Assert.Equal(HyperionDefaults.InfiniteDuration, image.Duration);
    }

    [Fact]
    public void WriteImage_KeepsExplicitDuration()
    {
        var request = EncodeAndDecode(b => HyperionRequestWriter.WriteImage(b, new byte[3], 1, 1, 5000), HyperionRequestWriter.GetMaxImageLength(3));

        Assert.Equal(5000, Assert.IsType<ImageRequest>(request).Duration);
    }

    [Fact]
    public void WriteClear_IsVerifiedAndDecodedByOfficialRuntime()
    {
        var request = EncodeAndDecode(b => HyperionRequestWriter.WriteClear(b, 150), HyperionRequestWriter.MaxClearLength);

        Assert.Equal(new ClearRequest(150), request);
    }

    [Fact]
    public void WriteColor_IsVerifiedAndDecodedByOfficialRuntime()
    {
        var request = EncodeAndDecode(b => HyperionRequestWriter.WriteColor(b, 0x123456, 1000), HyperionRequestWriter.MaxColorLength);

        Assert.Equal(new ColorRequest(0x123456, 1000), request);
    }

    [Fact]
    public void WriteClear_ProducesExactWireFormat()
    {
        // Golden bytes lock the layout documented in docs/development/hyperion-protocol.md.
        const string Expected =
            "00000028" // big-endian size prefix: 40 bytes
            + "0C000000" // root uoffset -> Request table at 12
            + "08000C0004000800" // Request vtable: 8 bytes, table 12 bytes, fields at 4 and 8
            + "08000000" // Request table: soffset 8 -> vtable at 4
            + "03000000" // command_type = Clear (3)
            + "0C000000" // command uoffset -> Clear table at 32
            + "0600080004000000" // Clear vtable: 6 bytes, table 8 bytes, field at 4, 2 bytes padding
            + "08000000" // Clear table: soffset 8 -> vtable at 24
            + "96000000"; // priority = 150
        var buffer = new byte[HyperionRequestWriter.MaxClearLength];

        var written = HyperionRequestWriter.WriteClear(buffer, 150);

        Assert.Equal(Expected, Convert.ToHexString(buffer, 0, written));
    }

    [Fact]
    public void SizePrefix_IsBigEndian()
    {
        var pixels = new byte[300 * 3];
        var buffer = new byte[HyperionRequestWriter.GetMaxImageLength(pixels.Length)];

        var written = HyperionRequestWriter.WriteImage(buffer, pixels, 300, 1, HyperionDefaults.InfiniteDuration);

        Assert.Equal((uint)(written - HyperionRequestWriter.SizePrefixLength), BinaryPrimitives.ReadUInt32BigEndian(buffer));
        Assert.Equal(0, buffer[0]); // most significant byte first
    }

    [Theory]
    [InlineData(2, 2, 11)]
    [InlineData(0, 1, 0)]
    [InlineData(1, -1, 3)]
    public void WriteImage_RejectsPixelDataThatDoesNotMatchSize(int width, int height, int length)
    {
        Assert.Throws<ArgumentException>(() => HyperionRequestWriter.WriteImage(new byte[1024], new byte[length], width, height, -1));
    }

    [Fact]
    public void Writers_RejectTooSmallDestination()
    {
        Assert.Throws<ArgumentException>(() => HyperionRequestWriter.WriteClear(new byte[16], 150));
        Assert.Throws<ArgumentException>(() => HyperionRequestWriter.WriteImage(new byte[64], new byte[30], 10, 1, -1));
    }

    private static ReceivedRequest EncodeAndDecode(Func<byte[], int> write, int maxLength)
    {
        var buffer = new byte[maxLength];
        var written = write(buffer);

        Assert.InRange(written, HyperionRequestWriter.SizePrefixLength + 1, maxLength);
        var length = (int)BinaryPrimitives.ReadUInt32BigEndian(buffer);
        Assert.Equal(written - HyperionRequestWriter.SizePrefixLength, length);
        return OfficialHyperionCodec.DecodeRequest(buffer.AsSpan(HyperionRequestWriter.SizePrefixLength, length).ToArray());
    }
}
