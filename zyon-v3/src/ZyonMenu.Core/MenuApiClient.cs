// C# / src/ZyonMenu.Core/MenuApiClient.cs / .NET 8 / authenticated FiveM resource HTTP API
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ZyonMenu.Core;

public sealed class MenuApiException : Exception
{
    public HttpStatusCode? StatusCode { get; }

    public MenuApiException(string message, HttpStatusCode? statusCode = null) : base(message)
    {
        StatusCode = statusCode;
    }
}

public sealed class MenuApiClient : IDisposable
{
    private const int MaximumResponseBytes = 128 * 1024;
    private readonly HttpClient _http;
    private readonly Uri _baseUri;
    private readonly string _token;
    private readonly SemaphoreSlim _requests = new(1, 1);
    private bool _disposed;
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        MaxDepth = 16
    };

    public MenuApiClient(Uri baseUri, string token, HttpMessageHandler? handler = null)
    {
        ArgumentNullException.ThrowIfNull(baseUri);
        if (!baseUri.IsAbsoluteUri)
            throw new ArgumentException("Informe uma URL absoluta.", nameof(baseUri));
        _baseUri = ValidateBaseUri(baseUri.AbsoluteUri);
        if (string.IsNullOrWhiteSpace(token) || token.Length < 32 || token.Length > 512 ||
            token.Any(char.IsWhiteSpace) || token.Any(char.IsControl))
            throw new ArgumentException("Use um token de 32 a 512 caracteres, sem espaços.", nameof(token));
        _token = token;
        // Redirects must not carry the administrator credential to another host.
        _http = handler is null
            ? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            : new HttpClient(handler, disposeHandler: true);
        _http.Timeout = TimeSpan.FromSeconds(5);
    }

    public static Uri ValidateBaseUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("Informe uma URL HTTP/HTTPS sem usuário, senha, consulta ou fragmento.");
        if (uri.Scheme == "http" && !uri.IsLoopback)
            throw new ArgumentException("HTTP é permitido somente em localhost. Para outro computador, configure HTTPS.");
        return new Uri(uri.AbsoluteUri.TrimEnd('/') + "/");
    }

    public async Task<ServerStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var result = await RequestAsync<ServerStatus>(HttpMethod.Get, "api/status", null, cancellationToken);
        if (result.Capabilities is null || string.IsNullOrWhiteSpace(result.Version))
            throw new MenuApiException("O servidor não retornou o protocolo esperado do recurso.");
        return result;
    }

    public async Task PushSettingsAsync(MenuSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        await RequestAsync<JsonElement>(HttpMethod.Post, "api/settings", new { settings }, cancellationToken);
    }

    public async Task ReviveAsync(CancellationToken cancellationToken = default) =>
        _ = await RequestAsync<JsonElement>(HttpMethod.Post, "api/revive", new { }, cancellationToken);

    public async Task SuicideAsync(CancellationToken cancellationToken = default) =>
        _ = await RequestAsync<JsonElement>(HttpMethod.Post, "api/suicide", new { }, cancellationToken);

    public async Task DisconnectAsync(CancellationToken cancellationToken = default) =>
        _ = await RequestAsync<JsonElement>(HttpMethod.Post, "api/disconnect", new { }, cancellationToken);

    public Task<MenuEvents> GetEventsAsync(long since, CancellationToken cancellationToken = default)
    {
        if (since < 0)
            throw new ArgumentOutOfRangeException(nameof(since));
        return RequestAsync<MenuEvents>(HttpMethod.Get,
            "api/events?since=" + since.ToString(System.Globalization.CultureInfo.InvariantCulture), null, cancellationToken);
    }

    private async Task<T> RequestAsync<T>(HttpMethod method, string path, object? body,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _requests.WaitAsync(cancellationToken);
        try
        {
            using var request = new HttpRequestMessage(method, new Uri(_baseUri, path));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (body is not null)
                request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.Content.Headers.ContentLength > MaximumResponseBytes)
                throw new MenuApiException("Resposta do servidor maior que o limite permitido.", response.StatusCode);
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var bytes = new MemoryStream();
            byte[] buffer = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(buffer, timeout.Token)) != 0)
            {
                if (bytes.Length + count > MaximumResponseBytes)
                    throw new MenuApiException("Resposta do servidor maior que o limite permitido.", response.StatusCode);
                bytes.Write(buffer, 0, count);
            }
            if (!response.IsSuccessStatusCode)
            {
                string detail = SafeError(bytes.ToArray());
                throw new MenuApiException($"HTTP {(int)response.StatusCode}: {detail}", response.StatusCode);
            }
            if (response.Content.Headers.ContentType?.MediaType != "application/json")
                throw new MenuApiException("A URL respondeu fora do protocolo JSON. Confira o nome e a instalação do recurso.");
            try
            {
                return JsonSerializer.Deserialize<T>(bytes.ToArray(), JsonOptions)
                    ?? throw new MenuApiException("O servidor retornou uma resposta vazia.");
            }
            catch (JsonException)
            {
                throw new MenuApiException("O servidor retornou JSON inválido.");
            }
        }
        catch (HttpRequestException)
        {
            throw new MenuApiException("Falha de conexão ou de validação TLS. Confira a URL e o certificado do servidor.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new MenuApiException("O servidor não respondeu em 5 segundos.");
        }
        finally
        {
            _requests.Release();
        }
    }

    private string SafeError(byte[] body)
    {
        string message = "Pedido recusado pelo recurso.";
        try
        {
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (json.RootElement.TryGetProperty("message", out var item) && item.ValueKind == JsonValueKind.String)
                    message = item.GetString() ?? message;
                else if (json.RootElement.TryGetProperty("error", out item) && item.ValueKind == JsonValueKind.String)
                    message = item.GetString() ?? message;
            }
        }
        catch (JsonException) { }
        message = message.Replace(_token, "[token omitido]", StringComparison.Ordinal);
        message = new string(message.Where(c => !char.IsControl(c)).ToArray());
        return message.Length <= 220 ? message : message[..220];
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _http.Dispose();
        // Do not dispose _requests while an outstanding response is releasing it.
    }
}
