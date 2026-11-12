using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace PedeAqui.PaymentGateway.Tests;

public class PaymentGatewayTests : IDisposable
{
    private const string DefaultKey = "order-1";

    private readonly WebApplicationFactory<Program> factory = new();
    private readonly HttpClient client;

    public PaymentGatewayTests()
    {
        client = factory.CreateClient();
    }

    public void Dispose()
    {
        client.Dispose();
        factory.Dispose();
    }

    [Fact]
    public async Task Approves_payment()
    {
        var response = await PostPayment(100.00m);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payment = await response.Content.ReadFromJsonAsync<PaymentResponse>();
        Assert.NotNull(payment);
        Assert.NotEqual(Guid.Empty, payment.PaymentId);
        Assert.Equal("Approved", payment.Status);
        Assert.Equal(100.00m, payment.Amount);
    }

    [Fact]
    public async Task Declines_amount_with_13_cents()
    {
        var response = await PostPayment(50.13m);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payment = await response.Content.ReadFromJsonAsync<PaymentResponse>();
        Assert.Equal("Declined", payment!.Status);
    }

    [Fact]
    public async Task Same_key_returns_same_payment()
    {
        var first = await (await PostPayment(80.00m)).Content.ReadFromJsonAsync<PaymentResponse>();
        var second = await (await PostPayment(80.00m)).Content.ReadFromJsonAsync<PaymentResponse>();

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Same_key_different_amount_returns_422()
    {
        await PostPayment(80.00m);

        var response = await PostPayment(90.00m);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Missing_key_returns_400()
    {
        var response = await PostPayment(80.00m, key: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Non_positive_amount_returns_400(decimal amount)
    {
        var response = await PostPayment(amount);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_known_key_returns_payment()
    {
        var created = await (await PostPayment(80.00m)).Content.ReadFromJsonAsync<PaymentResponse>();

        var response = await client.GetAsync($"/payments/{DefaultKey}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(created, await response.Content.ReadFromJsonAsync<PaymentResponse>());
    }

    [Fact]
    public async Task Get_unknown_key_returns_404()
    {
        var response = await client.GetAsync("/payments/never-received");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Chaos_down_returns_503()
    {
        await ConfigureChaos(new { down = true });

        var response = await PostPayment(80.00m);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Chaos_failure_rate_one_returns_503()
    {
        await ConfigureChaos(new { failureRate = 1 });

        var response = await PostPayment(80.00m);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Chaos_slow_delays_response()
    {
        await ConfigureChaos(new { slowRate = 1, slowDelayMs = 300 });

        var stopwatch = Stopwatch.StartNew();
        var response = await PostPayment(80.00m);
        stopwatch.Stop();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(stopwatch.ElapsedMilliseconds >= 250, $"elapsed {stopwatch.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task Chaos_down_does_not_affect_chaos_and_health_endpoints()
    {
        await ConfigureChaos(new { down = true });

        var chaos = await client.GetAsync("/chaos");
        var health = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, chaos.StatusCode);
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    private async Task<HttpResponseMessage> PostPayment(decimal amount, string? key = DefaultKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/payments")
        {
            Content = JsonContent.Create(new { orderReference = "order-1", amount })
        };
        if (key is not null)
        {
            request.Headers.Add("Idempotency-Key", key);
        }
        return await client.SendAsync(request);
    }

    private async Task ConfigureChaos(object chaos)
    {
        var response = await client.PostAsJsonAsync("/chaos", chaos);
        response.EnsureSuccessStatusCode();
    }

    private record PaymentResponse(Guid PaymentId, string Status, decimal Amount);
}
