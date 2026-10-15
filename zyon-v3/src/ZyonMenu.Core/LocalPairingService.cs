// C# / src/ZyonMenu.Core/LocalPairingService.cs / .NET 8 / Windows 10 loopback FiveM pairing
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZyonMenu.Core;

public sealed class PairingTicket : EventArgs
{
    public string Endpoint { get; set; } = "";
    public string Token { get; set; } = "";
    public int PlayerId { get; set; }
    public string PlayerName { get; set; } = "";
    public string Error { get; set; } = "";
    public string Version { get; set; } = "";
}

public sealed class LocalPairingService : IDisposable
{
    public const int Port = 39743;
    public const string AllowedOrigin = "https://cfx-nui-zyon_admin_menu";
    private const int Limit = 8192;
    private readonly TcpListener _listener;
    private readonly SemaphoreSlim _slots = new(8, 8);
    private readonly object _lifecycle = new();
    private readonly JsonSerializerOptions _json = new(MenuApiClient.JsonOptions)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    private CancellationTokenSource? _lifetime;
    private Task? _loop;
    private int _enabled = 1;
    private int _generation;
    private bool _disposed;
    public event EventHandler<PairingTicket>? TicketReceived;
    public int ListeningPort => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public bool Enabled
    {
        get => Volatile.Read(ref _enabled) != 0;
        set
        {
            if (Interlocked.Exchange(ref _enabled, value ? 1 : 0) != (value ? 1 : 0))
                Interlocked.Increment(ref _generation);
        }
    }

