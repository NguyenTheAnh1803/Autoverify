using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace AutoVerify.Api.Integration;

public sealed record MasterVerifyItem(string ExternalItemId, string StationId, string ProductId, byte[] ImageBuffer);

public sealed record MasterVerifyItemResult(
    string ExternalItemId, string Status, string? Verdict, double? AnomalyScore, string? DefectClass,
    string? BoxesJson, string? ModelVersionsJson, string? ClientMode, string? Error);

public sealed record MasterJobSummary(string JobId, string Status, int Total, int Pending, int Done, int Failed);

public sealed record MasterConnectionStatus(bool Reachable, bool Authorized, string Message);

public interface IMasterServerClient
{
    bool IsConfigured { get; }
    int MaxItemsPerJob { get; }
    Task<string> SubmitJobAsync(string? sourceRef, IReadOnlyList<MasterVerifyItem> items, CancellationToken cancellationToken);
    Task<MasterJobSummary> GetJobAsync(string jobId, CancellationToken cancellationToken);
    Task<IReadOnlyList<MasterVerifyItemResult>> GetItemsAsync(string jobId, CancellationToken cancellationToken);
    Task<MasterConnectionStatus> TestConnectionAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Talks to the Master Server using its RSA access envelope: every /api request carries an X-RSA-Access header holding a
/// timestamp, nonce, method, path, SHA-256 of the body and the API key, encrypted with the Master's public key.
/// </summary>
public sealed class MasterServerClient : IMasterServerClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;
    private readonly MasterServerOptions _options;
    private readonly ILogger<MasterServerClient> _logger;
    private readonly SemaphoreSlim _keyLock = new(1, 1);
    private RSA? _publicKey;

    public MasterServerClient(HttpClient http, IOptions<MasterServerOptions> options, ILogger<MasterServerClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public bool IsConfigured => _options.IsConfigured;
    public int MaxItemsPerJob => Math.Max(1, _options.MaxItemsPerJob);

    public async Task<string> SubmitJobAsync(string? sourceRef, IReadOnlyList<MasterVerifyItem> items, CancellationToken cancellationToken)
    {
        if (items.Count == 0) throw new ArgumentException("At least one item is required.", nameof(items));
        if (items.Count > _options.MaxItemsPerJob)
            throw new ArgumentException($"A job holds at most {_options.MaxItemsPerJob} items.", nameof(items));

        var payload = new
        {
            sourceRef,
            items = items.Select(i => new { i.ExternalItemId, i.StationId, i.ProductId, i.ImageBuffer })
        };
        using var response = await SendAsync(HttpMethod.Post, "/api/verify/jobs", payload, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, cancellationToken);
        return body.GetProperty("jobId").GetString() ?? throw new InvalidOperationException("Master returned no jobId.");
    }

    public async Task<MasterJobSummary> GetJobAsync(string jobId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, $"/api/verify/jobs/{Uri.EscapeDataString(jobId)}", null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var e = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, cancellationToken);
        return new MasterJobSummary(
            e.GetProperty("jobId").GetString()!, e.GetProperty("status").GetString()!,
            e.GetProperty("total").GetInt32(), e.GetProperty("pending").GetInt32(),
            e.GetProperty("done").GetInt32(), e.GetProperty("failed").GetInt32());
    }

    public async Task<IReadOnlyList<MasterVerifyItemResult>> GetItemsAsync(string jobId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, $"/api/verify/jobs/{Uri.EscapeDataString(jobId)}/items", null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var array = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, cancellationToken);
        var results = new List<MasterVerifyItemResult>();
        foreach (var e in array.EnumerateArray())
        {
            string? Str(string name) => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
            results.Add(new MasterVerifyItemResult(
                Str("externalItemId")!, Str("status")!, Str("verdict"),
                e.TryGetProperty("anomalyScore", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetDouble() : null,
                Str("defectClass"), Str("boxesJson"), Str("modelVersionsJson"), Str("clientMode"), Str("error")));
        }
        return results;
    }

    /// <summary>
    /// Reachability + authorization probe. Asking for a job that cannot exist is answered 404 JOB_NOT_FOUND only after the
    /// Master accepted the IP, the envelope, the API key and the verify.read scope; an empty job then proves verify.submit.
    /// Neither probe has side effects.
    /// </summary>
    public async Task<MasterConnectionStatus> TestConnectionAsync(CancellationToken cancellationToken)
    {
        if (!IsConfigured) return new MasterConnectionStatus(false, false, "MasterServer:BaseUrl is not configured.");
        try
        {
            using var response = await SendAsync(HttpMethod.Get, "/api/verify/jobs/connection-test", null, cancellationToken);
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!((int)response.StatusCode == 404 && text.Contains("JOB_NOT_FOUND", StringComparison.Ordinal)))
                return new MasterConnectionStatus(true, false, $"Master answered HTTP {(int)response.StatusCode}: {Truncate(text)}");

            // An empty job passes the scope check and is then refused with ITEMS_REQUIRED, so it proves verify.submit without creating anything.
            using var submitProbe = await SendAsync(HttpMethod.Post, "/api/verify/jobs", new { items = Array.Empty<object>() }, cancellationToken);
            var submitText = await submitProbe.Content.ReadAsStringAsync(cancellationToken);
            if ((int)submitProbe.StatusCode == 400 && submitText.Contains("ITEMS_REQUIRED", StringComparison.Ordinal))
                return new MasterConnectionStatus(true, true, "Master accepted the access envelope and the verify.read and verify.submit scopes.");
            return new MasterConnectionStatus(true, false,
                $"Master accepted verify.read but not verify.submit (HTTP {(int)submitProbe.StatusCode}): {Truncate(submitText)}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or CryptographicException)
        {
            return new MasterConnectionStatus(false, false, $"Cannot reach Master at {_options.BaseUrl}: {ex.Message}");
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? payload, CancellationToken cancellationToken)
    {
        var body = payload == null ? string.Empty : JsonSerializer.Serialize(payload, JsonOptions);
        var envelope = new
        {
            clientId = _options.ClientId,
            timestampUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            nonce = Guid.NewGuid().ToString("N"),
            method = method.Method,
            path,
            bodySha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))).ToLowerInvariant(),
            apiKey = _options.ApiKey
        };
        var key = await GetPublicKeyAsync(cancellationToken);
        var encrypted = key.Encrypt(JsonSerializer.SerializeToUtf8Bytes(envelope), RSAEncryptionPadding.OaepSHA256);

        var request = new HttpRequestMessage(method, $"{_options.BaseUrl.TrimEnd('/')}{path}");
        request.Headers.Add("X-RSA-Access", Convert.ToBase64String(encrypted));
        if (payload != null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        return await _http.SendAsync(request, cancellationToken);
    }

    private async Task<RSA> GetPublicKeyAsync(CancellationToken cancellationToken)
    {
        if (_publicKey != null) return _publicKey;
        await _keyLock.WaitAsync(cancellationToken);
        try
        {
            if (_publicKey != null) return _publicKey;
            var pem = await _http.GetStringAsync($"{_options.BaseUrl.TrimEnd('/')}/api/access/public-key", cancellationToken);
            var rsa = RSA.Create();
            rsa.ImportFromPem(pem);
            _publicKey = rsa;
            return rsa;
        }
        finally { _keyLock.Release(); }
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogWarning("Master Server rejected request: HTTP {Status} {Body}", (int)response.StatusCode, Truncate(text));
        throw new HttpRequestException($"Master Server answered HTTP {(int)response.StatusCode}: {Truncate(text)}", null, response.StatusCode);
    }

    private static string Truncate(string s) => s.Length <= 300 ? s : s[..300];
}
