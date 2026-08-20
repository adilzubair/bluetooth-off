using BluetoothOff.Security;
using BluetoothOff.Services;

namespace BluetoothOff.Api;

internal sealed class ApiServer : IAsyncDisposable
{
    private readonly WebApplication _application;

    internal ApiServer(
        ApiServerOptions options,
        IBluetoothRadioController radio,
        ApiSecurityState securityState)
    {
        var builder = ApiApplication.CreateLoopbackBuilder(options);
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

