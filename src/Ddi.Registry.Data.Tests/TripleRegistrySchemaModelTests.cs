using System.Linq;
using Ddi.Registry.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ddi.Registry.Data.Tests;

public class TripleRegistrySchemaModelTests
{
    [Fact]
    public void ApplicationDbContext_ShouldExposeTripleRegistrySets()
    {
        using var context = CreateContext();

        Assert.NotNull(context.ConceptRegistrations);
        Assert.NotNull(context.RepresentationRegistrations);
        Assert.NotNull(context.VariableRegistrations);
        Assert.NotNull(context.ConceptRelations);
    }

    [Fact]
    public void VariableRegistration_ShouldHaveIrdiIndexesAndReferenceForeignKeys()
    {
        using var context = CreateContext();

        var entity = context.Model.FindEntityType(typeof(VariableRegistration));

        Assert.NotNull(entity);
        Assert.Contains(entity!.GetIndexes(), index =>
            index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual(new[] { nameof(VariableRegistration.Irdi) }));
        Assert.Contains(entity.GetIndexes(), index =>
            index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual(new[]
            {
                nameof(VariableRegistration.AgencyId),
                nameof(VariableRegistration.Name),
                nameof(VariableRegistration.Version)
            }));
        Assert.Contains(entity.GetForeignKeys(), foreignKey =>
            foreignKey.Properties.Select(property => property.Name).SequenceEqual(new[] { nameof(VariableRegistration.ConceptIrdi) }));
        Assert.Contains(entity.GetForeignKeys(), foreignKey =>
            foreignKey.Properties.Select(property => property.Name).SequenceEqual(new[] { nameof(VariableRegistration.RepresentationIrdi) }));
    }

    [Fact]
    public void ConceptRegistration_ShouldHaveQuerySupportIndexes()
    {
        using var context = CreateContext();

        var entity = context.Model.FindEntityType(typeof(ConceptRegistration));

        Assert.NotNull(entity);
        Assert.Contains(entity!.GetIndexes(), index =>
            index.Properties.Select(property => property.Name).SequenceEqual(new[] { nameof(ConceptRegistration.AgencyId) }));
        Assert.Contains(entity.GetIndexes(), index =>
            index.Properties.Select(property => property.Name).SequenceEqual(new[] { nameof(ConceptRegistration.ApprovalState) }));
        Assert.Contains(entity.GetIndexes(), index =>
            index.Properties.Select(property => property.Name).SequenceEqual(new[] { nameof(ConceptRegistration.CreatedAt) }));
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: "triple-registry-schema-model")
            .Options;

        return new ApplicationDbContext(options);
    }
}