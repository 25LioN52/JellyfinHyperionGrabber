using System;
using System.Buffers.Binary;
using System.Text;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Hyperion.Protocol;

/// <summary>
/// Bounds-checked FlatBuffers reader for data received from the network.
/// </summary>
/// <remarks>
/// Every read is validated against the buffer; malformed input raises <see cref="HyperionProtocolException"/> and
/// never an out-of-range or overflow exception.
/// </remarks>
internal readonly ref struct FlatBufferReader
{
    private readonly ReadOnlySpan<byte> _buffer;

    /// <summary>
    /// Initializes a new instance of the <see cref="FlatBufferReader"/> struct.
    /// </summary>
    /// <param name="buffer">A complete FlatBuffer (without the TCP size prefix).</param>
    public FlatBufferReader(ReadOnlySpan<byte> buffer)
    {
        _buffer = buffer;
    }

    /// <summary>Resolves the root table position.</summary>
    /// <returns>Position of the root table.</returns>
    public int GetRootTable() => ResolveTable(0);

    /// <summary>Reads an <c>int</c> field, or <paramref name="defaultValue"/> when it is absent.</summary>
    /// <param name="table">Table position.</param>
    /// <param name="fieldIndex">Field index in declaration order.</param>
    /// <param name="defaultValue">Schema default.</param>
    /// <returns>The field value.</returns>
    public int GetInt32(int table, int fieldIndex, int defaultValue)
    {
        var fieldOffset = GetFieldOffset(table, fieldIndex);
        return fieldOffset == 0 ? defaultValue : ReadInt32(table + fieldOffset);
    }

    /// <summary>Reads a <c>string</c> field, or <see langword="null"/> when it is absent.</summary>
    /// <param name="table">Table position.</param>
    /// <param name="fieldIndex">Field index in declaration order.</param>
    /// <returns>The decoded string.</returns>
    public string? GetString(int table, int fieldIndex)
    {
        var fieldOffset = GetFieldOffset(table, fieldIndex);
        if (fieldOffset == 0)
        {
            return null;
        }

        var start = ResolveOffset(table + fieldOffset);
        var length = ReadUInt32(start);
        EnsureRange(start + 4L, length + 1L); // FlatBuffers strings are null-terminated
        return Encoding.UTF8.GetString(_buffer.Slice(start + 4, (int)length));
    }

    private int GetFieldOffset(int table, int fieldIndex)
    {
        var vtable = table - (long)ReadInt32(table);
        EnsureRange(vtable, 4);
        var vtableSize = ReadUInt16((int)vtable);
        var slot = 4 + (2 * fieldIndex);
        if (slot + 2 > vtableSize)
        {
            return 0;
        }

        EnsureRange(vtable + slot, 2);
        return ReadUInt16((int)vtable + slot);
    }

    private int ResolveTable(int position)
    {
        var table = ResolveOffset(position);
        EnsureRange(table, 4);
        return table;
    }

    private int ResolveOffset(int position)
    {
        var target = position + (long)ReadUInt32(position);
        EnsureRange(target, 4);
        return (int)target;
    }

    private int ReadInt32(int position)
    {
        EnsureRange(position, 4);
        return BinaryPrimitives.ReadInt32LittleEndian(_buffer.Slice(position, 4));
    }

    private uint ReadUInt32(int position)
    {
        EnsureRange(position, 4);
        return BinaryPrimitives.ReadUInt32LittleEndian(_buffer.Slice(position, 4));
    }

    private ushort ReadUInt16(int position)
    {
        EnsureRange(position, 2);
        return BinaryPrimitives.ReadUInt16LittleEndian(_buffer.Slice(position, 2));
    }

    private void EnsureRange(long position, long length)
    {
        if (position < 0 || length < 0 || position + length > _buffer.Length)
        {
            throw new HyperionProtocolException($"Malformed FlatBuffer: {length} bytes at offset {position} exceed the {_buffer.Length}-byte message.");
        }
    }
}
