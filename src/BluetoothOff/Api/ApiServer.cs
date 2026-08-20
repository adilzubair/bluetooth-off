using BluetoothOff.Security;
using BluetoothOff.Services;
using Microsoft.Extensions.Logging;

namespace BluetoothOff.Api;

internal sealed class ApiServer : IAsyncDisposable
{
    private readonly WebApplication _application;

    internal ApiServer(
        ApiServerOptions options,
        IBluetoothRadioController radio,
        ApiSecurityState securityState,
        ILoggerProvider? loggerProvider = null)
    {
        var builder = ApiApplication.CreateLoopbackBuilder(options);
        builder.Logging.ClearProviders();
        if (loggerProvider is not null)
        {
            builder.Logging.AddProvider(loggerProvider);
        }

        ApiApplication.AddServices(builder);
        _application = builder.Build();
        ApiApplication.Map(_application, radio, securityState);
    }

    internal Task StartAsync(CancellationToken cancellationToken)
    {
        return _application.StartAsync(cancellationToken);
    }

    internal Task StopAsync(CancellationToken cancellationToken)
    {
        return _application.StopAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _application.DisposeAsync();
    }
}
