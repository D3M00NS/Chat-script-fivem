// C# / tests/ZyonMenu.Core.Tests/PairingServiceChecks.cs / .NET 8 / real loopback pairing protocol tests
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ZyonMenu.Core;

internal static class PairingServiceChecks
{
    private const string Nonce = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    public static async Task RunAsync(Func<string, Func<Task>, Task> check)
    {
        await check("Pairing listener starts promptly, stays on loopback, and is idempotent", async () =>
        {
            using var service = new LocalPairingService(0);
            await service.StartAsync().WaitAsync(TimeSpan.FromSeconds(1));
            int port = service.ListeningPort;
            Assert(port > 0, "Ephemeral port was not allocated");
            await service.StartAsync();
            Assert(service.ListeningPort == port, "Second startup replaced the listener");
            using var response = await Send(service, HttpMethod.Get, "/presence");
            Assert(response.StatusCode == HttpStatusCode.OK, "Listener did not answer on loopback");
            var localAddress = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Select(n => n.Address)
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a));
            if (localAddress is not null)
            {
                using var otherInterface = new TcpListener(localAddress, port);
                otherInterface.Start();
            }
        });

        await check("Pairing rejects absent, foreign, and lookalike origins without CORS", async () =>
        {
            using var service = await Start();
            int events = 0;
            service.TicketReceived += (_, _) => events++;
            foreach (string? origin in new[] { null, "https://example.com", "https://cfx-nui-zyon_admin_menu.evil", "null" })
            {
                using var response = await Send(service, HttpMethod.Post, "/pair", Json(Ticket()), origin);
                Assert(response.StatusCode == HttpStatusCode.Forbidden, "Untrusted origin accepted");
                Assert(!response.Headers.Contains("Access-Control-Allow-Origin"), "Untrusted origin got CORS permission");
            }
            Assert(events == 0, "Untrusted origin raised a pairing event");
        });

        await check("Pairing browser preflight permits only the resource origin and private network", async () =>
        {
            using var service = await Start();
            using var response = await Send(service, HttpMethod.Options, "/pair");
            Assert(response.StatusCode == HttpStatusCode.NoContent, "Preflight rejected");
            Assert(Header(response, "Access-Control-Allow-Origin") == LocalPairingService.AllowedOrigin, "CORS origin changed");
            Assert(Header(response, "Access-Control-Allow-Methods") == "GET, POST, OPTIONS", "Wrong preflight methods");
            Assert(Header(response, "Access-Control-Allow-Headers") == "Content-Type", "Missing JSON header permission");
            Assert(Header(response, "Access-Control-Allow-Private-Network") == "true", "Private network permission missing");
            Assert((await response.Content.ReadAsByteArrayAsync()).Length == 0, "Preflight returned a body");
        });

        await check("Pairing presence reports pause and resume without restarting", async () =>
        {
            using var service = await Start();
            foreach (bool enabled in new[] { true, false, true })
            {
                service.Enabled = enabled;
                using var response = await Send(service, HttpMethod.Get, "/presence");
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                Assert(json.RootElement.GetProperty("ready").GetBoolean() == enabled, "Presence did not reflect detection state");
                Assert(json.RootElement.GetProperty("version").GetString() == "3.1.1", "Wrong bridge protocol");
                Assert(Header(response, "Cache-Control") == "no-store", "Presence can be cached");
            }
        });

        await check("Pairing delivers a normalized ticket and never echoes its nonce", async () =>
        {
            using var service = await Start();
            List<PairingTicket> tickets = [];
            service.TicketReceived += (_, ticket) => tickets.Add(ticket);
            var submitted = Ticket();
            submitted.Endpoint = "http://127.0.0.1:30120/zyon_admin_menu";
            submitted.PlayerName = "Player\r\nSeven";
            using var response = await Send(service, HttpMethod.Post, "/pair", Json(submitted));
            string body = await response.Content.ReadAsStringAsync();
            Assert(response.StatusCode == HttpStatusCode.OK && tickets.Count == 1, "Valid pairing was not delivered once");
            Assert(tickets[0].Endpoint == "http://127.0.0.1:30120/zyon_admin_menu/", "Resource path was not normalized");
            Assert(tickets[0].PlayerId == 7 && tickets[0].PlayerName == "PlayerSeven" && tickets[0].Token == Nonce, "Pairing identity lost");
            Assert(!body.Contains(Nonce, StringComparison.Ordinal) && body.Contains("\"accepted\":true", StringComparison.Ordinal), "Response leaked credentials or omitted acceptance");
        });

        await check("Pairing supports HTTPS servers and IPv6 localhost endpoints", async () =>
        {
            using var service = await Start();
            foreach (string endpoint in new[] { "https://admin.example.test/zyon_admin_menu/", "http://[::1]:30120/zyon_admin_menu/" })
            {
                var submitted = Ticket();
                submitted.Endpoint = endpoint;
                using var response = await Send(service, HttpMethod.Post, "/pair", Json(submitted));
                Assert(response.StatusCode == HttpStatusCode.OK, "Supported resource endpoint rejected");
            }
        });

        await check("Pairing refuses incomplete, stale, or oversized ticket fields", async () =>
        {
            using var service = await Start();
            int events = 0;
            service.TicketReceived += (_, _) => events++;
            Action<PairingTicket>[] invalid =
            [
                t => t.Version = "1.0.0", t => t.Version = "", t => t.PlayerId = 0, t => t.PlayerId = -1,
                t => t.Token = "", t => t.Token = new string('a', 63), t => t.Token = new string('a', 65),
                t => t.Token = new string('g', 64), t => t.Token = Nonce[..63] + "\n",
                t => t.PlayerName = new string('p', 129), t => t.Error = new string('e', 241),
                t => t.PlayerName = null!, t => t.Error = null!, t => t.Endpoint = null!, t => t.Token = null!,
                t => t.Endpoint = "https://example.test/" + new string('p', 2049)
            ];
            foreach (var mutate in invalid)
            {
                var submitted = Ticket();
                mutate(submitted);
                using var response = await Send(service, HttpMethod.Post, "/pair", Json(submitted));
                Assert(response.StatusCode == HttpStatusCode.UnprocessableEntity, "Invalid ticket field accepted");
            }
            using var missing = await Send(service, HttpMethod.Post, "/pair", "{}");
            Assert(missing.StatusCode == HttpStatusCode.UnprocessableEntity && events == 0, "Incomplete ticket was delivered");
        });

        await check("Pairing rejects malformed JSON and unrecognized ticket properties", async () =>
        {
            using var service = await Start();
            int events = 0;
            service.TicketReceived += (_, _) => events++;
            string valid = Json(Ticket());
            foreach (string submitted in new[] { "{", "[]", "42", valid[..^1] + ",\"targetPlayerId\":999}" })
            {
                using var response = await Send(service, HttpMethod.Post, "/pair", submitted);
                Assert(response.StatusCode == HttpStatusCode.BadRequest, "Invalid JSON shape accepted");
            }
            using var nullTicket = await Send(service, HttpMethod.Post, "/pair", "null");
            Assert(nullTicket.StatusCode == HttpStatusCode.UnprocessableEntity && events == 0, "Null/invalid JSON delivered a ticket");
        });

        await check("Pairing rejects remote plaintext, embedded credentials, and URL secrets", async () =>
        {
            using var service = await Start();
            int events = 0;
            service.TicketReceived += (_, _) => events++;
            foreach (string endpoint in new[] { "http://example.test/zyon_admin_menu/", "https://user:password@example.test/", "https://example.test/?token=x", "https://example.test/#token", "file:///tmp/menu", "not-an-endpoint" })
            {
                var submitted = Ticket();
                submitted.Endpoint = endpoint;
                using var response = await Send(service, HttpMethod.Post, "/pair", Json(submitted));
                Assert(response.StatusCode == HttpStatusCode.UnprocessableEntity, "Unsafe endpoint accepted");
            }
            Assert(events == 0, "Unsafe endpoint reached a subscriber");
        });

        await check("Pairing reports resource errors without forwarding the nonce", async () =>
        {
            using var service = await Start();
            PairingTicket? received = null;
            service.TicketReceived += (_, ticket) => received = ticket;
            var submitted = Ticket();
            submitted.Error = "Sem permissão\r\npara parear.";
            submitted.Endpoint = "";
            submitted.PlayerId = 0;
            using var response = await Send(service, HttpMethod.Post, "/pair", Json(submitted));
            Assert(response.StatusCode == HttpStatusCode.OK && received is not null, "Resource error was not delivered");
            Assert(received!.Token == "" && received.Error == "Sem permissãopara parear.", "Error ticket leaked nonce or control characters");
            Assert(!(await response.Content.ReadAsStringAsync()).Contains(Nonce, StringComparison.Ordinal), "Response leaked nonce");
            submitted.Error = "Falha com credencial " + Nonce;
            using var withCredential = await Send(service, HttpMethod.Post, "/pair", Json(submitted));
            Assert(withCredential.StatusCode == HttpStatusCode.OK && received.Token == "" &&
                !received.Error.Contains(Nonce, StringComparison.Ordinal), "Error message leaked its credential");
        });

        await check("Pairing rejects unknown routes, wrong methods, and non-JSON bodies", async () =>
        {
            using var service = await Start();
            using var unknown = await Send(service, HttpMethod.Get, "/other");
            using var wrongPair = await Send(service, HttpMethod.Get, "/pair");
            using var wrongPresence = await Send(service, HttpMethod.Post, "/presence", "{}");
            using var nonJson = await Send(service, HttpMethod.Post, "/pair", Json(Ticket()), mediaType: "text/plain");
            Assert(unknown.StatusCode == HttpStatusCode.NotFound && wrongPair.StatusCode == HttpStatusCode.MethodNotAllowed &&
                wrongPresence.StatusCode == HttpStatusCode.MethodNotAllowed && nonJson.StatusCode == HttpStatusCode.UnsupportedMediaType,
                "Route or content-type validation failed");
        });

        await check("Pairing pause rejects new tickets and resume requires a new request", async () =>
        {
            using var service = await Start();
            int events = 0;
            service.TicketReceived += (_, _) => events++;
            service.Enabled = false;
            using var paused = await Send(service, HttpMethod.Post, "/pair", Json(Ticket()));
            Assert(paused.StatusCode == HttpStatusCode.Conflict && events == 0, "Paused app accepted a ticket");
            service.Enabled = true;
            using var resumed = await Send(service, HttpMethod.Post, "/pair", Json(Ticket()));
            Assert(resumed.StatusCode == HttpStatusCode.OK && events == 1, "Fresh ticket did not recover after resume");
        });

        await check("Pairing fences a delayed request body across pause and resume", async () =>
        {
            using var service = await Start();
            int events = 0;
            service.TicketReceived += (_, _) => events++;
            using var socket = new TcpClient(AddressFamily.InterNetwork);
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            await socket.ConnectAsync(IPAddress.Loopback, service.ListeningPort, limit.Token);
            using var stream = socket.GetStream();
            byte[] body = Encoding.UTF8.GetBytes(Json(Ticket()));
            byte[] headers = Encoding.ASCII.GetBytes(RequestHeaders("POST", "/pair", body.Length));
            await stream.WriteAsync(headers, limit.Token);
            await stream.WriteAsync(body.AsMemory(0, 1), limit.Token);
            // The accept loop starts each handler before accepting the next connection. This
            // response proves the pending handler captured its generation before the pause.
            using var barrier = await Send(service, HttpMethod.Get, "/presence");
            Assert(barrier.StatusCode == HttpStatusCode.OK, "Pending body blocked presence");
            service.Enabled = false;
            service.Enabled = true;
            await stream.WriteAsync(body.AsMemory(1), limit.Token);
            var response = await ReadRaw(stream, limit.Token);
            Assert(response.Status == 409 && events == 0, "A stale request reconnected after pause/resume");
        });

        await check("Pairing malformed HTTP returns bounded errors instead of dropping the socket", async () =>
        {
            using var service = await Start();
            string origin = "Origin: " + LocalPairingService.AllowedOrigin + "\r\n";
            string[] requests =
            [
                "POST /pair HTTP/1.1\r\n" + origin + "Content-Length: 0\r\nContent-Length: 0\r\n\r\n",
                "POST /pair HTTP/1.1\r\n" + origin + "Transfer-Encoding: chunked\r\n\r\n0\r\n\r\n",
                "POST /pair HTTP/1.1\r\n" + origin + "Content-Length: -1\r\n\r\n",
                "POST /pair HTTP/1.1\r\n" + origin + "Content-Length: abc\r\n\r\n",
                "POST /pair HTTP/1.1\r\n" + origin + "Content-Length: 999999999999999999999\r\n\r\n",
                "GET /presence HTTP/1.1\r\n" + origin + "Content-Length: 1\r\n\r\nx",
                "GET /presence?key=x HTTP/1.1\r\n" + origin + "\r\n",
                "GET /presence HTTP/2\r\n" + origin + "\r\n",
                "GET /presence HTTP/1.1\r\n" + origin + " Folded: bad\r\n\r\n",
                "GET /presence HTTP/1.1\r\n" + origin + "Bad_Header: bad\r\n\r\n",
                "GET /presence HTTP/1.1\r\n" + origin + "X-Test: bad\0value\r\n\r\n"
            ];
            foreach (string request in requests)
            {
                var response = await Raw(service, request);
                Assert(response.Status == 400 && response.Body.Length <= 512, "Malformed request did not receive a bounded 400 response");
                Assert(!response.Headers.ContainsKey("Access-Control-Allow-Origin"), "Malformed headers received CORS authorization");
            }
        });

        await check("Pairing enforces body, header byte, and header count limits", async () =>
        {
            using var service = await Start();
            var largeBody = await Raw(service, RequestHeaders("POST", "/pair", 8193));
            Assert(largeBody.Status == 413, "Oversized declared body not rejected before reading");
            string prefix = "GET /presence HTTP/1.1\r\nOrigin: " + LocalPairingService.AllowedOrigin + "\r\n";
            var largeHeader = await Raw(service, prefix + "X-Long: " + new string('x', 8300) + "\r\n\r\n");
            Assert(largeHeader.Status is 400 or 431, "Oversized header not rejected");
            string extra = string.Concat(Enumerable.Range(0, 65).Select(n => $"X-{n}: x\r\n"));
            var manyHeaders = await Raw(service, prefix + extra + "\r\n");
            Assert(manyHeaders.Status == 431, "Header count was not capped");
        });

        await check("Pairing incomplete request bodies are rejected and the listener recovers", async () =>
        {
            using var service = await Start();
            using var socket = new TcpClient(AddressFamily.InterNetwork);
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            await socket.ConnectAsync(IPAddress.Loopback, service.ListeningPort, limit.Token);
            using var stream = socket.GetStream();
            await stream.WriteAsync(Encoding.ASCII.GetBytes(RequestHeaders("POST", "/pair", 40) + "{}"), limit.Token);
            socket.Client.Shutdown(SocketShutdown.Send);
            var incomplete = await ReadRaw(stream, limit.Token);
            Assert(incomplete.Status == 400, "Truncated request silently dropped");
            using var recovered = await Send(service, HttpMethod.Get, "/presence");
            Assert(recovered.StatusCode == HttpStatusCode.OK, "Malformed request stopped the listener");
        });

        await check("Pairing disposal closes pending sockets and prevents restart", async () =>
        {
            var service = await Start();
            int port = service.ListeningPort;
            using var socket = new TcpClient(AddressFamily.InterNetwork);
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await socket.ConnectAsync(IPAddress.Loopback, port, limit.Token);
            using var stream = socket.GetStream();
            await stream.WriteAsync(Encoding.ASCII.GetBytes(RequestHeaders("POST", "/pair", 40)), limit.Token);
            using var barrier = await Send(service, HttpMethod.Get, "/presence");
            service.Dispose();
            service.Dispose();
            int bytes;
            try { bytes = await stream.ReadAsync(new byte[1], limit.Token); }
            catch (IOException) { bytes = 0; }
            Assert(bytes == 0, "Disposed listener retained a pending request");
            bool restartRejected = false;
            try { await service.StartAsync(); }
            catch (ObjectDisposedException) { restartRejected = true; }
            Assert(restartRejected, "Disposed bridge restarted");
            using var probe = new TcpClient(AddressFamily.InterNetwork);
            bool connectRejected = false;
            try { await probe.ConnectAsync(IPAddress.Loopback, port, limit.Token); }
            catch (SocketException) { connectRejected = true; }
            Assert(connectRejected, "Disposed bridge still accepts connections");
        });

        await check("Pairing startup honors cancellation and rejects invalid listener ports", async () =>
        {
            foreach (int port in new[] { -1, 65536 })
            {
                bool rejected = false;
                try { using var invalid = new LocalPairingService(port); }
                catch (ArgumentOutOfRangeException) { rejected = true; }
                Assert(rejected, "Invalid listener port accepted");
            }
            using var service = new LocalPairingService(0);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            bool cancelled = false;
            try { await service.StartAsync(cancellation.Token); }
            catch (OperationCanceledException) { cancelled = true; }
            Assert(cancelled, "Cancelled startup opened a listener");
            await service.StartAsync();
            using var response = await Send(service, HttpMethod.Get, "/presence");
            Assert(response.StatusCode == HttpStatusCode.OK, "Cancelled startup poisoned a later valid startup");
        });
    }

    private static PairingTicket Ticket() => new()
    {
        Endpoint = "http://127.0.0.1:30120/zyon_admin_menu/",
        Token = Nonce,
        PlayerId = 7,
        PlayerName = "Player Seven",
        Version = "3.1.1"
    };

    private static string Json(PairingTicket ticket) => JsonSerializer.Serialize(ticket, MenuApiClient.JsonOptions);
    private static void Assert(bool value, string reason)
    {
        if (!value) throw new InvalidOperationException(reason);
    }

    private static async Task<LocalPairingService> Start()
    {
        var service = new LocalPairingService(0);
        await service.StartAsync();
        return service;
    }

    private static async Task<HttpResponseMessage> Send(LocalPairingService service, HttpMethod method, string path,
        string? body = null, string? origin = LocalPairingService.AllowedOrigin, string mediaType = "application/json")
    {
        using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(3) };
        using var request = new HttpRequestMessage(method, $"http://127.0.0.1:{service.ListeningPort}{path}");
        if (origin is not null) request.Headers.TryAddWithoutValidation("Origin", origin);
        if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, mediaType);
        return await client.SendAsync(request);
    }

    private static string Header(HttpResponseMessage response, string name) => string.Join(", ", response.Headers.GetValues(name));

    private static string RequestHeaders(string method, string path, int length) =>
        $"{method} {path} HTTP/1.1\r\nHost: 127.0.0.1\r\nOrigin: {LocalPairingService.AllowedOrigin}\r\nContent-Type: application/json\r\nContent-Length: {length}\r\nConnection: close\r\n\r\n";

    private sealed record RawResponse(int Status, Dictionary<string, string> Headers, string Body);

    private static async Task<RawResponse> Raw(LocalPairingService service, string request)
    {
        using var socket = new TcpClient(AddressFamily.InterNetwork);
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        await socket.ConnectAsync(IPAddress.Loopback, service.ListeningPort, limit.Token);
        using var stream = socket.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes(request), limit.Token);
        return await ReadRaw(stream, limit.Token);
    }

    private static async Task<RawResponse> ReadRaw(NetworkStream stream, CancellationToken cancellation)
    {
        using var output = new MemoryStream();
        byte[] data = new byte[1024];
        while (true)
        {
            int count = await stream.ReadAsync(data, cancellation);
            if (count == 0) break;
            output.Write(data, 0, count);
            Assert(output.Length <= 16 * 1024, "Bridge returned an unbounded response");
        }
        string value = Encoding.UTF8.GetString(output.ToArray());
        int split = value.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        Assert(split >= 0, "Socket closed without a complete HTTP error response");
        string[] lines = value[..split].Split("\r\n");
        string[] first = lines[0].Split(' ');
        Assert(first.Length >= 2 && first[0] == "HTTP/1.1" && int.TryParse(first[1], out _), "Missing HTTP status line");
        Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);
        foreach (string line in lines.Skip(1))
        {
            int separator = line.IndexOf(':');
            Assert(separator > 0, "Malformed response header");
            headers.Add(line[..separator], line[(separator + 1)..].Trim());
        }
        string body = value[(split + 4)..];
        Assert(headers.TryGetValue("Content-Length", out string? length) && int.TryParse(length, out int expected) &&
            expected == Encoding.UTF8.GetByteCount(body), "Truncated response body");
        return new RawResponse(int.Parse(first[1], System.Globalization.CultureInfo.InvariantCulture), headers, body);
    }
}
