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
    public void FunnelDetectionDoesNotMistakePrivateServeForPublicExposure()
    {
        const string privateServe = """
            {
              "TCP": { "443": { "HTTPS": true } },
              "Web": {
                "bluetooth-off-pc.example.ts.net:443": {
                  "Handlers": {
                    "/": { "Proxy": "http://127.0.0.1:53001" }
                  }
                }
              }
            }
            """;

        Assert.IsFalse(TailscaleClient.HasFunnelConfiguration(privateServe));
    }

    [TestMethod]
    public void FunnelDetectionRequiresExplicitlyAllowedPublicEndpoint()
    {
        const string publicFunnel = """
            {
              "AllowFunnel": {
                "bluetooth-off-pc.example.ts.net:443": true
              }
            }
            """;

        Assert.IsTrue(TailscaleClient.HasFunnelConfiguration(publicFunnel));
        Assert.IsFalse(TailscaleClient.HasFunnelConfiguration("{}"));
        Assert.IsTrue(TailscaleClient.HasFunnelConfiguration("not-json"));
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

    [TestMethod]
    public void ServeActivationUriAcceptsOnlyExpectedTailscaleUrl()
    {
        const string output = "To enable, visit:\nhttps://login.tailscale.com/f/serve?node=AbCdEfGh12345678\n";

        var found = TailscaleClient.TryGetServeActivationUri(output, out var activationUri);

        Assert.IsTrue(found);
        Assert.IsNotNull(activationUri);
        Assert.AreEqual(
            "https://login.tailscale.com/f/serve?node=AbCdEfGh12345678",
            activationUri.AbsoluteUri);
    }

    [TestMethod]
    [DataRow("https://evil.example/f/serve?node=AbCdEfGh12345678")]
    [DataRow("http://login.tailscale.com/f/serve?node=AbCdEfGh12345678")]
    [DataRow("https://login.tailscale.com.evil.example/f/serve?node=AbCdEfGh12345678")]
    [DataRow("https://login.tailscale.com/f/serve?node=short")]
    [DataRow("https://login.tailscale.com/f/serve?node=AbCdEfGh12345678&next=evil")]
    public void ServeActivationUriRejectsUntrustedOrMalformedUrl(string output)
    {
        Assert.IsFalse(TailscaleClient.TryGetServeActivationUri(output, out _));
    }
}
