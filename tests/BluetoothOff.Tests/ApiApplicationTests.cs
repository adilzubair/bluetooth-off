using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BluetoothOff.Api;
using BluetoothOff.Domain;
using BluetoothOff.Security;
using BluetoothOff.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;

namespace BluetoothOff.Tests;

[TestClass]
public sealed class ApiApplicationTests
{
    private const string ExpectedHost = "bluetooth-off-pc.example.ts.net";
    private const string ExpectedLogin = "person@example.com";
    private const string Token = "valid-test-token";

    [TestMethod]
    public async Task StatusRejectsMissingCredentials()
    {
        await using var fixture = await ApiFixture.StartAsync();

        using var response = await fixture.Client.GetAsync("/api/v1/status");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.AreEqual("Bearer", response.Headers.WwwAuthenticate.Single().Scheme);
    }

    [TestMethod]
    public async Task StatusRejectsWrongHostIdentityAndToken()
    {
        await using var fixture = await ApiFixture.StartAsync();

        using var wrongHost = ApiFixture.CreateRequest(HttpMethod.Get, "/api/v1/status");
        wrongHost.Headers.Host = "other.example.ts.net";
        using var hostResponse = await fixture.Client.SendAsync(wrongHost);

        using var wrongIdentity = ApiFixture.CreateRequest(HttpMethod.Get, "/api/v1/status");
        wrongIdentity.Headers.Remove(RequestAuthenticator.TailscaleLoginHeader);
        wrongIdentity.Headers.Add(RequestAuthenticator.TailscaleLoginHeader, "attacker@example.com");
        using var identityResponse = await fixture.Client.SendAsync(wrongIdentity);

        using var wrongToken = ApiFixture.CreateRequest(HttpMethod.Get, "/api/v1/status");
        wrongToken.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "wrong-token");
        using var tokenResponse = await fixture.Client.SendAsync(wrongToken);

