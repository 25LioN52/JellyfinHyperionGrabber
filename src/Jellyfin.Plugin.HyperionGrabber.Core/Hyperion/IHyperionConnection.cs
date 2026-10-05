using System;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Hyperion;

/// <summary>
/// A connected sink that releases its priority when disposed: <see cref="HyperionClient"/>, or a fake in tests.
/// </summary>
internal interface IHyperionConnection : IHyperionSink, IAsyncDisposable
{
}
