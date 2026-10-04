using System;
using System.IO;
using Google.FlatBuffers;

namespace Jellyfin.Plugin.HyperionGrabber.TestSupport;

/// <summary>
/// Decodes requests and encodes replies with the official Google.FlatBuffers runtime.
/// </summary>
/// <remarks>
/// The accessors below are what <c>flatc --csharp</c> generates for Hyperion's <c>hyperion_request.fbs</c> and
/// <c>hyperion_reply.fbs</c> (vtable offset = 4 + 2 * field index). Requests are first checked with the official
/// verifier, which applies the same structural and alignment checks as Hyperion's C++ <c>VerifyRequestBuffer</c>.
/// </remarks>
public static class OfficialHyperionCodec
{
    /// <summary>Verifies and decodes one request (without the TCP size prefix).</summary>
    /// <param name="flatBuffer">Request bytes.</param>
    /// <returns>The decoded request.</returns>
    /// <exception cref="InvalidDataException">The verifier rejected the bytes or the command is unknown.</exception>
    public static ReceivedRequest DecodeRequest(byte[] flatBuffer)
    {
        var buffer = new ByteBuffer(flatBuffer);
        var verifier = new Verifier(buffer, new Options());
        if (!verifier.VerifyBuffer(null, false, VerifyRequest))
        {
            throw new InvalidDataException("The official FlatBuffers verifier rejected the request.");
        }

        var request = new Table(buffer.GetInt(buffer.Position) + buffer.Position, buffer);
        var commandType = ReadByte(request, 4);
        var command = new Table(request.__indirect(request.__offset(6) + request.bb_pos), buffer);
        return commandType switch
        {
            1 => new ColorRequest(ReadInt(command, 4, -1), ReadInt(command, 6, -1)),
            2 => DecodeImage(command),
            3 => new ClearRequest(ReadInt(command, 4, 0)),
            4 => new RegisterRequest(command.__string(command.__offset(4) + command.bb_pos), ReadInt(command, 6, 0)),
            _ => throw new InvalidDataException($"Unknown command type {commandType}."),
        };
    }

    /// <summary>Encodes <c>Reply { error, video, registered }</c> (without the TCP size prefix).</summary>
    /// <param name="error">Error text or null.</param>
    /// <param name="video">Video mode or -1.</param>
    /// <param name="registered">Confirmed priority or -1.</param>
    /// <returns>Reply bytes.</returns>
    public static byte[] EncodeReply(string? error = null, int video = -1, int registered = -1)
    {
        var builder = new FlatBufferBuilder(64);
        var errorOffset = error is null ? 0 : builder.CreateString(error).Value;
        builder.StartTable(3);
        builder.AddInt(2, registered, -1);
        builder.AddInt(1, video, -1);
        if (error is not null)
        {
            builder.AddOffset(0, errorOffset, 0);
        }

        builder.Finish(builder.EndTable());
        return builder.SizedByteArray();
    }

    /// <summary>Encodes <c>Request { Clear { priority } }</c> with the official builder (harness self-test).</summary>
    /// <param name="priority">Priority to clear.</param>
    /// <returns>Request bytes.</returns>
    public static byte[] EncodeClearRequest(int priority)
    {
        var builder = new FlatBufferBuilder(64);
        builder.StartTable(1);
        builder.AddInt(0, priority, 0);
        var clear = builder.EndTable();
        builder.StartTable(2);
        builder.AddOffset(1, clear, 0);
        builder.AddByte(0, 3, 0);
        builder.Finish(builder.EndTable());
        return builder.SizedByteArray();
    }

    private static ImageRequest DecodeImage(Table image)
    {
        var imageType = ReadByte(image, 4);
        if (imageType != 1)
        {
            throw new InvalidDataException($"Expected RawImage (1) but got image type {imageType}.");
        }

        var duration = ReadInt(image, 8, -1);
        var raw = new Table(image.__indirect(image.__offset(6) + image.bb_pos), image.bb);
        var data = raw.__vector_as_array<byte>(4) ?? [];
        return new ImageRequest(data, ReadInt(raw, 6, -1), ReadInt(raw, 8, -1), duration);
    }

    private static int ReadInt(Table table, int vtableOffset, int defaultValue)
    {
        var offset = table.__offset(vtableOffset);
        return offset != 0 ? table.bb.GetInt(offset + table.bb_pos) : defaultValue;
    }

    private static byte ReadByte(Table table, int vtableOffset)
    {
        var offset = table.__offset(vtableOffset);
        return offset != 0 ? table.bb.Get(offset + table.bb_pos) : (byte)0;
    }

    // Google.FlatBuffers 25.2.10's Verifier.VerifyUnion passes a wrong type id to its callback (seen as 12 for a
    // Clear request built by the official builder), so unions are verified as "type field + referenced table".
    private static bool VerifyRequest(Verifier verifier, uint table)
        => verifier.VerifyTableStart(table)
            && verifier.VerifyField(table, 4, 1, 1, false)
            && VerifyUnionTable(verifier, table, 4, 6, CommandVerifier)
            && verifier.VerifyTableEnd(table);

    private static VerifyTableAction? CommandVerifier(byte type) => type switch
    {
        1 => static (verifier, table) => verifier.VerifyTableStart(table)
            && verifier.VerifyField(table, 4, 4, 4, false)
            && verifier.VerifyField(table, 6, 4, 4, false)
            && verifier.VerifyTableEnd(table),
        2 => static (verifier, table) => verifier.VerifyTableStart(table)
            && verifier.VerifyField(table, 4, 1, 1, false)
            && VerifyUnionTable(verifier, table, 4, 6, ImageVerifier)
            && verifier.VerifyField(table, 8, 4, 4, false)
            && verifier.VerifyTableEnd(table),
        3 => static (verifier, table) => verifier.VerifyTableStart(table)
            && verifier.VerifyField(table, 4, 4, 4, false)
            && verifier.VerifyTableEnd(table),
        4 => static (verifier, table) => verifier.VerifyTableStart(table)
            && verifier.VerifyString(table, 4, true)
            && verifier.VerifyField(table, 6, 4, 4, false)
            && verifier.VerifyTableEnd(table),
        _ => null,
    };

    private static VerifyTableAction? ImageVerifier(byte type) => type switch
    {
        1 => static (verifier, table) => verifier.VerifyTableStart(table)
            && verifier.VerifyVectorOfData(table, 4, 1, false)
            && verifier.VerifyField(table, 6, 4, 4, false)
            && verifier.VerifyField(table, 8, 4, 4, false)
            && verifier.VerifyTableEnd(table),
        _ => null,
    };

    private static bool VerifyUnionTable(Verifier verifier, uint table, short typeOffset, short valueOffset, Func<byte, VerifyTableAction?> verifierFor)
    {
        var action = verifierFor(ReadByte(new Table((int)table, verifier.Buf), typeOffset));
        return action is not null && verifier.VerifyTable(table, valueOffset, action, true);
    }
}
