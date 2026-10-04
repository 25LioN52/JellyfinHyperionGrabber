namespace Jellyfin.Plugin.HyperionGrabber.Core.Hyperion.Protocol;

/// <summary>
/// Wire-level identifiers of the Hyperion FlatBuffers schema.
/// </summary>
/// <remarks>
/// Hyperion.ng (<c>libsrc/flatbufserver/hyperion_request.fbs</c>, namespace <c>hyperionnet</c>) and HyperHDR
/// (<c>include/flatbuffers/parser/hyperhdr_request.fbs</c>, namespace <c>hyperhdrnet</c>) use the same tables,
/// field order and union order. FlatBuffers does not put namespaces on the wire, so one encoder serves both.
/// Field indexes below are declaration order; a union field occupies two indexes (type, then value).
/// See docs/development/hyperion-protocol.md before changing anything here.
/// </remarks>
internal static class HyperionSchema
{
    /// <summary>Values of the <c>Command</c> union type field.</summary>
    internal static class CommandType
    {
        public const byte Color = 1;
        public const byte Image = 2;
        public const byte Clear = 3;
        public const byte Register = 4;
    }

    /// <summary>Values of the <c>ImageType</c> union type field.</summary>
    internal static class ImageType
    {
        public const byte RawImage = 1;
        public const byte Nv12Image = 2;
    }

    /// <summary>Number of fields (vtable slots) per table.</summary>
    internal static class FieldCount
    {
        public const int Request = 2; // command_type, command
        public const int Register = 2; // origin, priority
        public const int Image = 3; // data_type, data, duration
        public const int RawImage = 3; // data, width, height
        public const int Clear = 1; // priority
        public const int Color = 2; // data, duration
    }

    /// <summary>Field indexes of the <c>Reply</c> table.</summary>
    internal static class ReplyField
    {
        public const int Error = 0;
        public const int Video = 1;
        public const int Registered = 2;
    }
}
