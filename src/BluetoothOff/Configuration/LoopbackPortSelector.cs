using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using BluetoothOff.Api;

namespace BluetoothOff.Configuration;

internal static class LoopbackPortSelector
{
    private const int MaximumAttempts = 128;

    internal static int SelectAvailablePort()
    {
        for (var attempt = 0; attempt < MaximumAttempts; attempt++)
        {
            var port = RandomNumberGenerator.GetInt32(
                ApiServerOptions.MinimumDynamicPort,
                ApiServerOptions.MaximumDynamicPort + 1);
            var listener = new TcpListener(IPAddress.Loopback, port);

            try
            {
                listener.Start();
                return port;
            }
            catch (SocketException)
            {
                // Try another cryptographically random dynamic port.
            }
            finally
            {
                listener.Stop();
            }
        }

        throw new InvalidOperationException("No available loopback port could be selected.");
    }
}

