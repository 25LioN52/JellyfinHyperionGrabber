namespace Jellyfin.Plugin.HyperionGrabber.TestSupport;

/// <summary>A request decoded by <see cref="FakeHyperionServer"/>.</summary>
public abstract record ReceivedRequest;

/// <summary><c>Register { origin, priority }</c>.</summary>
public sealed record RegisterRequest(string Origin, int Priority) : ReceivedRequest;

/// <summary><c>Image { RawImage { data, width, height }, duration }</c>.</summary>
public sealed record ImageRequest(byte[] Data, int Width, int Height, int Duration) : ReceivedRequest;

/// <summary><c>Clear { priority }</c>.</summary>
public sealed record ClearRequest(int Priority) : ReceivedRequest;

/// <summary><c>Color { data, duration }</c>.</summary>
public sealed record ColorRequest(int Rgb, int Duration) : ReceivedRequest;

/// <summary>Bytes the official FlatBuffers verifier rejected; any occurrence is a bug in our encoder.</summary>
public sealed record InvalidRequest(string Reason) : ReceivedRequest;
