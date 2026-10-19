using System;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.HyperionGrabber.Core.Hyperion.Protocol;
using Microsoft.Extensions.Logging;
using static System.FormattableString;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Hyperion;

/// <summary>
/// A registered FlatBuffers connection to Hyperion.ng or HyperHDR.
/// </summary>
/// <remarks>
/// <para>Create with <see cref="ConnectAsync(HyperionClientOptions, ILogger, TimeProvider, CancellationToken)"/>: the
/// returned client is connected and its priority is registered.</para>
/// <para>Sends are serialized internally and may be called concurrently, also while the client is being disposed (they
/// then fail with <see cref="HyperionConnectionException"/>). A send's cancellation token applies while it waits for its
/// turn; once a message is being written it completes, or fails within <see cref="HyperionClientOptions.WriteTimeout"/>,
/// so the stream never contains half a message.</para>
/// <para>Replies are drained continuously so the server never blocks on us.</para>
/// <para>When Hyperion answers with <c>registered = -1</c> (an instance no longer has our priority, for example after a
/// clear from Hyperion.ng's remote control), the next image or color is preceded by a new Register, at most once a
/// second. A Clear never registers first.</para>
/// <para>The client never reconnects by itself. When <see cref="IsConnected"/> turns <see langword="false"/>, dispose it and
/// connect again; retry policy belongs to the caller.</para>
/// <para>Disposing clears the priority (Hyperion and HyperHDR also clear it when the socket closes).</para>
/// </remarks>
public sealed partial class HyperionClient : IHyperionConnection
{
    private const int MaxReplyLength = 64 * 1024;
    private static readonly TimeSpan ClearOnDisposeTimeout = TimeSpan.FromSeconds(1);

    // Shortest time between two Registers: Hyperion also asks again for images that were already on their way.
    private static readonly TimeSpan MinRegisterInterval = TimeSpan.FromSeconds(1);

    private readonly HyperionClientOptions _options;
    private readonly ILogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly TcpClient _tcpClient;
    private readonly NetworkStream _stream;
    [SuppressMessage(
        "Usage",
        "CA2213:Disposable fields should be disposed",
        Justification = "Deliberately never disposed: concurrent senders may still wait on or release it after DisposeAsync. SemaphoreSlim only owns an unmanaged handle when AvailableWaitHandle is used, which it is not.")]
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly TaskCompletionSource<HyperionReply> _registration = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task _receiveLoop = Task.CompletedTask;
    private byte[] _writeBuffer = new byte[1024];
    private CancellationTokenSource _writeTimeout = new(); // reused for every write; guarded by _writeLock
    private long _lastRegisterTimestamp; // guarded by _writeLock once connected
    private volatile bool _closed;
    private volatile string? _lastServerError;
    private volatile bool _registered;
    private volatile bool _registrationRequested;
    private int _disposed;

    private HyperionClient(HyperionClientOptions options, ILogger logger, TimeProvider timeProvider, TcpClient tcpClient)
    {
        _options = options;
        _logger = logger;
        _timeProvider = timeProvider;
        _tcpClient = tcpClient;
        _stream = tcpClient.GetStream();
    }

    private delegate int MessageEncoder<TState>(Span<byte> destination, TState state);

    /// <summary>Gets the options this client was created with.</summary>
    public HyperionClientOptions Options => _options;

    /// <summary>Gets a value indicating whether the connection is still usable.</summary>
    public bool IsConnected => !_closed;

    /// <summary>Gets the most recent error text reported by the server, if any.</summary>
    public string? LastServerError => _lastServerError;

    /// <summary>Gets a value indicating whether Hyperion asked for a registration that has not been sent yet.</summary>
    internal bool IsRegistrationRequested => _registrationRequested;

    /// <summary>
    /// Connects to the server and registers <see cref="HyperionClientOptions.Priority"/>.
    /// </summary>
    /// <param name="options">Connection options; validated before connecting.</param>
    /// <param name="logger">Logger for connection lifecycle events.</param>
    /// <param name="cancellationToken">Cancels connecting and registering.</param>
    /// <returns>A connected, registered client.</returns>
    /// <exception cref="ArgumentException">The options are invalid.</exception>
    /// <exception cref="HyperionConnectionException">The server is unreachable, too slow, or not a FlatBuffers server.</exception>
    /// <exception cref="HyperionProtocolException">The server rejected the registration (for example the priority).</exception>
    public static Task<HyperionClient> ConnectAsync(HyperionClientOptions options, ILogger logger, CancellationToken cancellationToken)
        => ConnectAsync(options, logger, TimeProvider.System, cancellationToken);

