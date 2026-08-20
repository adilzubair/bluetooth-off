using System.Globalization;
using System.Threading.RateLimiting;
using BluetoothOff.Domain;
using BluetoothOff.Security;
using BluetoothOff.Services;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;

namespace BluetoothOff.Api;

internal static class ApiApplication
{
    private const string StatusRateLimit = "status";
    private const string OffRateLimit = "off";
    private static readonly Action<ILogger, string, Exception?> LogUnhandledFailure =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(1001, "UnhandledApiFailure"),
            "Unhandled API failure {CorrelationId}");

    internal static void AddServices(WebApplicationBuilder builder)
    {
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = static async (context, cancellationToken) =>
            {
                var correlationId = GetCorrelationId(context.HttpContext);
                await Results.Json(
                    new ErrorResponse("rate_limited", correlationId),
                    statusCode: StatusCodes.Status429TooManyRequests)
                    .ExecuteAsync(context.HttpContext);
            };

            options.AddFixedWindowLimiter(StatusRateLimit, limiter =>
            {
                limiter.PermitLimit = 30;
                limiter.Window = TimeSpan.FromMinutes(1);
                limiter.QueueLimit = 0;
                limiter.AutoReplenishment = true;
            });

            options.AddFixedWindowLimiter(OffRateLimit, limiter =>
            {
                limiter.PermitLimit = 5;
                limiter.Window = TimeSpan.FromMinutes(1);
                limiter.QueueLimit = 0;
                limiter.AutoReplenishment = true;
            });
        });
    }

    internal static void Map(
        WebApplication app,
        IBluetoothRadioController radio,
        ApiSecurityState securityState)
    {
        var authenticator = new RequestAuthenticator(securityState);

        app.Use(async (context, next) =>
        {
            var correlationId = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(8))
                .ToLowerInvariant();
            context.Items["CorrelationId"] = correlationId;
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.Pragma = "no-cache";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            context.Response.Headers.Append("X-Correlation-ID", correlationId);
            context.Response.Headers.Append("Content-Security-Policy", "default-src 'none'; frame-ancestors 'none'");

            try
            {
                await next(context);
            }
            catch (BluetoothControlException exception)
            {
                var code = MapFailureCode(exception.Code);
                await WriteErrorAsync(context, StatusCodes.Status503ServiceUnavailable, code);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                // The caller disconnected; do not attempt to write another response.
            }
            catch (Exception exception)
            {
                LogUnhandledFailure(app.Logger, correlationId, exception);
                await WriteErrorAsync(context, StatusCodes.Status500InternalServerError, "internal_error");
            }
        });

        app.Use(async (context, next) =>
        {
            if (!authenticator.IsAuthorized(context.Request))
            {
                context.Response.Headers.WWWAuthenticate = "Bearer";
                await WriteErrorAsync(context, StatusCodes.Status401Unauthorized, "unauthorized");
                return;
            }

            await next(context);
        });

        app.UseRateLimiter();

        app.MapGet("/api/v1/status", async (CancellationToken cancellationToken) =>
            {
                var status = await radio.GetStatusAsync(cancellationToken);
                return Results.Json(new StatusResponse(
                    BluetoothStateWireFormat.Format(status.State),
                    status.ObservedAt));
            })
            .RequireRateLimiting(StatusRateLimit);

        app.MapPost("/api/v1/bluetooth/off", async (HttpContext context, CancellationToken cancellationToken) =>
            {
                if (HasRequestBody(context.Request))
                {
                    return Results.Json(
                        new ErrorResponse("unexpected_body", GetCorrelationId(context)),
                        statusCode: StatusCodes.Status400BadRequest);
                }

                var result = await radio.TurnOffAsync(cancellationToken);
                return Results.Json(new OffResponse(
                    "off",
                    BluetoothStateWireFormat.Format(result.State),
                    result.Changed,
                    result.ObservedAt));
            })
            .RequireRateLimiting(OffRateLimit);
    }

    internal static WebApplicationBuilder CreateLoopbackBuilder(ApiServerOptions options)
    {
        options.Validate();
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.ConfigureKestrel(server =>
        {
            server.AddServerHeader = false;
            server.Listen(System.Net.IPAddress.Loopback, options.Port);
            server.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(30);
            server.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10);
            server.Limits.MaxRequestBodySize = 1024;
            server.Limits.MaxRequestHeaderCount = 32;
            server.Limits.MaxRequestHeadersTotalSize = 8192;
        });
        return builder;
    }

    private static bool HasRequestBody(HttpRequest request)
    {
        if (request.ContentLength.GetValueOrDefault() > 0)
        {
            return true;
        }

        return request.Headers.ContainsKey("Transfer-Encoding");
    }

    private static string MapFailureCode(BluetoothFailureCode code)
    {
        return code switch
        {
            BluetoothFailureCode.PermissionDenied => "permission_denied",
            BluetoothFailureCode.RadioUnavailable => "radio_unavailable",
            BluetoothFailureCode.PolicyRestricted => "policy_restricted",
            BluetoothFailureCode.RadioDisabled => "radio_disabled",
            BluetoothFailureCode.StateNotConfirmed => "state_not_confirmed",
            _ => "bluetooth_error",
        };
    }

    private static string GetCorrelationId(HttpContext context)
    {
        return Convert.ToString(context.Items["CorrelationId"], CultureInfo.InvariantCulture)
            ?? "unavailable";
    }

    private static async Task WriteErrorAsync(HttpContext context, int statusCode, string code)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.ContentLength = null;
        context.Response.ContentType = null;
        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsJsonAsync(
            new ErrorResponse(code, GetCorrelationId(context)),
            context.RequestAborted);
    }
}
