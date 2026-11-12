using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using PedeAqui.Domain.Payments;
using PedeAqui.Domain.Services;
using Polly;

namespace PedeAqui.Infrastructure.Payments;

internal sealed class HttpPaymentGateway : IPaymentGateway
{
    private const string IDEMPOTENCY_KEY_HEADER = "Idempotency-Key";
    private const string CORRELATION_ID_HEADER = "X-Correlation-Id";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly HttpClient _httpClient;
    private readonly ICorrelationContext _correlationContext;

    public HttpPaymentGateway(HttpClient httpClient, ICorrelationContext correlationContext)
    {
        _httpClient = httpClient;
        _correlationContext = correlationContext;
    }

    public async Task<PaymentResult> Charge(string idempotencyKey, string orderReference, decimal amount, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "payments")
        {
            Content = JsonContent.Create(new GatewayChargeRequest(orderReference, amount), options: SerializerOptions)
        };
        request.Headers.Add(IDEMPOTENCY_KEY_HEADER, idempotencyKey);

        using var response = await Send(request, cancellationToken);
        EnsureSuccess(response);

        return await ReadPayment(response, cancellationToken);
    }

    public async Task<PaymentResult?> Find(string idempotencyKey, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"payments/{Uri.EscapeDataString(idempotencyKey)}");

        using var response = await Send(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        EnsureSuccess(response);

        return await ReadPayment(response, cancellationToken);
    }

    private async Task<HttpResponseMessage> Send(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(_correlationContext.CorrelationId))
        {
            request.Headers.Add(CORRELATION_ID_HEADER, _correlationContext.CorrelationId);
        }

        try
        {
            return await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (ExecutionRejectedException exception)
        {
            throw new PaymentGatewayUnavailableException(exception);
        }
        catch (HttpRequestException exception)
        {
            throw new PaymentGatewayUnavailableException(exception);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new PaymentGatewayUnavailableException(exception);
        }
    }

    private static void EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        if (PaymentGatewayResilience.IsTransientStatusCode(response.StatusCode))
        {
            throw new PaymentGatewayUnavailableException(new HttpRequestException(
                $"Gateway de pagamento respondeu {(int)response.StatusCode}.", inner: null, response.StatusCode));
        }

        throw new PaymentGatewayRejectedException(response.StatusCode);
    }

    private static async Task<PaymentResult> ReadPayment(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var payment = await response.Content.ReadFromJsonAsync<GatewayPaymentResponse>(SerializerOptions, cancellationToken)
            ?? throw new PaymentGatewayUnavailableException(new JsonException("Gateway de pagamento respondeu sem corpo."));

        return new PaymentResult(payment.Status, payment.PaymentId);
    }

    private sealed record GatewayChargeRequest(string OrderReference, decimal Amount);

    private sealed record GatewayPaymentResponse(string PaymentId, PaymentStatus Status, decimal Amount);
}