    public LocalPairingService(int port = Port)
    {
        if (port is < 0 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        _listener = new TcpListener(IPAddress.Loopback, port) { ExclusiveAddressUse = true };
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_lifecycle)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            if (_loop is not null) return Task.CompletedTask;
            _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            try { _listener.Start(16); }
            catch
            {
                _lifetime.Dispose();
                _lifetime = null;
                throw;
            }
            _loop = AcceptAsync(_lifetime.Token);
            return Task.CompletedTask;
        }
    }

    private async Task AcceptAsync(CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(cancellation); }
            catch (OperationCanceledException) { break; }
            catch (SocketException) when (cancellation.IsCancellationRequested) { break; }
            catch (ObjectDisposedException) when (cancellation.IsCancellationRequested) { break; }
            if (!_slots.Wait(0)) { client.Dispose(); continue; }
            // Each connection owns a bounded parser and deadline; task handles its exceptions.
            _ = ServeAsync(client, cancellation);
        }
    }

    private async Task ServeAsync(TcpClient client, CancellationToken cancellation)
    {
        using (client)
        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
        {
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            NetworkStream? stream = null;
            try
            {
                if (client.Client.RemoteEndPoint is not IPEndPoint peer || !IPAddress.IsLoopback(peer.Address)) return;
                int generation = Volatile.Read(ref _generation);
                stream = client.GetStream();
                var request = await ReadRequestAsync(stream, deadline.Token);
                if (request.Headers.GetValueOrDefault("origin") != AllowedOrigin)
                {
                    await RespondAsync(stream, 403, new { error = "Origem de pareamento não autorizada." }, false, deadline.Token);
                    return;
                }
                if (request.Path is not ("/presence" or "/pair"))
                {
                    await RespondAsync(stream, 404, new { error = "Rota desconhecida." }, true, deadline.Token);
                    return;
                }
                if (request.Method == "OPTIONS")
                {
                    await RespondAsync(stream, 204, null, true, deadline.Token);
                    return;
                }
                if (request.Path == "/presence" && request.Method == "GET")
                {
                    await RespondAsync(stream, 200, new { ready = Enabled, version = "3.1.1" }, true, deadline.Token);
                    return;
                }
                if (request.Path != "/pair" || request.Method != "POST")
                {
                    await RespondAsync(stream, 405, new { error = "Método não permitido." }, true, deadline.Token);
                    return;
                }
                if (!Enabled || generation != Volatile.Read(ref _generation))
                {
                    await RespondAsync(stream, 409, new { error = "Detecção pausada no aplicativo." }, true, deadline.Token);
                    return;
                }
                if (request.Headers.GetValueOrDefault("content-type")?.Split(';')[0].Trim() != "application/json")
                {
                    await RespondAsync(stream, 415, new { error = "Use o formato JSON de pareamento." }, true, deadline.Token);
                    return;
                }
                PairingTicket? ticket;
                try { ticket = JsonSerializer.Deserialize<PairingTicket>(request.Body, _json); }
                catch (JsonException)
                {
                    await RespondAsync(stream, 400, new { error = "Pareamento inválido." }, true, deadline.Token);
                    return;
                }
                if (!ValidateTicket(ticket))
                {
                    await RespondAsync(stream, 422, new { error = "Dados de pareamento incompletos ou incompatíveis." }, true, deadline.Token);
                    return;
                }
                if (!Enabled || generation != Volatile.Read(ref _generation))
                {
                    await RespondAsync(stream, 409, new { error = "Pareamento substituído ou pausado." }, true, deadline.Token);
                    return;
                }
                TicketReceived?.Invoke(this, ticket!);
                await RespondAsync(stream, 200, new { accepted = true }, true, deadline.Token);
            }
            catch (RequestException ex)
            {
                try { if (stream is not null) await RespondAsync(stream, ex.Status, new { error = "Requisição local inválida." }, false, deadline.Token); }
                catch (Exception error) when (error is IOException or SocketException or OperationCanceledException or ObjectDisposedException) { }
            }
            catch (Exception error) when (error is IOException or SocketException or OperationCanceledException or ObjectDisposedException) { }
            finally { stream?.Dispose(); _slots.Release(); }
        }
    }

    private static bool ValidateTicket(PairingTicket? ticket)
    {
        if (ticket is null || ticket.Version != "3.1.1" || ticket.Error is null || ticket.Error.Length > 240 ||
            ticket.PlayerName is null || ticket.PlayerName.Length > 128 || ticket.Endpoint is null || ticket.Token is null)
            return false;
        if (ticket.Error.Length != 0)
        {
            if (ticket.Token.Length != 0)
                ticket.Error = ticket.Error.Replace(ticket.Token, "[oculto]", StringComparison.Ordinal);
            ticket.Token = "";
            ticket.Error = new string(ticket.Error.Where(c => !char.IsControl(c)).ToArray());
            return ticket.Error.Length != 0;
        }
        if (ticket.PlayerId <= 0 || ticket.Token.Length != 64 || !ticket.Token.All(Uri.IsHexDigit) || ticket.Endpoint.Length > 2048)
            return false;
        try { ticket.Endpoint = MenuApiClient.ValidateBaseUri(ticket.Endpoint).AbsoluteUri; }
        catch (ArgumentException) { return false; }
        ticket.PlayerName = new string(ticket.PlayerName.Where(c => !char.IsControl(c)).ToArray());
        return true;
    }

    private sealed record Request(string Method, string Path, Dictionary<string, string> Headers, byte[] Body);
    private sealed class RequestException(int status) : Exception
    {
        public int Status { get; } = status;
    }

    private static async Task<Request> ReadRequestAsync(NetworkStream stream, CancellationToken cancellation)
    {
        byte[] data = new byte[Limit * 2];
        int size = 0;
        int end = -1;
        while (end < 0)
        {
            if (size >= Limit) throw new RequestException(431);
            int read = await stream.ReadAsync(data.AsMemory(size, Math.Min(1024, data.Length - size)), cancellation);
            if (read == 0) throw new RequestException(400);
            int start = Math.Max(0, size - 3);
            size += read;
            for (int i = start; i + 3 < size; i++)
            {
                if (data[i] == 13 && data[i + 1] == 10 && data[i + 2] == 13 && data[i + 3] == 10) { end = i + 4; break; }
            }
        }
        if (end > Limit || data.AsSpan(0, end).IndexOf((byte)0) >= 0) throw new RequestException(400);
        var lines = Encoding.ASCII.GetString(data, 0, end - 4).Split("\r\n");
        var first = lines[0].Split(' ');
        if (first.Length != 3 || first[2] is not ("HTTP/1.1" or "HTTP/1.0") || first[1].Contains('?'))
            throw new RequestException(400);
        Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);
        if (lines.Length > 65) throw new RequestException(431);
        foreach (var line in lines.Skip(1))
        {
            int separator = line.IndexOf(':');
            if (separator <= 0 || line[0] is ' ' or '\t') throw new RequestException(400);
            string key = line[..separator];
            if (!key.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')) throw new RequestException(400);
            string value = line[(separator + 1)..].Trim();
            if (value.Any(c => char.IsControl(c) && c != '\t') || !headers.TryAdd(key, value)) throw new RequestException(400);
        }
        if (headers.ContainsKey("transfer-encoding")) throw new RequestException(400);
        int length = 0;
        if (headers.TryGetValue("content-length", out var lengthText) &&
            (!int.TryParse(lengthText, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out length) || length < 0))
            throw new RequestException(400);
        if (length > Limit) throw new RequestException(413);
        if (first[0] != "POST" && length != 0) throw new RequestException(400);
        byte[] body = new byte[length];
        int already = Math.Min(size - end, length);
        data.AsSpan(end, already).CopyTo(body);
        int received = already;
        while (received < length)
        {
            int read = await stream.ReadAsync(body.AsMemory(received), cancellation);
            if (read == 0) throw new RequestException(400);
            received += read;
        }
        return new Request(first[0], first[1], headers, body);
    }

    private static async Task RespondAsync(NetworkStream stream, int status, object? value, bool cors,
        CancellationToken cancellation)
    {
        byte[] body = value is null ? [] : JsonSerializer.SerializeToUtf8Bytes(value, MenuApiClient.JsonOptions);
        string reason = status switch
        {
            200 => "OK", 204 => "No Content", 400 => "Bad Request", 403 => "Forbidden", 404 => "Not Found",
            405 => "Method Not Allowed", 409 => "Conflict", 413 => "Content Too Large", 415 => "Unsupported Media Type",
            422 => "Unprocessable Content", 431 => "Request Header Fields Too Large", _ => "Error"
        };
        string headers = $"HTTP/1.1 {status} {reason}\r\nContent-Length: {body.Length}\r\nContent-Type: application/json; charset=utf-8\r\nConnection: close\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\n";
        if (cors)
            headers += $"Access-Control-Allow-Origin: {AllowedOrigin}\r\nVary: Origin\r\nAccess-Control-Allow-Methods: GET, POST, OPTIONS\r\nAccess-Control-Allow-Headers: Content-Type\r\nAccess-Control-Allow-Private-Network: true\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(headers + "\r\n"), cancellation);
        if (body.Length != 0) await stream.WriteAsync(body, cancellation);
    }

    public void Dispose()
    {
        lock (_lifecycle)
        {
            if (_disposed) return;
            _disposed = true;
            Enabled = false;
            _lifetime?.Cancel();
            _listener.Stop();
            // Existing connections release their semaphore and deadline after cancellation.
        }
    }
}