    /// <summary>
    /// Connects to the server and registers <see cref="HyperionClientOptions.Priority"/>.
    /// </summary>
    /// <param name="options">Connection options; validated before connecting.</param>
    /// <param name="logger">Logger for connection lifecycle events.</param>
    /// <param name="timeProvider">Clock that paces registering again when Hyperion asks for it.</param>
    /// <param name="cancellationToken">Cancels connecting and registering.</param>
    /// <returns>A connected, registered client.</returns>
    /// <exception cref="ArgumentException">The options are invalid.</exception>
    /// <exception cref="HyperionConnectionException">The server is unreachable, too slow, or not a FlatBuffers server.</exception>
    /// <exception cref="HyperionProtocolException">The server rejected the registration (for example the priority).</exception>
    public static async Task<HyperionClient> ConnectAsync(HyperionClientOptions options, ILogger logger, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(timeProvider);
        options.Validate();

        var tcpClient = new TcpClient { NoDelay = true };
        try
        {
            await ConnectSocketAsync(tcpClient, options, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            tcpClient.Dispose();
            throw;
        }

        var client = new HyperionClient(options, logger, timeProvider, tcpClient);
        try
        {
            client.StartReceiving();
            await client.RegisterAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await client.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        Log.Connected(logger, options.Host, options.Port, options.Priority);
        return client;
    }

    /// <inheritdoc />
    public ValueTask SendImageAsync(ReadOnlyMemory<byte> rgb24, int width, int height, CancellationToken cancellationToken)
    {
        if (width is <= 0 or > HyperionDefaults.MaxImageDimension
            || height is <= 0 or > HyperionDefaults.MaxImageDimension
            || (long)width * height * 3 != rgb24.Length)
        {
            throw new ArgumentException(
                Invariant($"Expected a {width}x{height} RGB24 image (edges 1-{HyperionDefaults.MaxImageDimension} px) but got {rgb24.Length} bytes."),
                nameof(rgb24));
        }

        return SendAsync(
            HyperionRequestWriter.GetMaxImageLength(rgb24.Length),
            (Rgb24: rgb24, Width: width, Height: height),
            static (destination, image) => HyperionRequestWriter.WriteImage(destination, image.Rgb24.Span, image.Width, image.Height, HyperionDefaults.InfiniteDuration),
            cancellationToken,
            registerIfRequested: true);
    }

    /// <summary>
    /// Shows a solid color at this client's priority until replaced or cleared.
    /// </summary>
    /// <param name="red">Red component.</param>
    /// <param name="green">Green component.</param>
    /// <param name="blue">Blue component.</param>
    /// <param name="cancellationToken">Cancels the send.</param>
    /// <returns>A task that completes when the request has been written.</returns>
    public ValueTask SendColorAsync(byte red, byte green, byte blue, CancellationToken cancellationToken)
        => SendAsync(
            HyperionRequestWriter.MaxColorLength,
            (red << 16) | (green << 8) | blue,
            static (destination, rgb) => HyperionRequestWriter.WriteColor(destination, rgb, HyperionDefaults.InfiniteDuration),
            cancellationToken,
            registerIfRequested: true);

    /// <inheritdoc />
    public ValueTask ClearAsync(CancellationToken cancellationToken)
        => SendAsync(
            HyperionRequestWriter.MaxClearLength,
            _options.Priority,
            static (destination, priority) => HyperionRequestWriter.WriteClear(destination, priority),
            cancellationToken);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (_registered && !_closed)
        {
            try
            {
                using var timeout = new CancellationTokenSource(ClearOnDisposeTimeout);
                await ClearAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HyperionConnectionException or OperationCanceledException)
            {
                // Best effort: the server clears our priority when the socket closes anyway.
            }
        }

        // Graceful close: send FIN and keep draining replies until the server closes. Closing a socket with unread
        // replies makes Windows send RST, and the server may then drop our last messages (the Clear) unread.
        var wasOpen = !_closed;
        _closed = true;
        if (wasOpen)
        {
            try
            {
                _tcpClient.Client.Shutdown(SocketShutdown.Send);
                await _receiveLoop.WaitAsync(ClearOnDisposeTimeout).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException or TimeoutException)
            {
                // Fall through to the hard close below.
            }
        }

        // Closing the socket ends the receive loop and aborts a write that is still in progress.
        await _lifetime.CancelAsync().ConfigureAwait(false);
        await _stream.DisposeAsync().ConfigureAwait(false);
        _tcpClient.Dispose();
        await _receiveLoop.ConfigureAwait(false);

        // Wait until no sender is inside the lock before disposing what it uses; later senders see _closed and throw.
        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            _writeTimeout.Dispose();
        }
        finally
        {
            _writeLock.Release();
        }

        _lifetime.Dispose();

        if (_registered)
        {
            Log.Disconnected(_logger, _options.Host, _options.Port);
        }
    }

