using Ddi.Registry.Data;
using Xunit;

namespace Ddi.Registry.Data.Tests;

public class RegistryIrdiTests
{
    [Fact]
    public void BuildConceptIrdi_ShouldMatchCanonicalFormat()
    {
        var irdi = RegistryIrdi.BuildConceptIrdi("us.demo", "worker-status", "1.0");

        Assert.Equal("urn:irdi:us.demo:concept:worker-status:1.0", irdi);
    }

    [Fact]
    public void TryParse_ShouldExtractParts()
    {
        var ok = RegistryIrdi.TryParse("urn:irdi:us.demo:variable:employment:1.0", out var parts);

        Assert.True(ok);
        Assert.NotNull(parts);
        Assert.Equal("us.demo", parts!.AgencyId);
        Assert.Equal("variable", parts.Kind);
        Assert.Equal("employment", parts.Name);
        Assert.Equal("1.0", parts.Version);
    }
}