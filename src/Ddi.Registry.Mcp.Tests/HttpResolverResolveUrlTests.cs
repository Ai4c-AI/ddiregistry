using Ddi.Registry.Data;
using Xunit;

namespace Ddi.Registry.Mcp.Tests
{
    public class HttpResolverResolveUrlTests
    {
        [Fact] public void ResolveUrl_FillsAllTokens() {
            var r = new HttpResolver { UrlTemplate = "https://{agency}.example.org/{identifier}/{version}" };
            DdiUrn.TryParse("urn:ddi:us.foo:bar:1", out var urn); Assert.Equal("https://us.foo.example.org/bar/1", r.ResolveUrl(urn));
        }
        [Fact] public void ResolveUrl_FillsUrnToken() {
            var r = new HttpResolver { UrlTemplate = "https://resolver.example.org/lookup?u={urn}" };
            DdiUrn.TryParse("urn:ddi:us.foo:bar:1", out var urn); Assert.Equal("https://resolver.example.org/lookup?u=urn:ddi:us.foo:bar:1", r.ResolveUrl(urn));
        }
        [Fact] public void ResolveUrl_NoTokens_ReturnsTemplateVerbatim() {
            var r = new HttpResolver { UrlTemplate = "https://static.example.org" };
            DdiUrn.TryParse("urn:ddi:us.foo:bar:1", out var urn); Assert.Equal("https://static.example.org", r.ResolveUrl(urn));
        }
    }
}
