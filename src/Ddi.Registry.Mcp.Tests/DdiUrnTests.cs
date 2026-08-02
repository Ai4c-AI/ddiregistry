using Ddi.Registry.Data;
using Xunit;

namespace Ddi.Registry.Mcp.Tests
{
    public class DdiUrnTests
    {
        [Fact] public void TryParse_ValidUrn_ParsesComponents() {
            var ok = DdiUrn.TryParse("urn:ddi:us.foo:bar:1", out var urn);
            Assert.True(ok); Assert.Equal("us.foo", urn.Agency); Assert.Equal("bar", urn.Identifier); Assert.Equal("1", urn.Version);
        }
        [Fact] public void TryParse_Agency_IsLowercased() { DdiUrn.TryParse("urn:ddi:US.Foo:bar:1", out var urn); Assert.Equal("us.foo", urn.Agency); }
        [Fact] public void TryParse_NotFiveParts_ReturnsFalse() => Assert.False(DdiUrn.TryParse("urn:ddi:us.foo:bar", out _));
        [Fact] public void TryParse_WrongSchemePrefix_ReturnsFalse() => Assert.False(DdiUrn.TryParse("http:ddi:us.foo:bar:1", out _));
        [Fact] public void TryParse_WrongSchemeSecondPart_ReturnsFalse() => Assert.False(DdiUrn.TryParse("urn:not-ddi:us.foo:bar:1", out _)); // 回归
        [Fact] public void ToString_RoundTrips() { DdiUrn.TryParse("urn:ddi:us.foo:bar:1", out var urn); Assert.Equal("urn:ddi:us.foo:bar:1", urn.ToString()); }
    }
}
