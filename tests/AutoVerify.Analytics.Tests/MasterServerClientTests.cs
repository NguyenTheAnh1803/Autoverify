using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AutoVerify.Api.Integration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AutoVerify.Analytics.Tests;

public sealed class MasterServerClientTests
{
    private sealed class CapturingHandler(RSA masterKey, Func<HttpRequestMessage, string, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body, JsonElement Envelope)> Calls { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/api/access/public-key")
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(masterKey.ExportSubjectPublicKeyInfoPem()) };

            var body = request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            var header = request.Headers.GetValues("X-RSA-Access").Single();
            var plain = masterKey.Decrypt(Convert.FromBase64String(header), RSAEncryptionPadding.OaepSHA256);
            Calls.Add((request, body, JsonDocument.Parse(plain).RootElement.Clone()));
            return respond(request, body);
        }
    }

    private static MasterServerClient CreateClient(CapturingHandler handler, string apiKey = "key-123") =>
        new(new HttpClient(handler),
            Options.Create(new MasterServerOptions { BaseUrl = "http://master:336", ClientId = "autoverify", ApiKey = apiKey }),
            NullLogger<MasterServerClient>.Instance);

    [Fact]
    public async Task SubmitJob_SendsEnvelopeBoundToMethodPathAndBodyHash()
    {
        using var rsa = RSA.Create(3072);
        var handler = new CapturingHandler(rsa, (_, _) => new HttpResponseMessage(HttpStatusCode.Accepted)
        {
            Content = new StringContent("""{"jobId":"abc123","itemCount":1}""")
        });

        var jobId = await CreateClient(handler).SubmitJobAsync(
            "overkill:run-1", new[] { new MasterVerifyItem("d1", "L01", "P-A", new byte[] { 1, 2, 3 }) }, CancellationToken.None);

        Assert.Equal("abc123", jobId);
        var (request, body, envelope) = Assert.Single(handler.Calls);
        Assert.Equal("POST", envelope.GetProperty("method").GetString());
        Assert.Equal("/api/verify/jobs", envelope.GetProperty("path").GetString());
        Assert.Equal("autoverify", envelope.GetProperty("clientId").GetString());
        Assert.Equal("key-123", envelope.GetProperty("apiKey").GetString());
        Assert.True(envelope.GetProperty("nonce").GetString()!.Length >= 16);
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))).ToLowerInvariant(),
            envelope.GetProperty("bodySha256").GetString());
        Assert.Equal("/api/verify/jobs", request.RequestUri!.AbsolutePath);

        using var parsed = JsonDocument.Parse(body);
        var item = parsed.RootElement.GetProperty("items")[0];
        Assert.Equal("d1", item.GetProperty("externalItemId").GetString());
        Assert.Equal(new byte[] { 1, 2, 3 }, item.GetProperty("imageBuffer").GetBytesFromBase64());
    }

    [Fact]
    public async Task EachRequestUsesAFreshNonce()
    {
        using var rsa = RSA.Create(3072);
        var handler = new CapturingHandler(rsa, (_, _) => new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("""{"error":"JOB_NOT_FOUND"}""")
        });
        var client = CreateClient(handler);

        await client.TestConnectionAsync(CancellationToken.None);
        await client.TestConnectionAsync(CancellationToken.None);

        var nonces = handler.Calls.Select(c => c.Envelope.GetProperty("nonce").GetString()).ToList();
        Assert.Equal(2, nonces.Distinct().Count());
    }

    [Fact]
    public async Task TestConnection_IsAuthorizedOnlyWhenMasterAnswersJobNotFound()
    {
        using var rsa = RSA.Create(3072);
        var ok = await CreateClient(new CapturingHandler(rsa, (_, _) => new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("""{"error":"JOB_NOT_FOUND"}""")
        })).TestConnectionAsync(CancellationToken.None);
        Assert.True(ok.Reachable);
        Assert.True(ok.Authorized);

        var denied = await CreateClient(new CapturingHandler(rsa, (_, _) => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("""{"error":"ACCESS_DENIED","message":"API_KEY_INVALID"}""")
        })).TestConnectionAsync(CancellationToken.None);
        Assert.True(denied.Reachable);
        Assert.False(denied.Authorized);
    }

    [Fact]
    public async Task TestConnection_ReportsNotConfiguredWithoutCallingMaster()
    {
        var client = new MasterServerClient(
            new HttpClient(new CapturingHandler(RSA.Create(3072), (_, _) => throw new InvalidOperationException("must not be called"))),
            Options.Create(new MasterServerOptions()), NullLogger<MasterServerClient>.Instance);

        var status = await client.TestConnectionAsync(CancellationToken.None);

        Assert.False(status.Reachable);
        Assert.False(client.IsConfigured);
    }
}