        Assert.AreEqual(HttpStatusCode.Unauthorized, hostResponse.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, identityResponse.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, tokenResponse.StatusCode);
    }

    [TestMethod]
    public async Task StatusRejectsDuplicateAuthorizationHeaders()
    {
        await using var fixture = await ApiFixture.StartAsync();
        using var request = ApiFixture.CreateRequest(HttpMethod.Get, "/api/v1/status");
        request.Headers.Remove("Authorization");
        request.Headers.TryAddWithoutValidation(
            "Authorization",
            ["Bearer valid-test-token", "Bearer valid-test-token"]);

        using var response = await fixture.Client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task StatusReturnsCurrentStateWithoutChangingRadio()
    {
        var radio = new FakeController { State = BluetoothState.On };
        await using var fixture = await ApiFixture.StartAsync(radio);
        using var request = ApiFixture.CreateRequest(HttpMethod.Get, "/api/v1/status");

        using var response = await fixture.Client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<StatusResponse>();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsNotNull(body);
        Assert.AreEqual("on", body.State);
        Assert.AreEqual(0, radio.OffCallCount);
        Assert.AreEqual("no-store", response.Headers.CacheControl?.ToString());
    }

    [TestMethod]
    public async Task OffRejectsRequestBody()
    {
        await using var fixture = await ApiFixture.StartAsync();
        using var request = ApiFixture.CreateRequest(HttpMethod.Post, "/api/v1/bluetooth/off");
        request.Content = JsonContent.Create(new { command = "off" });

        using var response = await fixture.Client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.AreEqual("unexpected_body", body?.Code);
    }

    [TestMethod]
    public async Task OffReturnsConfirmedResult()
    {
        var radio = new FakeController { State = BluetoothState.On };
        await using var fixture = await ApiFixture.StartAsync(radio);
        using var request = ApiFixture.CreateRequest(HttpMethod.Post, "/api/v1/bluetooth/off");

        using var response = await fixture.Client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<OffResponse>();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsNotNull(body);
        Assert.AreEqual("off", body.State);
        Assert.IsTrue(body.Changed);
        Assert.AreEqual(1, radio.OffCallCount);
    }

    [TestMethod]
    public async Task OffRateLimitRejectsSixthRequestInWindow()
    {
        await using var fixture = await ApiFixture.StartAsync();
        HttpResponseMessage? finalResponse = null;

        try
        {
            for (var requestNumber = 0; requestNumber < 6; requestNumber++)
            {
                using var request = ApiFixture.CreateRequest(HttpMethod.Post, "/api/v1/bluetooth/off");
                finalResponse?.Dispose();
                finalResponse = await fixture.Client.SendAsync(request);
            }

            Assert.IsNotNull(finalResponse);
            Assert.AreEqual(HttpStatusCode.TooManyRequests, finalResponse.StatusCode);
        }
        finally
        {
            finalResponse?.Dispose();
        }
    }

    [TestMethod]
    public async Task UnsupportedMethodReturnsMethodNotAllowed()
    {
        await using var fixture = await ApiFixture.StartAsync();
        using var request = ApiFixture.CreateRequest(HttpMethod.Put, "/api/v1/status");

        using var response = await fixture.Client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [TestMethod]
    public async Task BluetoothFailuresReturnSanitizedServiceUnavailable()
    {
        var radio = new FakeController
        {
            Failure = new BluetoothControlException(
                BluetoothFailureCode.RadioUnavailable,
                "sensitive internal detail"),
        };
        await using var fixture = await ApiFixture.StartAsync(radio);
        using var request = ApiFixture.CreateRequest(HttpMethod.Get, "/api/v1/status");

        using var response = await fixture.Client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        StringAssert.Contains(text, "radio_unavailable");
        Assert.IsFalse(text.Contains("sensitive internal detail", StringComparison.Ordinal));
        Assert.IsTrue(response.Headers.Contains("X-Correlation-ID"));
    }

    [TestMethod]
    public async Task UnexpectedFailuresReturnSanitizedInternalError()
    {
        var radio = new FakeController
        {
            Failure = new InvalidOperationException("secret exception detail"),
        };
        await using var fixture = await ApiFixture.StartAsync(radio);
        using var request = ApiFixture.CreateRequest(HttpMethod.Get, "/api/v1/status");

        using var response = await fixture.Client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.InternalServerError, response.StatusCode);
        StringAssert.Contains(text, "internal_error");
        Assert.IsFalse(text.Contains("secret exception detail", StringComparison.Ordinal));
    }

    private sealed class ApiFixture : IAsyncDisposable
    {
        private readonly WebApplication _application;

        private ApiFixture(WebApplication application, HttpClient client)
        {
            _application = application;
            Client = client;
        }

        internal HttpClient Client { get; }

        internal static async Task<ApiFixture> StartAsync(FakeController? radio = null)
        {
            radio ??= new FakeController();
            var security = new ApiSecurityState(
                ExpectedHost,
                CredentialHasher.HashLogin(ExpectedLogin),
                CredentialHasher.HashExact(Token));
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseTestServer();
            ApiApplication.AddServices(builder);
            var app = builder.Build();
            ApiApplication.Map(app, radio, security);
            await app.StartAsync();
            return new ApiFixture(app, app.GetTestClient());
        }

        internal static HttpRequestMessage CreateRequest(HttpMethod method, string path)
        {
            var request = new HttpRequestMessage(method, path);
            request.Headers.Host = ExpectedHost;
            request.Headers.Add(RequestAuthenticator.TailscaleLoginHeader, ExpectedLogin);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
            return request;
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _application.DisposeAsync();
        }
    }

    private sealed class FakeController : IBluetoothRadioController
    {
        internal Exception? Failure { get; init; }

        internal int OffCallCount { get; private set; }

        internal BluetoothState State { get; set; } = BluetoothState.Off;

        public bool IsAuthorized => true;

        public Task AuthorizeAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task<BluetoothStatus> GetStatusAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Failure is null
                ? Task.FromResult(new BluetoothStatus(State, DateTimeOffset.UtcNow))
                : Task.FromException<BluetoothStatus>(Failure);
        }

        public Task<BluetoothOffResult> TurnOffAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Failure is not null)
            {
                return Task.FromException<BluetoothOffResult>(Failure);
            }

            OffCallCount++;
            var changed = State != BluetoothState.Off;
            State = BluetoothState.Off;
            return Task.FromResult(new BluetoothOffResult(State, changed, DateTimeOffset.UtcNow));
        }
    }
}
