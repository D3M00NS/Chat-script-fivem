// C# / tests/ZyonMenu.Core.Tests/Program.cs / .NET 8 / real loopback HTTP and client failures
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ZyonMenu.Core;

int passed = 0;
int failed = 0;
async Task Check(string name, Func<Task> action)
{
    try { await action(); passed++; Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex.Message); }
}
void Assert(bool value, string reason)
{
    if (!value) throw new InvalidOperationException(reason);
}
async Task Expect<T>(Func<Task> action) where T : Exception
{
    try { await action(); }
    catch (T) { return; }
    throw new InvalidOperationException("Expected " + typeof(T).Name);
}
const string token = "test-only-fake-token-0123456789abcdef";

await Check("Endpoint rejects insecure remote HTTP and embedded credentials", async () =>
{
    foreach (var value in new[] { "http://example.com/zyon_admin_menu/", "https://user:pass@example.com/", "file:///tmp/test", "https://example.com/?key=foo", "https://example.com/#key" })
        await Expect<ArgumentException>(() => Task.FromResult(MenuApiClient.ValidateBaseUri(value)));
    Assert(MenuApiClient.ValidateBaseUri("https://example.com/zyon_admin_menu").AbsolutePath == "/zyon_admin_menu/", "Base path lost");
    Assert(MenuApiClient.ValidateBaseUri("http://127.0.0.1:30120/zyon_admin_menu/").IsLoopback, "Loopback rejected");
    Assert(MenuApiClient.ValidateBaseUri("http://[::1]:30120/zyon_admin_menu/").IsLoopback, "IPv6 loopback rejected");
});
await Check("Tokens cannot inject headers or use weak defaults", async () =>
{
    foreach (var bad in new[] { "", "short", new string('x', 32) + "\r\nX-Fake: true", new string('x', 513) })
        await Expect<ArgumentException>(() => Task.FromResult(new MenuApiClient(new Uri("http://localhost/"), bad)));
});
await Check("Valid settings round trip with effects off by default", () =>
{
    var settings = new MenuSettings();
    settings.Validate();
    var parsed = JsonSerializer.Deserialize<MenuSettings>(JsonSerializer.Serialize(settings, MenuApiClient.JsonOptions), MenuApiClient.JsonOptions)!;
    Assert(!parsed.Aim.Enabled && !parsed.Silent.Enabled && !parsed.Player.GodMode && !parsed.Player.Noclip &&
           !parsed.Esp.Enabled && !parsed.Weapons.NoSpread && !parsed.Weapons.NoRecoil && !parsed.Weapons.FreezeAmmo,
           "Default enables an effect");
    Assert(parsed.Aim.OnlyPlayers && parsed.Esp.MaxDistance == 250 && parsed.Player.NoclipSpeed == 5, "Configuration lost");
    Assert(!parsed.Player.Invisible && !parsed.Player.MuteVoice && !parsed.Esp.Admins, "Administrative effects must start off");
    return Task.CompletedTask;
});
await Check("Invalid settings never reach transport", async () =>
{
    int calls = 0;
    using var api = new MenuApiClient(new Uri("http://localhost/"), token, new Handler(_ => { calls++; return JsonResponse("{}"); }));
    var invalid = new MenuSettings();
    invalid.Silent.Enabled = true;
    await Expect<ArgumentException>(() => api.PushSettingsAsync(invalid));
    invalid.Silent.Enabled = false;
    invalid.Aim.Fov = 999;
    await Expect<ArgumentException>(() => api.PushSettingsAsync(invalid));
    invalid.Aim.Fov = 100;
    invalid.Aim.ActivationKey = "Unknown";
    await Expect<ArgumentException>(() => api.PushSettingsAsync(invalid));
    invalid.Aim.ActivationKey = "RMB";
    foreach (var speed in new[] { -1, 0, 51 })
    {
        invalid.Player.NoclipSpeed = speed;
        await Expect<ArgumentException>(() => api.PushSettingsAsync(invalid));
    }
    Assert(calls == 0, "Sent invalid settings");
});
await Check("Real TCP HTTP request carries token and resource path", async () =>
{
    using var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var port = ((IPEndPoint)listener.LocalEndpoint).Port;
    using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(8));
    Task<string> server = ServeOnce(listener,
        "{\"version\":\"3.1.1\",\"ready\":true,\"sessionActive\":true,\"playerOnline\":true,\"playerId\":7,\"playerName\":\"Test\",\"desiredRevision\":3,\"appliedRevision\":2,\"message\":\"OK\",\"capabilities\":[\"aim\",\"esp\"],\"medicalProvider\":\"qbcore\",\"medicalMessage\":\"Hospital QBCore selecionado.\"}", limit.Token);
    using var api = new MenuApiClient(new Uri($"http://127.0.0.1:{port}/zyon_admin_menu/"), token);
    var status = await api.GetStatusAsync(limit.Token);
    var request = await server;
    Assert(request.StartsWith("GET /zyon_admin_menu/api/status HTTP/1.1\r\n"), "Wrong HTTP path");
    Assert(request.Contains("Authorization: Bearer " + token + "\r\n", StringComparison.OrdinalIgnoreCase), "Missing auth");
    Assert(status.Ready && status.SessionActive && status.AppliedRevision == 2 && status.DesiredRevision == 3, "State/ack information lost");
    Assert(status.MedicalProvider == "qbcore" && status.MedicalMessage == "Hospital QBCore selecionado.", "Medical integration state lost in HTTP response");
});
await Check("Settings serialize without confusing acceptance with application", async () =>
{
    string? body = null;
    using var api = new MenuApiClient(new Uri("http://localhost/zyon_admin_menu/"), token,
        new AsyncHandler(async request => { body = await request.Content!.ReadAsStringAsync(); return JsonResponse("{\"revision\":4,\"message\":\"accepted\"}"); }));
    var settings = new MenuSettings();
    settings.Aim.Enabled = true;
    settings.Player.GodMode = true;
    settings.Player.NoclipSpeed = 27;
    settings.Player.Invisible = true;
    settings.Player.MuteVoice = true;
    settings.Esp.Admins = true;
    await api.PushSettingsAsync(settings);
    using var json = JsonDocument.Parse(body!);
    Assert(json.RootElement.GetProperty("settings").GetProperty("player").GetProperty("godMode").GetBoolean(), "Lost godmode value");
    Assert(json.RootElement.GetProperty("settings").GetProperty("aim").GetProperty("onlyPlayers").GetBoolean(), "Lost target filter");
    var player = json.RootElement.GetProperty("settings").GetProperty("player");
    Assert(player.GetProperty("noclipSpeed").GetInt32() == 27 && player.GetProperty("invisible").GetBoolean() &&
        player.GetProperty("muteVoice").GetBoolean(), "Administrative settings lost in transport");
    Assert(json.RootElement.GetProperty("settings").GetProperty("esp").GetProperty("admins").GetBoolean(), "ADM filter lost");
});
await Check("Authentication errors redact the token", async () =>
{
    using var api = new MenuApiClient(new Uri("http://localhost/"), token, new Handler(_ =>
        JsonResponse(JsonSerializer.Serialize(new { error = "denied " + token }), HttpStatusCode.Unauthorized)));
    try { await api.GetStatusAsync(); throw new InvalidOperationException("Did not reject 401"); }
    catch (MenuApiException ex)
    {
        Assert(ex.StatusCode == HttpStatusCode.Unauthorized && !ex.Message.Contains(token), "Leaked token or status missing");
    }
});
await Check("Invalid protocol does not report a ready connection", async () =>
{
    foreach (var json in new[] { "{", "null", "{\"ready\":true}" })
    {
        using var api = new MenuApiClient(new Uri("http://localhost/"), token, new Handler(_ => JsonResponse(json)));
        await Expect<MenuApiException>(() => api.GetStatusAsync());
    }
});
await Check("Oversized and non-JSON responses are rejected", async () =>
{
    using var large = new MenuApiClient(new Uri("http://localhost/"), token, new Handler(_ => JsonResponse(new string('a', 140_000))));
    await Expect<MenuApiException>(() => large.GetStatusAsync());
    using var html = new MenuApiClient(new Uri("http://localhost/"), token, new Handler(_ =>
        new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>not the API</html>", Encoding.UTF8, "text/html") }));
    await Expect<MenuApiException>(() => html.GetStatusAsync());
});
await Check("HTTP redirects are refused without forwarding credentials", async () =>
{
    using var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    int port = ((IPEndPoint)listener.LocalEndpoint).Port;
    using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(8));
    var server = ServeOnce(listener, "{}", limit.Token, "302 Found", "Location: https://example.com/\r\n");
    using var api = new MenuApiClient(new Uri($"http://127.0.0.1:{port}/zyon_admin_menu/"), token);
    await Expect<MenuApiException>(() => api.GetStatusAsync(limit.Token));
    await server;
});
await Check("Cancellation stops a pending operation", async () =>
{
    using var api = new MenuApiClient(new Uri("http://localhost/"), token, new AsyncHandler(async _ =>
    { await Task.Delay(100); return JsonResponse("{}"); }));
    using var cancelled = new CancellationTokenSource();
    cancelled.Cancel();
    await Expect<OperationCanceledException>(() => api.GetStatusAsync(cancelled.Token));
});
await Check("Events and revive use bounded authenticated routes", async () =>
{
    List<string> routes = [];
    using var api = new MenuApiClient(new Uri("http://localhost/zyon_admin_menu/"), token,
        new Handler(request =>
        {
            routes.Add(request.Method + " " + request.RequestUri!.PathAndQuery);
            Assert(request.Headers.Authorization?.Parameter == token, "Unauthenticated request");
            return request.Method == HttpMethod.Get
                ? JsonResponse("{\"cursor\":9,\"events\":[{\"id\":9,\"type\":\"applied\",\"message\":\"OK\"}]}") : JsonResponse("{}");
        }));
    var events = await api.GetEventsAsync(8);
    await api.ReviveAsync();
    await api.SuicideAsync();
    await api.DisconnectAsync();
    await Expect<ArgumentOutOfRangeException>(() => api.GetEventsAsync(-1));
    Assert(events.Cursor == 9 && events.Events.Count == 1, "Event response lost");
    Assert(routes.SequenceEqual(new[] { "GET /zyon_admin_menu/api/events?since=8", "POST /zyon_admin_menu/api/revive", "POST /zyon_admin_menu/api/suicide", "POST /zyon_admin_menu/api/disconnect" }), "Wrong routes");
});

await PairingServiceChecks.RunAsync(Check);

Console.WriteLine($"RESULT {passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;

static HttpResponseMessage JsonResponse(string value, HttpStatusCode status = HttpStatusCode.OK) =>
    new(status) { Content = new StringContent(value, Encoding.UTF8, "application/json") };

static async Task<string> ServeOnce(TcpListener listener, string json, CancellationToken cancellation,
    string status = "200 OK", string extraHeaders = "")
{
    using var socket = await listener.AcceptTcpClientAsync(cancellation);
    await using var stream = socket.GetStream();
    using var buffer = new MemoryStream();
    byte[] item = new byte[1];
    while (buffer.Length < 8192)
    {
        int n = await stream.ReadAsync(item, cancellation);
        if (n == 0) break;
        buffer.WriteByte(item[0]);
        if (buffer.Length >= 4)
        {
            byte[] bytes = buffer.GetBuffer();
            int end = (int)buffer.Length;
            if (bytes[end - 4] == 13 && bytes[end - 3] == 10 && bytes[end - 2] == 13 && bytes[end - 1] == 10) break;
        }
    }
    byte[] body = Encoding.UTF8.GetBytes(json);
    byte[] headers = Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n{extraHeaders}\r\n");
    await stream.WriteAsync(headers, cancellation);
    await stream.WriteAsync(body, cancellation);
    return Encoding.ASCII.GetString(buffer.ToArray());
}

sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> handle) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(handle(request));
}
sealed class AsyncHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handle) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handle(request);
}
