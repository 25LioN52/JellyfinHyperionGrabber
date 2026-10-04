using System;
using System.Buffers.Binary;
using System.Text;
using static Jellyfin.Plugin.HyperionGrabber.Core.Hyperion.Protocol.HyperionSchema;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Hyperion.Protocol;

/// <summary>
/// Encodes Hyperion FlatBuffers requests, including the 4-byte big-endian size prefix used on the TCP stream.
/// </summary>
internal static class HyperionRequestWriter
{
    /// <summary>Length of the big-endian message size prefix.</summary>
    public const int SizePrefixLength = sizeof(uint);

    /// <summary>Upper bound of an encoded Clear request.</summary>
    public const int MaxClearLength = SizePrefixLength + MaxStructureLength;

    /// <summary>Upper bound of an encoded Color request.</summary>
    public const int MaxColorLength = SizePrefixLength + MaxStructureLength;

    /// <summary>Upper bound of everything except string/vector payloads: root offset, vtables, tables, padding.</summary>
    private const int MaxStructureLength = 128;

    /// <summary>Returns an upper bound of the encoded size of a Register request.</summary>
    /// <param name="origin">Origin string that will be sent.</param>
    /// <returns>Maximum number of bytes <see cref="WriteRegister"/> writes.</returns>
    public static int GetMaxRegisterLength(string origin)
        => SizePrefixLength + MaxStructureLength + Encoding.UTF8.GetByteCount(origin) + 1;

    /// <summary>Returns an upper bound of the encoded size of an Image request.</summary>
    /// <param name="pixelByteCount">Length of the RGB24 pixel data.</param>
    /// <returns>Maximum number of bytes <see cref="WriteImage"/> writes.</returns>
    public static int GetMaxImageLength(int pixelByteCount)
        => SizePrefixLength + MaxStructureLength + pixelByteCount;

    /// <summary>Encodes <c>Request { Register { origin, priority } }</c>.</summary>
    /// <param name="destination">Buffer of at least <see cref="GetMaxRegisterLength"/> bytes.</param>
    /// <param name="origin">Name shown in Hyperion's priority list.</param>
    /// <param name="priority">Priority to register.</param>
    /// <returns>Number of bytes written, including the size prefix.</returns>
    public static int WriteRegister(Span<byte> destination, string origin, int priority)
    {
        EnsureCapacity(destination, GetMaxRegisterLength(origin));
        var writer = new FlatBufferWriter(destination[SizePrefixLength..]);
        var command = WriteRequest(ref writer, CommandType.Register);

        var registerVTable = writer.WriteVTable(FieldCount.Register);
        var register = writer.BeginTable(registerVTable);
        var originField = writer.ReserveOffset();
        writer.WriteInt32(priority);
        var originString = writer.WriteString(origin);

        writer.PatchOffset(command, register);
        writer.PatchOffset(originField, originString);
        return Finish(destination, ref writer);
    }

    /// <summary>Encodes <c>Request { Image { RawImage { data, width, height }, duration } }</c>.</summary>
    /// <param name="destination">Buffer of at least <see cref="GetMaxImageLength"/> bytes.</param>
    /// <param name="rgb24">Pixel data, 3 bytes per pixel.</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <param name="durationMilliseconds">How long Hyperion shows the image; <see cref="HyperionDefaults.InfiniteDuration"/> for no limit.</param>
    /// <returns>Number of bytes written, including the size prefix.</returns>
    public static int WriteImage(Span<byte> destination, ReadOnlySpan<byte> rgb24, int width, int height, int durationMilliseconds)
    {
        if (width <= 0 || height <= 0 || (long)width * height * 3 != rgb24.Length)
        {
            throw new ArgumentException($"RGB24 data of {rgb24.Length} bytes does not match a {width}x{height} image.", nameof(rgb24));
        }

        EnsureCapacity(destination, GetMaxImageLength(rgb24.Length));
        var writer = new FlatBufferWriter(destination[SizePrefixLength..]);
        var command = WriteRequest(ref writer, CommandType.Image);

        var imageVTable = writer.WriteVTable(FieldCount.Image);
        var image = writer.BeginTable(imageVTable);
        writer.WriteByteSlot(ImageType.RawImage);
        var imageData = writer.ReserveOffset();
        writer.WriteInt32(durationMilliseconds);

        var rawImageVTable = writer.WriteVTable(FieldCount.RawImage);
        var rawImage = writer.BeginTable(rawImageVTable);
        var pixelsField = writer.ReserveOffset();
        writer.WriteInt32(width);
        writer.WriteInt32(height);
        var pixels = writer.WriteByteVector(rgb24);

        writer.PatchOffset(command, image);
        writer.PatchOffset(imageData, rawImage);
        writer.PatchOffset(pixelsField, pixels);
        return Finish(destination, ref writer);
    }

