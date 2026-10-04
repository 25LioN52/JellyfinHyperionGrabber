using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Hyperion.Protocol;

/// <summary>
/// Minimal, allocation-free FlatBuffers writer for the small, frozen Hyperion schema.
/// </summary>
/// <remarks>
/// <para>Unlike the official builder (which writes back to front), this writer lays the buffer out front to back:</para>
/// <list type="bullet">
/// <item>Each table is preceded by its vtable; the table's soffset is therefore positive.</item>
/// <item>Every inline field occupies one 4-byte slot after the soffset, so all fields stay aligned.</item>
/// <item>Referenced objects (sub-tables, strings, vectors) are written after the field that points at them,
/// because FlatBuffers uoffsets are unsigned and must point forward.</item>
/// </list>
/// <para>All positions are relative to the start of the FlatBuffer. The result is validated against the official
/// runtime and verifier in the test suite.</para>
/// </remarks>
internal ref struct FlatBufferWriter
{
    private const int SlotSize = 4;

    private readonly Span<byte> _buffer;
    private int _position;

    /// <summary>
    /// Initializes a new instance of the <see cref="FlatBufferWriter"/> struct.
    /// </summary>
    /// <param name="buffer">Destination; must be large enough for the whole message.</param>
    public FlatBufferWriter(Span<byte> buffer)
    {
        _buffer = buffer;
        _position = 0;
    }

    /// <summary>Gets the number of bytes written so far.</summary>
    public readonly int Position => _position;

    /// <summary>Writes a placeholder uoffset and returns its position for <see cref="PatchOffset"/>.</summary>
    /// <returns>Position of the placeholder.</returns>
    public int ReserveOffset()
    {
        Align(SlotSize);
        var position = _position;
        WriteUInt32(0);
        return position;
    }

    /// <summary>Points a previously reserved uoffset at <paramref name="targetPosition"/>.</summary>
    /// <param name="fieldPosition">Position returned by <see cref="ReserveOffset"/>.</param>
    /// <param name="targetPosition">Position of the referenced table, string or vector.</param>
    public readonly void PatchOffset(int fieldPosition, int targetPosition)
    {
        Debug.Assert(targetPosition > fieldPosition, "FlatBuffers uoffsets must point forward.");
        BinaryPrimitives.WriteUInt32LittleEndian(_buffer.Slice(fieldPosition, sizeof(uint)), (uint)(targetPosition - fieldPosition));
    }

    /// <summary>Writes a vtable in which every one of <paramref name="fieldCount"/> fields is present.</summary>
    /// <param name="fieldCount">Number of fields; the table that follows must write exactly this many slots.</param>
    /// <returns>Position of the vtable.</returns>
    public int WriteVTable(int fieldCount)
    {
        Align(sizeof(ushort));
        var position = _position;
        WriteUInt16((ushort)(4 + (2 * fieldCount))); // vtable size in bytes
        WriteUInt16((ushort)(SlotSize + (SlotSize * fieldCount))); // inline table size in bytes
        for (var field = 0; field < fieldCount; field++)
        {
            WriteUInt16((ushort)(SlotSize + (SlotSize * field))); // field offset from table start
        }

        return position;
    }

    /// <summary>Starts a table that uses the vtable at <paramref name="vtablePosition"/>.</summary>
    /// <param name="vtablePosition">Position returned by <see cref="WriteVTable"/>.</param>
    /// <returns>Position of the table.</returns>
    public int BeginTable(int vtablePosition)
    {
        Align(SlotSize);
        var position = _position;
        WriteInt32(position - vtablePosition); // soffset: vtable = table - soffset
        return position;
    }

    /// <summary>Writes a one-byte field (ubyte / union type) into a 4-byte slot.</summary>
    /// <param name="value">Field value.</param>
    public void WriteByteSlot(byte value)
    {
        _buffer[_position] = value;
        _buffer.Slice(_position + 1, SlotSize - 1).Clear();
        _position += SlotSize;
    }

    /// <summary>Writes a 32-bit integer field into a 4-byte slot.</summary>
    /// <param name="value">Field value.</param>
    public void WriteInt32(int value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(_buffer.Slice(_position, sizeof(int)), value);
        _position += sizeof(int);
    }

    /// <summary>Writes a null-terminated UTF-8 string object.</summary>
    /// <param name="value">String to write.</param>
    /// <returns>Position of the string object (its length prefix).</returns>
    public int WriteString(string value)
    {
        Align(SlotSize);
        var position = _position;
        var byteCount = Encoding.UTF8.GetByteCount(value);
        WriteUInt32((uint)byteCount);
        _position += Encoding.UTF8.GetBytes(value, _buffer.Slice(_position, byteCount));
        _buffer[_position++] = 0;
        return position;
    }

    /// <summary>Writes a <c>[ubyte]</c> vector object.</summary>
    /// <param name="data">Vector contents.</param>
    /// <returns>Position of the vector object (its length prefix).</returns>
    public int WriteByteVector(ReadOnlySpan<byte> data)
    {
        Align(SlotSize);
        var position = _position;
        WriteUInt32((uint)data.Length);
        data.CopyTo(_buffer.Slice(_position, data.Length));
        _position += data.Length;
        return position;
    }

    /// <summary>Pads with zero bytes until <see cref="Position"/> is a multiple of <paramref name="alignment"/>.</summary>
    /// <param name="alignment">Required alignment (power of two).</param>
    public void Align(int alignment)
    {
        var padding = (alignment - (_position % alignment)) % alignment;
        _buffer.Slice(_position, padding).Clear();
        _position += padding;
    }

    private void WriteUInt16(ushort value)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(_buffer.Slice(_position, sizeof(ushort)), value);
        _position += sizeof(ushort);
    }

    private void WriteUInt32(uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(_buffer.Slice(_position, sizeof(uint)), value);
        _position += sizeof(uint);
    }
}
