using System;
using static Jellyfin.Plugin.HyperionGrabber.Core.Hyperion.Protocol.HyperionSchema;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Hyperion.Protocol;

/// <summary>
/// Decodes <c>Reply</c> messages: <c>table Reply { error:string; video:int = -1; registered:int = -1; }</c>.
/// </summary>
internal static class HyperionReplyReader
{
    /// <summary>Decodes one reply.</summary>
    /// <param name="flatBuffer">Message body without the TCP size prefix.</param>
    /// <returns>The decoded reply.</returns>
    /// <exception cref="HyperionProtocolException">The data is not a valid reply.</exception>
    public static HyperionReply Parse(ReadOnlySpan<byte> flatBuffer)
    {
        var reader = new FlatBufferReader(flatBuffer);
        var table = reader.GetRootTable();
        return new HyperionReply(
            reader.GetString(table, ReplyField.Error),
            reader.GetInt32(table, ReplyField.Video, -1),
            reader.GetInt32(table, ReplyField.Registered, -1));
    }
}
