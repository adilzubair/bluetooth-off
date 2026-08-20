using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace BluetoothOff.Tailscale;

internal sealed class TailscaleClient
{
    internal const string NeutralHostname = "bluetooth-off-pc";
    private const int MaximumCapturedCharacters = 262_144;
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(30);
    private readonly string? _configuredExecutablePath;

    internal TailscaleClient(string? executablePath = null)
    {
        _configuredExecutablePath = executablePath;
    }

    internal bool IsInstalled => ResolveExecutable() is not null;

    internal async Task<TailscaleStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var result = await RunAsync(["status", "--json"], cancellationToken);
        EnsureSuccess(result, "Tailscale status could not be read.");

        try
        {
            return ParseStatusJson(result.StandardOutput);
        }
        catch (JsonException exception)
        {
            throw new TailscaleException("Tailscale returned an invalid status response.", exception);
        }
    }

    internal async Task SetNeutralHostnameAsync(CancellationToken cancellationToken)
    {
        var result = await RunAsync(
            ["set", $"--hostname={NeutralHostname}"],
            cancellationToken);
        EnsureSuccess(result, "The neutral Tailscale hostname could not be configured.");
    }

    internal async Task EnsureNoExistingExposureAsync(CancellationToken cancellationToken)
    {
        var funnel = await RunAsync(["funnel", "status", "--json"], cancellationToken);
        if (funnel.ExitCode == 0 && HasConfiguration(funnel.StandardOutput))
        {
            throw new TailscaleException(
                "Tailscale Funnel is configured on this PC. Remove it before setting up Bluetooth Off.");
        }

        var serve = await RunAsync(["serve", "status", "--json"], cancellationToken);
        if (serve.ExitCode == 0 && HasConfiguration(serve.StandardOutput))
        {
            throw new TailscaleException(
                "A Tailscale Serve configuration already exists. Bluetooth Off will not overwrite it.");
        }
    }

    internal async Task ConfigurePrivateServeAsync(int loopbackPort, CancellationToken cancellationToken)
    {
        new Api.ApiServerOptions(loopbackPort).Validate();
        var target = string.Create(
            CultureInfo.InvariantCulture,
            $"http://127.0.0.1:{loopbackPort}");
        var result = await RunAsync(
            ["serve", "--bg", "--yes", "--https=443", target],
            cancellationToken);
        EnsureSuccess(
            result,
            "Tailscale Serve could not be enabled. Open the Tailscale admin link shown by the CLI, enable HTTPS/Serve, and retry.");
    }

    internal async Task VerifyPrivateServeAsync(int loopbackPort, CancellationToken cancellationToken)
    {
        var funnel = await RunAsync(["funnel", "status", "--json"], cancellationToken);
        if (funnel.ExitCode == 0 && HasConfiguration(funnel.StandardOutput))
        {
            throw new TailscaleException("Tailscale Funnel must remain disabled.");
        }

        var serve = await RunAsync(["serve", "status", "--json"], cancellationToken);
        EnsureSuccess(serve, "Tailscale Serve status could not be verified.");

        var expectedTarget = string.Create(
            CultureInfo.InvariantCulture,
            $"http://127.0.0.1:{loopbackPort}");
        if (!JsonContainsValue(serve.StandardOutput, expectedTarget))
        {
            throw new TailscaleException(
                "Tailscale Serve is not proxying to the configured Bluetooth Off loopback port.");
        }
    }

    internal async Task DisableOwnedServeAsync(int loopbackPort, CancellationToken cancellationToken)
    {
        await VerifyPrivateServeAsync(loopbackPort, cancellationToken);
        var result = await RunAsync(
            ["serve", "--https=443", "off"],
            cancellationToken);
        EnsureSuccess(result, "The Bluetooth Off Tailscale Serve mapping could not be removed.");
    }

    internal static TailscaleStatus ParseStatusJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var backendState = GetRequiredString(root, "BackendState");
        var version = GetRequiredString(root, "Version");
        var self = root.GetProperty("Self");
        var dnsName = GetRequiredString(self, "DNSName").TrimEnd('.');
        var userId = self.GetProperty("UserID").GetInt64().ToString(CultureInfo.InvariantCulture);
        var users = root.GetProperty("User");

        if (!users.TryGetProperty(userId, out var user))
        {
            throw new JsonException("The Tailscale user profile is missing.");
        }

        var loginName = GetRequiredString(user, "LoginName");
        var online = self.TryGetProperty("Online", out var onlineElement)
            && onlineElement.ValueKind == JsonValueKind.True;

        return new TailscaleStatus(
            version,
            string.Equals(backendState, "Running", StringComparison.OrdinalIgnoreCase) && online,
            dnsName,
            loginName);
    }

    internal static bool HasConfiguration(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind switch
            {
                JsonValueKind.Object => document.RootElement.EnumerateObject().Any(),
                JsonValueKind.Array => document.RootElement.GetArrayLength() > 0,
                JsonValueKind.Null => false,
                _ => true,
            };
        }
        catch (JsonException)
        {
            return true;
        }
    }

    internal static bool JsonContainsValue(string json, string expected)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return ContainsValue(document.RootElement, expected);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private async Task<ProcessResult> RunAsync(
        IReadOnlyCollection<string> arguments,
        CancellationToken cancellationToken)
    {
        var executablePath = ResolveExecutable();
        if (executablePath is null)
        {
            throw new TailscaleException(
                "Tailscale is not installed. Install it from https://tailscale.com/download/windows and sign in first.");
        }

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        try
        {
            if (!process.Start())
            {
                throw new TailscaleException("The Tailscale command could not be started.");
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(CommandTimeout);
            var standardOutputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var standardErrorTask = process.StandardError.ReadToEndAsync(timeout.Token);

            try
            {
                await process.WaitForExitAsync(timeout.Token);
                var standardOutput = await standardOutputTask;
                var standardError = await standardErrorTask;
                return new ProcessResult(
                    process.ExitCode,
                    Limit(standardOutput),
                    Limit(standardError));
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                process.Kill(entireProcessTree: true);
                throw new TailscaleException("The Tailscale command timed out.");
            }
        }
        catch (TailscaleException)
        {
            throw;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            throw new TailscaleException("The Tailscale command could not be executed.", exception);
        }
    }

    private static string? FindExecutable()
    {
        var candidates = new List<string>
        {
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Tailscale",
                "tailscale.exe"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Tailscale",
                "tailscale.exe"),
        };

        var path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(path))
        {
            candidates.AddRange(path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Select(static directory => Path.Combine(directory.Trim(), "tailscale.exe")));
        }

        return candidates.FirstOrDefault(File.Exists);
    }

    private string? ResolveExecutable()
    {
        if (_configuredExecutablePath is not null)
        {
            return File.Exists(_configuredExecutablePath) ? _configuredExecutablePath : null;
        }

        return FindExecutable();
    }

    private static string GetRequiredString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new JsonException($"Required Tailscale field '{propertyName}' is missing.");
        }

        return property.GetString()!;
    }

    private static bool ContainsValue(JsonElement element, string expected)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => string.Equals(
                element.GetString()?.TrimEnd('/'),
                expected.TrimEnd('/'),
                StringComparison.OrdinalIgnoreCase),
            JsonValueKind.Object => element.EnumerateObject()
                .Any(property => ContainsValue(property.Value, expected)),
            JsonValueKind.Array => element.EnumerateArray()
                .Any(item => ContainsValue(item, expected)),
            _ => false,
        };
    }

    private static string Limit(string value)
    {
        return value.Length <= MaximumCapturedCharacters
            ? value
            : value[..MaximumCapturedCharacters];
    }

    private static void EnsureSuccess(ProcessResult result, string safeMessage)
    {
        if (result.ExitCode != 0)
        {
            throw new TailscaleException(safeMessage);
        }
    }
}
