using System.Text.Json;

namespace BluetoothOff.Configuration;

internal sealed class AppConfigurationStore(AppPaths paths)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    internal async Task<AppConfiguration?> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(paths.ConfigurationFile))
        {
            return null;
        }

        await using var stream = new FileStream(
            paths.ConfigurationFile,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var configuration = await JsonSerializer.DeserializeAsync<AppConfiguration>(
            stream,
            SerializerOptions,
            cancellationToken);

        if (configuration is null)
        {
            throw new InvalidDataException("The application configuration file is empty.");
        }

        configuration.Validate();
        return configuration;
    }

    internal async Task SaveAsync(
        AppConfiguration configuration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        configuration.Validate();
        Directory.CreateDirectory(paths.RootDirectory);

        var temporaryFile = Path.Combine(
            paths.RootDirectory,
            $"config-{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = new FileStream(
                temporaryFile,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    configuration,
                    SerializerOptions,
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryFile, paths.ConfigurationFile, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryFile))
            {
                File.Delete(temporaryFile);
            }
        }
    }
}