    /// <summary>Encodes <c>Request { Clear { priority } }</c>.</summary>
    /// <param name="destination">Buffer of at least <see cref="MaxClearLength"/> bytes.</param>
    /// <param name="priority">Priority to clear. Never send -1: it clears every source, not just ours.</param>
    /// <returns>Number of bytes written, including the size prefix.</returns>
    public static int WriteClear(Span<byte> destination, int priority)
    {
        EnsureCapacity(destination, MaxClearLength);
        var writer = new FlatBufferWriter(destination[SizePrefixLength..]);
        var command = WriteRequest(ref writer, CommandType.Clear);

        var clearVTable = writer.WriteVTable(FieldCount.Clear);
        var clear = writer.BeginTable(clearVTable);
        writer.WriteInt32(priority);

        writer.PatchOffset(command, clear);
        return Finish(destination, ref writer);
    }

    /// <summary>Encodes <c>Request { Color { data, duration } }</c>.</summary>
    /// <param name="destination">Buffer of at least <see cref="MaxColorLength"/> bytes.</param>
    /// <param name="rgb">Color packed as 0x00RRGGBB.</param>
    /// <param name="durationMilliseconds">How long Hyperion shows the color; <see cref="HyperionDefaults.InfiniteDuration"/> for no limit.</param>
    /// <returns>Number of bytes written, including the size prefix.</returns>
    public static int WriteColor(Span<byte> destination, int rgb, int durationMilliseconds)
    {
        EnsureCapacity(destination, MaxColorLength);
        var writer = new FlatBufferWriter(destination[SizePrefixLength..]);
        var command = WriteRequest(ref writer, CommandType.Color);

        var colorVTable = writer.WriteVTable(FieldCount.Color);
        var color = writer.BeginTable(colorVTable);
        writer.WriteInt32(rgb);
        writer.WriteInt32(durationMilliseconds);

        writer.PatchOffset(command, color);
        return Finish(destination, ref writer);
    }

    /// <summary>Writes the root offset and the <c>Request</c> table; returns the position of its command field.</summary>
    private static int WriteRequest(ref FlatBufferWriter writer, byte commandType)
    {
        var root = writer.ReserveOffset();
        var requestVTable = writer.WriteVTable(FieldCount.Request);
        var request = writer.BeginTable(requestVTable);
        writer.WriteByteSlot(commandType);
        var command = writer.ReserveOffset();
        writer.PatchOffset(root, request);
        return command;
    }

    private static int Finish(Span<byte> destination, ref FlatBufferWriter writer)
    {
        var length = writer.Position;
        BinaryPrimitives.WriteUInt32BigEndian(destination, (uint)length);
        return SizePrefixLength + length;
    }

    private static void EnsureCapacity(Span<byte> destination, int required)
    {
        if (destination.Length < required)
        {
            throw new ArgumentException($"Destination buffer of {destination.Length} bytes is smaller than the required {required} bytes.", nameof(destination));
        }
    }
}
