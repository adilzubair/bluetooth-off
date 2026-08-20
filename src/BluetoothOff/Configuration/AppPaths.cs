namespace BluetoothOff.Configuration;

internal sealed class AppPaths
{
    internal AppPaths(string? rootDirectory = null)
    {
        RootDirectory = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BluetoothOff");
        ConfigurationFile = Path.Combine(RootDirectory, "config.json");
        LogsDirectory = Path.Combine(RootDirectory, "Logs");
    }

    internal string RootDirectory { get; }

    internal string ConfigurationFile { get; }

    internal string LogsDirectory { get; }
}

