using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Kodi;

/// <summary>
/// One JSON-RPC connection to Kodi over raw TCP (port 9090 by default): requests with ids, answers matched by id,
/// notifications (<c>Player.OnPause</c>, ...) passed to a callback.
/// </summary>
/// <remarks>Kodi's TCP server needs no authentication, but only accepts connections from other machines when "Allow
/// remote control from applications on other systems" is on. This class only sends what its caller asks for.</remarks>
internal sealed class KodiJsonRpcConnection : IAsyncDisposable
{
    /// <summary>Longest message accepted from Kodi; library notifications are a few KiB.</summary>
    internal const int MaxMessageLength = 1 << 20;

    private readonly TcpClient _tcpClient;
    private readonly NetworkStream _stream;
    private readonly Action<string> _onNotification;
    private readonly string _endpoint;
    private readonly Lock _gate = new();
    private readonly Dictionary<int, TaskCompletionSource<JsonElement>> _pending = [];
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private Task _receiveLoop = Task.CompletedTask;
    private bool _closed; // Guarded by _gate.
    private int _nextId;
    private int _disposed;

    private KodiJsonRpcConnection(TcpClient tcpClient, string endpoint, Action<string> onNotification)
    {
        _tcpClient = tcpClient;
        _stream = tcpClient.GetStream();
        _endpoint = endpoint;
        _onNotification = onNotification;
    }

    /// <summary>Connects to Kodi.</summary>
    /// <param name="host">Kodi's address.</param>
    /// <param name="port">Kodi's JSON-RPC TCP port.</param>
    /// <param name="timeout">How long connecting may take.</param>
    /// <param name="onNotification">Called with the method name of every notification, on the receive task; must not block.</param>
    /// <param name="timeProvider">Clock for the timeout.</param>
    /// <param name="cancellationToken">Stops connecting.</param>
    /// <returns>The connection.</returns>
    /// <exception cref="KodiException">Kodi could not be reached.</exception>
    public static async Task<KodiJsonRpcConnection> ConnectAsync(
        string host,
        int port,
        TimeSpan timeout,
        Action<string> onNotification,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var endpoint = string.Create(CultureInfo.InvariantCulture, $"{host}:{port}");
        var tcpClient = new TcpClient { NoDelay = true };
        try
        {
            using var deadline = new CancellationTokenSource(timeout, timeProvider);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
            await tcpClient.ConnectAsync(host, port, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            tcpClient.Dispose();
            throw new KodiException($"Timed out connecting to Kodi at {endpoint}.", ex);
        }
        catch (SocketException ex)
        {
            tcpClient.Dispose();
            throw new KodiException($"Could not connect to Kodi at {endpoint}: {ex.Message}", ex);
        }
        catch
        {
            tcpClient.Dispose();
            throw;
        }

        var connection = new KodiJsonRpcConnection(tcpClient, endpoint, onNotification);
        connection._receiveLoop = connection.ReceiveAsync(connection._lifetime.Token);
        return connection;
    }

    /// <summary>Calls a method and waits for Kodi's answer.</summary>
    /// <param name="method">The JSON-RPC method, for example <c>Player.GetProperties</c>.</param>
    /// <param name="writeParams">Writes the <c>params</c> object, or <see langword="null"/> for none.</param>
    /// <param name="timeout">How long Kodi may take to answer.</param>
    /// <param name="timeProvider">Clock for the timeout.</param>
    /// <param name="cancellationToken">Stops waiting.</param>
    /// <returns>The <c>result</c> of the answer.</returns>
    /// <exception cref="KodiErrorException">Kodi answered with an error.</exception>
    /// <exception cref="KodiException">No answer in time, or the connection is lost.</exception>
    public async Task<JsonElement> CallAsync(
        string method,
        Action<Utf8JsonWriter>? writeParams,
        TimeSpan timeout,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _nextId);
        var answer = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (_closed)
            {
                throw new KodiException($"The connection to Kodi at {_endpoint} is closed.");
            }

            _pending[id] = answer;
        }

        try
        {
            var request = WriteRequest(id, method, writeParams);
            await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _stream.WriteAsync(request, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
            }

            return await answer.Task.WaitAsync(timeout, timeProvider, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            throw new KodiException($"Kodi at {_endpoint} did not answer {method} within {timeout.TotalSeconds:0.#} s.", ex);
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            throw new KodiException($"Lost the connection to Kodi at {_endpoint}.", ex);
        }
        finally
        {
            lock (_gate)
            {
                _pending.Remove(id);
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _lifetime.CancelAsync().ConfigureAwait(false);
        await _stream.DisposeAsync().ConfigureAwait(false);
        _tcpClient.Dispose(); // Ends a read that does not observe cancellation.
        await _receiveLoop.ConfigureAwait(false);
        _lifetime.Dispose();
        _writeLock.Dispose();
    }

    private static ReadOnlyMemory<byte> WriteRequest(int id, string method, Action<Utf8JsonWriter>? writeParams)
    {
        var buffer = new ArrayBufferWriter<byte>(256);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("jsonrpc", "2.0");
            writer.WriteString("method", method);
            if (writeParams is not null)
            {
                writer.WritePropertyName("params");
                writeParams(writer);
            }

            writer.WriteNumber("id", id);
            writer.WriteEndObject();
        }

        return buffer.WrittenMemory;
    }

    private async Task ReceiveAsync(CancellationToken cancellationToken)
    {
        var splitter = new JsonObjectSplitter(MaxMessageLength);
        Exception? failure = null;
        try
        {
            while (true)
            {
                var read = await _stream.ReadAsync(splitter.GetBuffer(), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break; // Kodi closed the connection.
                }

                splitter.Advance(read);
                while (splitter.TryRead(out var message))
                {
                    Dispatch(message);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Disposing.
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or InvalidDataException or JsonException or InvalidOperationException)
        {
            failure = ex;
        }
        finally
        {
            TaskCompletionSource<JsonElement>[] waiting;
            lock (_gate)
            {
                _closed = true;
                waiting = [.. _pending.Values];
                _pending.Clear();
            }

            var message = $"The connection to Kodi at {_endpoint} closed.";
            foreach (var answer in waiting)
            {
                answer.TrySetException(failure is null ? new KodiException(message) : new KodiException(message, failure));
            }
        }
    }

    private void Dispatch(ReadOnlyMemory<byte> message)
    {
        using var document = JsonDocument.Parse(message);
        var root = document.RootElement;
        if (root.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.Number && idElement.TryGetInt32(out var id))
        {
            TaskCompletionSource<JsonElement>? answer;
            lock (_gate)
            {
                _pending.Remove(id, out answer);
            }

            if (answer is null)
            {
                return; // Already timed out.
            }

            if (root.TryGetProperty("result", out var result))
            {
                answer.TrySetResult(result.Clone());
            }
            else
            {
                var error = root.TryGetProperty("error", out var details)
                    && details.ValueKind == JsonValueKind.Object
                    && details.TryGetProperty("message", out var text)
                    ? text.ToString()
                    : "no result";
                answer.TrySetException(new KodiErrorException($"Kodi answered with an error: {error}"));
            }
        }
        else if (root.TryGetProperty("method", out var method) && method.ValueKind == JsonValueKind.String)
        {
            _onNotification(method.GetString()!);
        }
    }
}
