using BluetoothOff.Logging;
using Microsoft.Extensions.Logging;

namespace BluetoothOff.Tests;

[TestClass]
public sealed class BoundedFileLoggerProviderTests
{
    private string? _temporaryDirectory;

    [TestInitialize]
    public void Initialize()
    {
        _temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "BluetoothOff.LoggerTests",
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
    public void LoggerStoresSafeApplicationMessageWithoutExceptionDetail()
    {
        using var provider = new BoundedFileLoggerProvider(_temporaryDirectory!);
        var logger = provider.CreateLogger("BluetoothOff.Tests");
        logger.Log(
            LogLevel.Error,
            new EventId(42),
            "safe-message",
            new InvalidOperationException("secret exception detail"),
            static (state, _) => state);

        var logFile = Directory.GetFiles(_temporaryDirectory!, "*.log").Single();
        var contents = File.ReadAllText(logFile);

        StringAssert.Contains(contents, "safe-message");
        StringAssert.Contains(contents, "System.InvalidOperationException");
        Assert.IsFalse(contents.Contains("secret exception detail", StringComparison.Ordinal));
    }

    [TestMethod]
    public void LoggerIgnoresFrameworkCategories()
    {
        using var provider = new BoundedFileLoggerProvider(_temporaryDirectory!);
        var logger = provider.CreateLogger("Microsoft.AspNetCore.Hosting");
        logger.Log(
            LogLevel.Warning,
            new EventId(7),
            "https://host/path?token=secret",
            null,
            static (state, _) => state);

        Assert.AreEqual(0, Directory.GetFiles(_temporaryDirectory!, "*.log").Length);
    }
}