    private static async Task ConnectSocketAsync(TcpClient tcpClient, HyperionClientOptions options, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.ConnectTimeout);
        try
        {
            await tcpClient.ConnectAsync(options.Host, options.Port, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new HyperionConnectionException(
                Invariant($"Timed out after {options.ConnectTimeout.TotalMilliseconds:0} ms connecting to {options.Host}:{options.Port}."),
                ex);
        }
        catch (SocketException ex)
        {
            throw new HyperionConnectionException(Invariant($"Could not connect to {options.Host}:{options.Port}: {ex.Message}"), ex);
        }
    }

    private async Task RegisterAsync(CancellationToken cancellationToken)
    {
        _lastRegisterTimestamp = _timeProvider.GetTimestamp();
        await SendAsync(
            HyperionRequestWriter.GetMaxRegisterLength(_options.Origin),
            (_options.Origin, _options.Priority),
            static (destination, registration) => HyperionRequestWriter.WriteRegister(destination, registration.Origin, registration.Priority),
            cancellationToken).ConfigureAwait(false);

        HyperionReply reply;
        try
        {
            reply = await _registration.Task.WaitAsync(_options.ReplyTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            throw new HyperionConnectionException(
                Invariant($"{_options.Host}:{_options.Port} accepted the connection but did not confirm the registration within {_options.ReplyTimeout.TotalMilliseconds:0} ms. Is this the FlatBuffers port of Hyperion.ng or HyperHDR?"),
                ex);
        }

        if (reply.IsError)
        {
            throw new HyperionProtocolException(Invariant($"Hyperion rejected the registration: {reply.Error}"));
        }

        if (reply.Registered != _options.Priority)
        {
            Log.UnexpectedRegistration(_logger, reply.Registered, _options.Priority);
        }

        _registered = true;
    }

    private async ValueTask SendAsync<TState>(
        int maxLength,
        TState state,
        MessageEncoder<TState> encode,
        CancellationToken cancellationToken,
        bool registerIfRequested = false)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_closed)
            {
                throw new HyperionConnectionException(Invariant($"The connection to Hyperion at {_options.Host}:{_options.Port} is closed."));
            }

            if (registerIfRequested && _registrationRequested)
            {
                await RegisterAgainLockedAsync().ConfigureAwait(false);
            }

            EnsureWriteBuffer(maxLength);
            var length = encode(_writeBuffer, state);
            await WriteLockedAsync(_writeBuffer.AsMemory(0, length)).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Sends Register again unless the last one is less than <see cref="MinRegisterInterval"/> ago. The caller holds
    /// <see cref="_writeLock"/>.
    /// </summary>
    private async ValueTask RegisterAgainLockedAsync()
    {
        var now = _timeProvider.GetTimestamp();
        if (_timeProvider.GetElapsedTime(_lastRegisterTimestamp, now) < MinRegisterInterval)
        {
            return; // Keep the request; a later send registers.
        }

        // Reset before writing, so a request that arrives meanwhile is kept for the next send.
        _registrationRequested = false;
        _lastRegisterTimestamp = now;
        EnsureWriteBuffer(HyperionRequestWriter.GetMaxRegisterLength(_options.Origin));
        var length = HyperionRequestWriter.WriteRegister(_writeBuffer, _options.Origin, _options.Priority);
        await WriteLockedAsync(_writeBuffer.AsMemory(0, length)).ConfigureAwait(false);
        Log.RegisteredAgain(_logger, _options.Priority);
    }

    /// <summary>Grows the write buffer if needed. The caller holds <see cref="_writeLock"/>.</summary>
    private void EnsureWriteBuffer(int length)
    {
        if (_writeBuffer.Length < length)
        {
            _writeBuffer = new byte[length];
        }
    }

