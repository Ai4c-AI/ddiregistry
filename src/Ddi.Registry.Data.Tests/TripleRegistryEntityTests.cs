using System;
using Ddi.Registry.Data;
using Xunit;

namespace Ddi.Registry.Data.Tests;

public class TripleRegistryEntityTests
{
    [Fact]
    public void ConceptRegistration_Constructor_SetsRequestedDefaults()
    {
        var before = DateTime.UtcNow;
        var concept = new ConceptRegistration();
        var after = DateTime.UtcNow;

        Assert.NotEqual(Guid.Empty, concept.Id);
        Assert.Equal(ApprovalState.None, concept.ApprovalState);
        Assert.InRange(concept.CreatedAt, before, after);
        Assert.Null(concept.UpdatedAt);
    }

    [Fact]
    public void VariableRegistration_ExposesReferenceFields()
    {
        var variable = new VariableRegistration
        {
            Irdi = "urn:irdi:us.demo:variable:employment:1.0",
            AgencyId = "us.demo",
            Name = "employment",
            Version = "1.0",
            ConceptIrdi = "urn:irdi:us.demo:concept:employment-concept:1.0",
            RepresentationIrdi = "urn:irdi:us.demo:representation:boolean:1.0"
        };

        Assert.Equal("urn:irdi:us.demo:concept:employment-concept:1.0", variable.ConceptIrdi);
        Assert.Equal("urn:irdi:us.demo:representation:boolean:1.0", variable.RepresentationIrdi);
    }
}