using System.Diagnostics;
using System.Globalization;
using System.Text;
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
            cancellationToken,
            detectServeConsent: true);
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

    internal static bool TryGetServeActivationUri(string output, out Uri? activationUri)
    {
        const string prefix = "https://login.tailscale.com/f/serve?node=";
        activationUri = null;

        var start = output.IndexOf(prefix, StringComparison.Ordinal);
        if (start < 0)
        {
            return false;
        }

        var end = start;
        while (end < output.Length && !char.IsWhiteSpace(output[end]))
        {
            end++;
        }

        var candidate = output[start..end];
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var parsed)
            || !string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
            || !string.Equals(parsed.IdnHost, "login.tailscale.com", StringComparison.Ordinal)
            || !parsed.IsDefaultPort
            || parsed.UserInfo.Length != 0
            || !string.Equals(parsed.AbsolutePath, "/f/serve", StringComparison.Ordinal)
            || parsed.Fragment.Length != 0
            || !parsed.Query.StartsWith("?node=", StringComparison.Ordinal))
        {
            return false;
        }

        var node = parsed.Query.AsSpan("?node=".Length);
        if (node.Length is < 8 or > 128)
        {
            return false;
        }

        foreach (var character in node)
        {
            if (!char.IsAsciiLetterOrDigit(character))
            {
                return false;
            }
        }

        activationUri = parsed;
        return true;
    }

    private async Task<ProcessResult> RunAsync(
        IReadOnlyCollection<string> arguments,
        CancellationToken cancellationToken,
        bool detectServeConsent = false)
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

        var output = new StringBuilder();
        var error = new StringBuilder();
        var outputLock = new object();
        var errorLock = new object();
        var serveConsent = new TaskCompletionSource<Uri>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        process.OutputDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is null)
            {
                return;
            }

            AppendCapturedLine(output, outputLock, eventArgs.Data);
            DetectServeConsent(eventArgs.Data, detectServeConsent, serveConsent);
        };
        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is null)
            {
                return;
            }

            AppendCapturedLine(error, errorLock, eventArgs.Data);
            DetectServeConsent(eventArgs.Data, detectServeConsent, serveConsent);
        };

        try
        {
            if (!process.Start())
            {
                throw new TailscaleException("The Tailscale command could not be started.");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(CommandTimeout);

            try
            {
                var exitTask = process.WaitForExitAsync(timeout.Token);
                if (detectServeConsent)
                {
                    var completed = await Task.WhenAny(exitTask, serveConsent.Task);
                    if (completed == serveConsent.Task)
                    {
                        Terminate(process);
                        throw new TailscaleServeConsentRequiredException(
                            await serveConsent.Task);
                    }
                }

                await exitTask;
                process.WaitForExit();
                return new ProcessResult(
                    process.ExitCode,
                    Snapshot(output, outputLock),
                    Snapshot(error, errorLock));
            }
            catch (OperationCanceledException)
            {
                Terminate(process);

                if (detectServeConsent
                    && TryGetServeActivationUri(
                        string.Concat(
                            Snapshot(output, outputLock),
                            Environment.NewLine,
                            Snapshot(error, errorLock)),
                        out var activationUri)
                    && activationUri is not null)
                {
                    throw new TailscaleServeConsentRequiredException(activationUri);
                }

                cancellationToken.ThrowIfCancellationRequested();
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

    private static void DetectServeConsent(
        string line,
        bool enabled,
        TaskCompletionSource<Uri> completion)
    {
        if (enabled
            && TryGetServeActivationUri(line, out var activationUri)
            && activationUri is not null)
        {
            completion.TrySetResult(activationUri);
        }
    }

    private static void AppendCapturedLine(
        StringBuilder builder,
        object syncRoot,
        string line)
    {
        lock (syncRoot)
        {
            if (builder.Length >= MaximumCapturedCharacters)
            {
                return;
            }

            var remaining = MaximumCapturedCharacters - builder.Length;
            var characterCount = Math.Min(line.Length, remaining);
            builder.Append(line.AsSpan(0, characterCount));
            if (builder.Length < MaximumCapturedCharacters)
            {
                builder.AppendLine();
            }
        }
    }

    private static string Snapshot(StringBuilder builder, object syncRoot)
    {
        lock (syncRoot)
        {
            return builder.ToString();
        }
    }

    private static void Terminate(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            process.WaitForExit(5_000);
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or System.ComponentModel.Win32Exception)
        {
            // The process exited between the state check and termination.
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

    private static void EnsureSuccess(ProcessResult result, string safeMessage)
    {
        if (result.ExitCode != 0)
        {
            throw new TailscaleException(safeMessage);
        }
    }
}