    /// <summary>Writes one complete message. The caller holds <see cref="_writeLock"/>.</summary>
    private async ValueTask WriteLockedAsync(ReadOnlyMemory<byte> message)
    {
        // Reuse one timeout source instead of allocating a source and a timer per frame.
        if (!_writeTimeout.TryReset())
        {
            _writeTimeout.Dispose();
            _writeTimeout = new CancellationTokenSource();
        }

        _writeTimeout.CancelAfter(_options.WriteTimeout);
        try
        {
            await _stream.WriteAsync(message, _writeTimeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex)
        {
            _closed = true;
            throw new HyperionConnectionException(
                Invariant($"Timed out after {_options.WriteTimeout.TotalMilliseconds:0} ms writing to Hyperion; the connection was closed."),
                ex);
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or InvalidOperationException)
        {
            _closed = true;
            throw new HyperionConnectionException(Invariant($"Lost the connection to Hyperion at {_options.Host}:{_options.Port}."), ex);
        }
        finally
        {
            _writeTimeout.CancelAfter(Timeout.InfiniteTimeSpan);
        }
    }

    private void StartReceiving() => _receiveLoop = ReceiveLoopAsync(_lifetime.Token);

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var header = new byte[HyperionRequestWriter.SizePrefixLength];
        var body = new byte[256];
        Exception? failure = null;
        try
        {
            while (true)
            {
                await _stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
                var length = BinaryPrimitives.ReadUInt32BigEndian(header);
                if (length is 0 or > MaxReplyLength)
                {
                    throw new HyperionProtocolException(Invariant($"Hyperion sent a reply of {length} bytes; expected 1-{MaxReplyLength}."));
                }

                if (body.Length < length)
                {
                    body = new byte[length];
                }

                await _stream.ReadExactlyAsync(body.AsMemory(0, (int)length), cancellationToken).ConfigureAwait(false);
                HandleReply(HyperionReplyReader.Parse(body.AsSpan(0, (int)length)));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Disposing.
        }
        catch (EndOfStreamException ex)
        {
            failure = ex;

            // Only once registered: before that ConnectAsync throws, and its caller reports the failure.
            if (!_closed && _registered)
            {
                Log.ServerClosed(_logger, _options.Host, _options.Port);
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or InvalidOperationException or HyperionProtocolException)
        {
            failure = ex;
            if (!_closed && _registered)
            {
                Log.ConnectionLost(_logger, _options.Host, _options.Port, ex.Message);
            }
        }
        finally
        {
            _closed = true;
            const string Message = "The connection closed before Hyperion confirmed the registration.";
            _registration.TrySetException(failure is null ? new HyperionConnectionException(Message) : new HyperionConnectionException(Message, failure));

            // If the registration already timed out nobody awaits it any more; mark the fault as observed.
            _ = _registration.Task.Exception;
        }
    }

    private void HandleReply(HyperionReply reply)
    {
        if (reply.IsError)
        {
            var isNewError = !string.Equals(reply.Error, _lastServerError, StringComparison.Ordinal);
            _lastServerError = reply.Error;
            if (!_registration.TrySetResult(reply) && isNewError)
            {
                Log.ServerError(_logger, reply.Error!);
            }

            return;
        }

        if (reply.IsRegistration)
        {
            _registration.TrySetResult(reply);
            return;
        }

        // registered = -1 without an error. Hyperion.ng sends it when an instance gets an image for a priority it does
        // not have (its remote control cleared it) and answers like this once we cleared our own priority; HyperHDR
        // answers Color and Clear like this. Registering again before the next image is right or harmless in each case.
        if (_registered)
        {
            _registrationRequested = true;
        }
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Connected to Hyperion at {Host}:{Port} with priority {Priority}")]
        public static partial void Connected(ILogger logger, string host, int port, int priority);

        [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Disconnected from Hyperion at {Host}:{Port}")]
        public static partial void Disconnected(ILogger logger, string host, int port);

        [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "Hyperion at {Host}:{Port} closed the connection")]
        public static partial void ServerClosed(ILogger logger, string host, int port);

        [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "Lost the connection to Hyperion at {Host}:{Port}: {Reason}")]
        public static partial void ConnectionLost(ILogger logger, string host, int port, string reason);

        [LoggerMessage(EventId = 5, Level = LogLevel.Warning, Message = "Hyperion reported an error: {Error}")]
        public static partial void ServerError(ILogger logger, string error);

        [LoggerMessage(EventId = 6, Level = LogLevel.Warning, Message = "Hyperion confirmed priority {Confirmed} but {Requested} was requested")]
        public static partial void UnexpectedRegistration(ILogger logger, int confirmed, int requested);

        [LoggerMessage(EventId = 7, Level = LogLevel.Debug, Message = "Hyperion no longer had priority {Priority}; registered it again")]
        public static partial void RegisteredAgain(ILogger logger, int priority);
    }
}
