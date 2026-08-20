using BluetoothOff.Tailscale;

namespace BluetoothOff.Tests;

[TestClass]
public sealed class TailscaleClientTests
{
    private const string StatusJson = """
        {
          "Version": "1.98.2",
          "BackendState": "Running",
          "Self": {
            "Online": true,
            "DNSName": "bluetooth-off-pc.example.ts.net.",
            "UserID": 1234
          },
          "User": {
            "1234": {
              "LoginName": "person@example.com"
            }
          }
        }
        """;

    [TestMethod]
    public void ParseStatusReadsOnlyRequiredIdentityFields()
    {
        var status = TailscaleClient.ParseStatusJson(StatusJson);

        Assert.IsTrue(status.IsConnected);
        Assert.AreEqual("1.98.2", status.Version);
        Assert.AreEqual("bluetooth-off-pc.example.ts.net", status.DnsName);
        Assert.AreEqual("person@example.com", status.LoginName);
    }

    [TestMethod]
    public void HasConfigurationRecognizesEmptyAndPopulatedDocuments()
    {
        Assert.IsFalse(TailscaleClient.HasConfiguration("{}"));
        Assert.IsFalse(TailscaleClient.HasConfiguration("null"));
        Assert.IsTrue(TailscaleClient.HasConfiguration("{\"Web\":{}}"));
        Assert.IsTrue(TailscaleClient.HasConfiguration("not-json"));
    }

    [TestMethod]
    public void JsonContainsValueFindsOnlyExactLoopbackTarget()
    {
        const string serveJson = """
            {
              "Web": {
                "bluetooth-off-pc.example.ts.net:443": {
                  "Handlers": {
                    "/": { "Proxy": "http://127.0.0.1:53001" }
                  }
                }
              }
            }
            """;

        Assert.IsTrue(TailscaleClient.JsonContainsValue(
            serveJson,
            "http://127.0.0.1:53001"));
        Assert.IsFalse(TailscaleClient.JsonContainsValue(
            serveJson,
            "http://127.0.0.1:53002"));
    }
}

