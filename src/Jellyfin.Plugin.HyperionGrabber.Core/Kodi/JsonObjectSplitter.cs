using System;
using System.IO;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Kodi;

/// <summary>
/// Splits a byte stream of JSON objects sent back to back (Kodi's JSON-RPC over TCP has no delimiter between them)
/// into single objects, by counting braces and brackets outside strings, as Kodi's own server does.
/// </summary>
/// <remarks>Read into <see cref="GetBuffer"/>, call <see cref="Advance"/>, then <see cref="TryRead"/> until it returns
/// <see langword="false"/>. A returned object stays valid until the next <see cref="GetBuffer"/>. Bytes between
/// objects (whitespace) are skipped. Not thread-safe.</remarks>
internal sealed class JsonObjectSplitter
{
    private const int ReadSize = 4096;

    private readonly int _maxLength;
    private byte[] _buffer = new byte[ReadSize];
    private int _start; // First byte not consumed yet.
    private int _end; // End of the received bytes.
    private int _scan; // Next byte to look at.
    private int _objectStart = -1;
    private int _depth;
    private bool _inString;
    private bool _escaped;

    /// <summary>Initializes a new instance of the <see cref="JsonObjectSplitter"/> class.</summary>
    /// <param name="maxLength">Longest object accepted; a longer one throws <see cref="InvalidDataException"/>.</param>
    public JsonObjectSplitter(int maxLength) => _maxLength = maxLength;

    /// <summary>Returns room for the next read, after dropping the objects already returned.</summary>
    /// <returns>A buffer of at least 4 KiB.</returns>
    public Memory<byte> GetBuffer()
    {
        if (_start > 0)
        {
            var remaining = _end - _start;
            Buffer.BlockCopy(_buffer, _start, _buffer, 0, remaining);
            _scan -= _start;
            if (_objectStart >= 0)
            {
                _objectStart -= _start;
            }

            _end = remaining;
            _start = 0;
        }

        if (_buffer.Length - _end < ReadSize)
        {
            Array.Resize(ref _buffer, Math.Max(_buffer.Length * 2, _end + ReadSize));
        }

        return _buffer.AsMemory(_end);
    }

    /// <summary>Records that <paramref name="count"/> bytes were read into the buffer.</summary>
    /// <param name="count">Bytes read.</param>
    public void Advance(int count) => _end += count;

    /// <summary>Returns the next complete object, if one was received.</summary>
    /// <param name="json">The object's bytes.</param>
    /// <returns>Whether an object was complete.</returns>
    /// <exception cref="InvalidDataException">An object is longer than allowed.</exception>
    public bool TryRead(out ReadOnlyMemory<byte> json)
    {
        while (_scan < _end)
        {
            var b = _buffer[_scan++];
            if (_objectStart < 0)
            {
                if (b == (byte)'{')
                {
                    _objectStart = _scan - 1;
                    _depth = 1;
                    _inString = false;
                    _escaped = false;
                }
                else
                {
                    _start = _scan; // Whitespace between objects.
                }

                continue;
            }

            if (_inString)
            {
                if (_escaped)
                {
                    _escaped = false;
                }
                else if (b == (byte)'\\')
                {
                    _escaped = true;
                }
                else if (b == (byte)'"')
                {
                    _inString = false;
                }
            }
            else if (b == (byte)'"')
            {
                _inString = true;
            }
            else if (b is (byte)'{' or (byte)'[')
            {
                _depth++;
            }
            else if (b is (byte)'}' or (byte)']')
            {
                _depth--;
                if (_depth == 0)
                {
                    json = _buffer.AsMemory(_objectStart, _scan - _objectStart);
                    _start = _scan;
                    _objectStart = -1;
                    return true;
                }
            }

            if (_scan - _objectStart > _maxLength)
            {
                throw new InvalidDataException($"Kodi sent a message longer than {_maxLength} bytes.");
            }
        }

        json = default;
        return false;
    }
}
