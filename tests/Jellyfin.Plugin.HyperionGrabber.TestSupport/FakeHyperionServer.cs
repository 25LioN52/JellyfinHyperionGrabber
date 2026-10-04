using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.HyperionGrabber.TestSupport;

/// <summary>How the fake server answers a Register request.</summary>
public enum RegistrationMode
{
    /// <summary>Reply with <c>registered = priority</c>, like Hyperion.ng and HyperHDR.</summary>
    Accept,

    /// <summary>Reply with an error, like Hyperion.ng for a priority outside 100-199.</summary>
    Reject,

    /// <summary>Never reply, like a server on the wrong port.</summary>
    Silent,
}

/// <summary>
/// In-process Hyperion FlatBuffers server on 127.0.0.1 that records every request it receives.
/// </summary>
public sealed class FakeHyperionServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly Channel<ReceivedRequest> _requests = Channel.CreateUnbounded<ReceivedRequest>();
    private readonly ConcurrentBag<TcpClient> _connections = [];
    private readonly ConcurrentBag<Task> _handlers = [];
    private readonly Task _acceptLoop;
    private int _connectionCount;

    private FakeHyperionServer(RegistrationMode registration, string rejectionMessage)
    {
        Registration = registration;
        RejectionMessage = rejectionMessage;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        _acceptLoop = AcceptLoopAsync(_stop.Token);
    }

    /// <summary>Gets the registration behaviour.</summary>
    public RegistrationMode Registration { get; }

    /// <summary>Gets the error text sent in <see cref="RegistrationMode.Reject"/> mode.</summary>
    public string RejectionMessage { get; }

    /// <summary>Gets the host to connect to.</summary>
    public string Host => ((IPEndPoint)_listener.LocalEndpoint).Address.ToString();

    /// <summary>Gets the port the server listens on.</summary>
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>Gets the number of accepted connections.</summary>
    public int ConnectionCount => Volatile.Read(ref _connectionCount);

    /// <summary>Starts a server on a free loopback port.</summary>
    /// <param name="registration">Registration behaviour.</param>
    /// <param name="rejectionMessage">Error text for <see cref="RegistrationMode.Reject"/>.</param>
    /// <returns>The running server.</returns>
    public static FakeHyperionServer Start(
        RegistrationMode registration = RegistrationMode.Accept,
        string rejectionMessage = "The priority 150 is not in the priority range between 100 and 199")
        => new(registration, rejectionMessage);

    /// <summary>Waits for the next request.</summary>
    /// <param name="timeout">Maximum wait; defaults to 5 seconds.</param>
    /// <returns>The request.</returns>
    public async Task<ReceivedRequest> NextRequestAsync(TimeSpan? timeout = null)
    {
        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(5));
        return await _requests.Reader.ReadAsync(cts.Token);
    }

    /// <summary>Waits for the next request and checks its type.</summary>
    /// <typeparam name="T">Expected request type.</typeparam>
    /// <param name="timeout">Maximum wait; defaults to 5 seconds.</param>
    /// <returns>The request.</returns>
    public async Task<T> NextRequestAsync<T>(TimeSpan? timeout = null)
        where T : ReceivedRequest
    {
        var request = await NextRequestAsync(timeout);
        return request as T ?? throw new InvalidOperationException($"Expected {typeof(T).Name} but received {request}.");
    }

    /// <summary>Returns a request if one is already queued.</summary>
    /// <param name="request">The request.</param>
    /// <returns>Whether a request was available.</returns>
    public bool TryReadRequest(out ReceivedRequest? request) => _requests.Reader.TryRead(out request);

    /// <summary>Closes every client connection from the server side.</summary>
    public void DropConnections()
    {
        foreach (var connection in _connections)
        {
            connection.Dispose();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        DropConnections();
        await _acceptLoop;
        await Task.WhenAll(_handlers);
        _stop.Dispose();
    }

    private static byte[] WithSizePrefix(byte[] flatBuffer)
    {
        var message = new byte[flatBuffer.Length + 4];
        BinaryPrimitives.WriteUInt32BigEndian(message, (uint)flatBuffer.Length);
        flatBuffer.CopyTo(message, 4);
        return message;
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(cancellationToken);
                Interlocked.Increment(ref _connectionCount);
                _connections.Add(client);
                _handlers.Add(HandleClientAsync(client, cancellationToken));
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
        {
            // Stopping.
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        await Task.Yield();
        try
        {
            var stream = client.GetStream();
            var header = new byte[4];
            while (!cancellationToken.IsCancellationRequested)
            {
                await stream.ReadExactlyAsync(header, cancellationToken);
                var body = new byte[BinaryPrimitives.ReadUInt32BigEndian(header)];
                await stream.ReadExactlyAsync(body, cancellationToken);

                ReceivedRequest request;
                try
                {
                    request = OfficialHyperionCodec.DecodeRequest(body);
                }
                catch (InvalidDataException ex)
                {
                    request = new InvalidRequest(ex.Message);
                }

                await _requests.Writer.WriteAsync(request, cancellationToken);
                var reply = CreateReply(request);
                if (reply is not null)
                {
                    await stream.WriteAsync(WithSizePrefix(reply), cancellationToken);
                }
            }
        }
        catch (Exception ex) when (ex is EndOfStreamException or IOException or ObjectDisposedException or OperationCanceledException or InvalidOperationException)
        {
            // Client went away or server is stopping.
        }
        finally
        {
            client.Dispose();
        }
    }

    private byte[]? CreateReply(ReceivedRequest request) => request switch
    {
        RegisterRequest register => Registration switch
        {
            RegistrationMode.Accept => OfficialHyperionCodec.EncodeReply(registered: register.Priority),
            RegistrationMode.Reject => OfficialHyperionCodec.EncodeReply(error: RejectionMessage),
            _ => null,
        },
        InvalidRequest invalid => OfficialHyperionCodec.EncodeReply(error: invalid.Reason),
        _ => OfficialHyperionCodec.EncodeReply(), // Hyperion.ng answers every command with an empty success reply.
    };
}
