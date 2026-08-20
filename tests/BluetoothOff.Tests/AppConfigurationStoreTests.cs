using BluetoothOff.Configuration;
using BluetoothOff.Security;

namespace BluetoothOff.Tests;

[TestClass]
public sealed class AppConfigurationStoreTests
{
    private string? _temporaryDirectory;

    [TestInitialize]
    public void Initialize()
    {
        _temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "BluetoothOff.Tests",
            Guid.NewGuid().ToString("N"));
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (_temporaryDirectory is not null && Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task SaveAndLoadRoundTripsOnlyCredentialHashes()
    {
        var token = CredentialHasher.CreateToken();
        var paths = new AppPaths(_temporaryDirectory);
        var store = new AppConfigurationStore(paths);
        var configuration = new AppConfiguration(
            AppConfiguration.CurrentVersion,
            53001,
            "bluetooth-off-pc.example.ts.net",
            CredentialHasher.HashLogin("person@example.com"),
            token.Sha256Hash);

        await store.SaveAsync(configuration, CancellationToken.None);
        var loaded = await store.LoadAsync(CancellationToken.None);
        var persistedText = await File.ReadAllTextAsync(paths.ConfigurationFile);

        Assert.AreEqual(configuration, loaded);
        Assert.IsFalse(persistedText.Contains(token.Plaintext, StringComparison.Ordinal));
        Assert.IsFalse(persistedText.Contains("person@example.com", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task LoadRejectsInvalidConfiguration()
    {
        var paths = new AppPaths(_temporaryDirectory);
        Directory.CreateDirectory(paths.RootDirectory);
        await File.WriteAllTextAsync(paths.ConfigurationFile, "{\"version\":999}");
        var store = new AppConfigurationStore(paths);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => store.LoadAsync(CancellationToken.None));
    }
}

