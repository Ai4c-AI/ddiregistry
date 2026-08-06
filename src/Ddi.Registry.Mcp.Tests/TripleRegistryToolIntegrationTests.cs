using System.Collections.Generic;
using System.Threading.Tasks;
using Ddi.Registry.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ddi.Registry.Mcp.Tests
{
    public class TripleRegistryToolIntegrationTests
    {
        [Fact]
        public async Task RequestConcept_UnknownIdentity_ReturnsMappedIdentityErrorBeforeDuplicateSignal()
        {
            using var factory = new McpWebApplicationFactory();
            factory.Seed();

            using (var scope = factory.Services.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                context.ConceptRegistrations.Add(new ConceptRegistration
                {
                    Irdi = "urn:irdi:us.testorg:concept:worker-status:1.0",
                    AgencyId = "us.testorg",
                    Name = "worker-status",
                    Version = "1.0",
                    Label = "Worker Status",
                    ApprovalState = ApprovalState.Requested
                });
                await context.SaveChangesAsync();
            }

            var client = await McpHttpTestClient.ConnectAsync(factory, "unknown");
            var result = await client.CallToolAsync("request_concept", new Dictionary<string, object>
            {
                ["agencyId"] = "us.testorg",
                ["name"] = "worker-status",
                ["version"] = "1.0",
                ["label"] = "Worker Status"
            });

            Assert.Contains("could not be mapped", result.Content);
            Assert.DoesNotContain("already exists", result.Content);
        }

        [Fact]
        public async Task RequestConcept_WithWriteScope_CreatesRequestedConcept()
        {
            using var factory = new McpWebApplicationFactory();
            factory.Seed();

            var client = await McpHttpTestClient.ConnectAsync(factory, "full");
            var result = await client.CallToolAsync("request_concept", new Dictionary<string, object>
            {
                ["agencyId"] = "us.testorg",
                ["name"] = "worker-status",
                ["version"] = "1.0",
                ["label"] = "Worker Status"
            });

            Assert.False(result.IsError);
            Assert.Contains("Requested", result.Content);
            Assert.Contains("urn:irdi:us.testorg:concept:worker-status:1.0", result.Content);

            using var scope = factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var concept = await context.ConceptRegistrations.FirstOrDefaultAsync(c => c.Irdi == "urn:irdi:us.testorg:concept:worker-status:1.0");
            Assert.NotNull(concept);
            Assert.Equal(ApprovalState.Requested, concept!.ApprovalState);
        }

        [Fact]
        public async Task UpdateConceptRequest_WithWriteScope_UpdatesRequestedConcept()
        {
            using var factory = new McpWebApplicationFactory();
            factory.Seed();

            using (var scope = factory.Services.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                context.ConceptRegistrations.Add(new ConceptRegistration
                {
                    Irdi = "urn:irdi:us.testorg:concept:worker-status:1.0",
                    AgencyId = "us.testorg",
                    Name = "worker-status",
                    Version = "1.0",
                    Label = "Worker Status",
                    ApprovalState = ApprovalState.Requested
                });
                await context.SaveChangesAsync();
            }

            var client = await McpHttpTestClient.ConnectAsync(factory, "full");
            var result = await client.CallToolAsync("update_concept_request", new Dictionary<string, object>
            {
                ["irdi"] = "urn:irdi:us.testorg:concept:worker-status:1.0",
                ["label"] = "Worker Status Updated"
            });

            Assert.False(result.IsError);
            Assert.Contains("updated", result.Content);

            using var verifyScope = factory.Services.CreateScope();
            var verifyContext = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var concept = await verifyContext.ConceptRegistrations.FirstOrDefaultAsync(c => c.Irdi == "urn:irdi:us.testorg:concept:worker-status:1.0");
            Assert.NotNull(concept);
            Assert.Equal("Worker Status Updated", concept!.Label);
        }

        [Fact]
        public async Task UpdateConceptRequest_ApprovedConcept_IsRejected()
        {
            using var factory = new McpWebApplicationFactory();
            factory.Seed();

            using (var scope = factory.Services.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                context.ConceptRegistrations.Add(new ConceptRegistration
                {
                    Irdi = "urn:irdi:us.testorg:concept:worker-status:1.0",
                    AgencyId = "us.testorg",
                    Name = "worker-status",
                    Version = "1.0",
                    Label = "Worker Status",
                    ApprovalState = ApprovalState.Approved
                });
                await context.SaveChangesAsync();
            }

            var client = await McpHttpTestClient.ConnectAsync(factory, "full");
            var result = await client.CallToolAsync("update_concept_request", new Dictionary<string, object>
            {
                ["irdi"] = "urn:irdi:us.testorg:concept:worker-status:1.0",
                ["label"] = "Worker Status Updated"
            });

            Assert.Contains("Requested", result.Content);
            Assert.Contains("Only Requested concepts can be updated", result.Content);
        }

        [Fact]
        public async Task ApproveConcept_NonAdmin_IsRejected()
        {
            using var factory = new McpWebApplicationFactory();
            factory.Seed();

            using (var scope = factory.Services.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                context.ConceptRegistrations.Add(new ConceptRegistration
                {
                    Irdi = "urn:irdi:us.testorg:concept:worker-status:1.0",
                    AgencyId = "us.testorg",
                    Name = "worker-status",
                    Version = "1.0",
                    Label = "Worker Status",
                    ApprovalState = ApprovalState.Requested
                });
                await context.SaveChangesAsync();
            }

            var client = await McpHttpTestClient.ConnectAsync(factory, "full");
            var result = await client.CallToolAsync("approve_concept", new Dictionary<string, object>
            {
                ["irdi"] = "urn:irdi:us.testorg:concept:worker-status:1.0"
            });

            Assert.Contains("admin", result.Content);
        }

        [Fact]
        public async Task ApproveConcept_Admin_TransitionsToApproved()
        {
            using var factory = new McpWebApplicationFactory();
            factory.Seed();

            using (var scope = factory.Services.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                context.ConceptRegistrations.Add(new ConceptRegistration
                {
                    Irdi = "urn:irdi:us.testorg:concept:worker-status:1.0",
                    AgencyId = "us.testorg",
                    Name = "worker-status",
                    Version = "1.0",
                    Label = "Worker Status",
                    ApprovalState = ApprovalState.Requested
                });
                await context.SaveChangesAsync();
            }

            var client = await McpHttpTestClient.ConnectAsync(factory, "admin");
            var result = await client.CallToolAsync("approve_concept", new Dictionary<string, object>
            {
                ["irdi"] = "urn:irdi:us.testorg:concept:worker-status:1.0"
            });

            Assert.False(result.IsError);
            Assert.Contains("Approved", result.Content);

            using var verifyScope = factory.Services.CreateScope();
            var verifyContext = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var concept = await verifyContext.ConceptRegistrations.FirstOrDefaultAsync(c => c.Irdi == "urn:irdi:us.testorg:concept:worker-status:1.0");
            Assert.NotNull(concept);
            Assert.Equal(ApprovalState.Approved, concept!.ApprovalState);
        }

        [Fact]
        public async Task DeprecateConcept_NonAdmin_IsRejected()
        {
            using var factory = new McpWebApplicationFactory();
            factory.Seed();

            using (var scope = factory.Services.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                context.ConceptRegistrations.Add(new ConceptRegistration
                {
                    Irdi = "urn:irdi:us.testorg:concept:worker-status:1.0",
                    AgencyId = "us.testorg",
                    Name = "worker-status",
                    Version = "1.0",
                    Label = "Worker Status",
                    ApprovalState = ApprovalState.Approved
                });
                await context.SaveChangesAsync();
            }

            var client = await McpHttpTestClient.ConnectAsync(factory, "full");
            var result = await client.CallToolAsync("deprecate_concept", new Dictionary<string, object>
            {
                ["irdi"] = "urn:irdi:us.testorg:concept:worker-status:1.0"
            });

            Assert.Contains("admin", result.Content);
        }

        [Fact]
        public async Task DeprecateConcept_Admin_TransitionsToDeprecated()
        {
            using var factory = new McpWebApplicationFactory();
            factory.Seed();

            using (var scope = factory.Services.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                context.ConceptRegistrations.Add(new ConceptRegistration
                {
                    Irdi = "urn:irdi:us.testorg:concept:worker-status:1.0",
                    AgencyId = "us.testorg",
                    Name = "worker-status",
                    Version = "1.0",
                    Label = "Worker Status",
                    ApprovalState = ApprovalState.Approved
                });
                await context.SaveChangesAsync();
            }

            var client = await McpHttpTestClient.ConnectAsync(factory, "admin");
            var result = await client.CallToolAsync("deprecate_concept", new Dictionary<string, object>
            {
                ["irdi"] = "urn:irdi:us.testorg:concept:worker-status:1.0"
            });

            Assert.False(result.IsError);
            Assert.Contains("Deprecated", result.Content);

            using var verifyScope = factory.Services.CreateScope();
            var verifyContext = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var concept = await verifyContext.ConceptRegistrations.FirstOrDefaultAsync(c => c.Irdi == "urn:irdi:us.testorg:concept:worker-status:1.0");
            Assert.NotNull(concept);
            Assert.Equal(ApprovalState.Deprecated, concept!.ApprovalState);
        }

        [Fact]
        public async Task RequestRepresentation_UnknownIdentity_ReturnsMappedIdentityErrorBeforeDuplicateSignal()
        {
            using var factory = new McpWebApplicationFactory();
            factory.Seed();

            using (var scope = factory.Services.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                context.RepresentationRegistrations.Add(new RepresentationRegistration
                {
                    Irdi = "urn:irdi:us.testorg:representation:boolean:1.0",
                    AgencyId = "us.testorg",
                    Name = "boolean",
                    Version = "1.0",
                    Type = "Code",
                    JsonSchema = "{\"type\":\"boolean\"}",
                    ApprovalState = ApprovalState.Requested
                });
                await context.SaveChangesAsync();
            }

            var client = await McpHttpTestClient.ConnectAsync(factory, "unknown");
            var result = await client.CallToolAsync("request_representation", new Dictionary<string, object>
            {
                ["agencyId"] = "us.testorg",
                ["name"] = "boolean",
                ["version"] = "1.0",
                ["type"] = "Code",
                ["jsonSchema"] = "{\"type\":\"boolean\"}"
            });

            Assert.Contains("could not be mapped", result.Content);
            Assert.DoesNotContain("already exists", result.Content);
        }

        [Fact]
        public async Task RequestRepresentation_WithWriteScope_CreatesRequestedRepresentation()
        {
            using var factory = new McpWebApplicationFactory();
            factory.Seed();

            var client = await McpHttpTestClient.ConnectAsync(factory, "full");
            var result = await client.CallToolAsync("request_representation", new Dictionary<string, object>
            {
                ["agencyId"] = "us.testorg",
                ["name"] = "boolean",
                ["version"] = "1.0",
                ["type"] = "Code",
                ["jsonSchema"] = "{\"type\":\"boolean\"}"
            });

            Assert.False(result.IsError);
            Assert.Contains("Requested", result.Content);
            Assert.Contains("urn:irdi:us.testorg:representation:boolean:1.0", result.Content);

            using var scope = factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var representation = await context.RepresentationRegistrations.FirstOrDefaultAsync(r => r.Irdi == "urn:irdi:us.testorg:representation:boolean:1.0");
            Assert.NotNull(representation);
            Assert.Equal(ApprovalState.Requested, representation!.ApprovalState);
        }

        [Fact]
        public async Task UpdateRepresentationRequest_WithWriteScope_UpdatesRequestedRepresentation()
        {
            using var factory = new McpWebApplicationFactory();
            factory.Seed();

            using (var scope = factory.Services.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                context.RepresentationRegistrations.Add(new RepresentationRegistration
                {
                    Irdi = "urn:irdi:us.testorg:representation:boolean:1.0",
                    AgencyId = "us.testorg",
                    Name = "boolean",
                    Version = "1.0",
                    Type = "Code",
                    JsonSchema = "{\"type\":\"boolean\"}",
                    ApprovalState = ApprovalState.Requested
                });
                await context.SaveChangesAsync();
            }

            var client = await McpHttpTestClient.ConnectAsync(factory, "full");
            var result = await client.CallToolAsync("update_representation_request", new Dictionary<string, object>
            {
                ["irdi"] = "urn:irdi:us.testorg:representation:boolean:1.0",
                ["jsonSchema"] = "{\"type\":\"string\"}",
                ["type"] = "Text"
            });

            Assert.False(result.IsError);
            Assert.Contains("updated", result.Content);

            using var verifyScope = factory.Services.CreateScope();
            var verifyContext = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var representation = await verifyContext.RepresentationRegistrations.FirstOrDefaultAsync(r => r.Irdi == "urn:irdi:us.testorg:representation:boolean:1.0");
            Assert.NotNull(representation);
            Assert.Equal("Text", representation!.Type);
            Assert.Equal("{\"type\":\"string\"}", representation.JsonSchema);
        }

        [Fact]
        public async Task UpdateRepresentationRequest_ApprovedRepresentation_IsRejected()
        {
            using var factory = new McpWebApplicationFactory();
            factory.Seed();

            using (var scope = factory.Services.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                context.RepresentationRegistrations.Add(new RepresentationRegistration
                {
                    Irdi = "urn:irdi:us.testorg:representation:boolean:1.0",
                    AgencyId = "us.testorg",
                    Name = "boolean",
                    Version = "1.0",
                    Type = "Code",
                    JsonSchema = "{\"type\":\"boolean\"}",
                    ApprovalState = ApprovalState.Approved
                });
                await context.SaveChangesAsync();
            }

            var client = await McpHttpTestClient.ConnectAsync(factory, "full");
            var result = await client.CallToolAsync("update_representation_request", new Dictionary<string, object>
            {
                ["irdi"] = "urn:irdi:us.testorg:representation:boolean:1.0",
                ["jsonSchema"] = "{\"type\":\"string\"}",
                ["type"] = "Text"
            });

            Assert.Contains("Requested", result.Content);
            Assert.Contains("Only Requested representations can be updated", result.Content);
        }

        [Fact]
        public async Task ApproveRepresentation_NonAdmin_IsRejected()
        {
            using var factory = new McpWebApplicationFactory();
            factory.Seed();

            using (var scope = factory.Services.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                context.RepresentationRegistrations.Add(new RepresentationRegistration
                {
                    Irdi = "urn:irdi:us.testorg:representation:boolean:1.0",
                    AgencyId = "us.testorg",
                    Name = "boolean",
                    Version = "1.0",
                    Type = "Code",
                    JsonSchema = "{\"type\":\"boolean\"}",
                    ApprovalState = ApprovalState.Requested
                });
                await context.SaveChangesAsync();
            }

            var client = await McpHttpTestClient.ConnectAsync(factory, "full");
            var result = await client.CallToolAsync("approve_representation", new Dictionary<string, object>
            {
                ["irdi"] = "urn:irdi:us.testorg:representation:boolean:1.0"
            });

            Assert.Contains("admin", result.Content);
        }

        [Fact]
        public async Task ApproveRepresentation_Admin_TransitionsToApproved()
        {
            using var factory = new McpWebApplicationFactory();
            factory.Seed();

            using (var scope = factory.Services.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                context.RepresentationRegistrations.Add(new RepresentationRegistration
                {
                    Irdi = "urn:irdi:us.testorg:representation:boolean:1.0",
                    AgencyId = "us.testorg",
                    Name = "boolean",
                    Version = "1.0",
                    Type = "Code",
                    JsonSchema = "{\"type\":\"boolean\"}",
                    ApprovalState = ApprovalState.Requested
                });
                await context.SaveChangesAsync();
            }

            var client = await McpHttpTestClient.ConnectAsync(factory, "admin");
            var result = await client.CallToolAsync("approve_representation", new Dictionary<string, object>
            {
                ["irdi"] = "urn:irdi:us.testorg:representation:boolean:1.0"
            });

            Assert.False(result.IsError);
            Assert.Contains("Approved", result.Content);

            using var verifyScope = factory.Services.CreateScope();
            var verifyContext = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var representation = await verifyContext.RepresentationRegistrations.FirstOrDefaultAsync(r => r.Irdi == "urn:irdi:us.testorg:representation:boolean:1.0");
            Assert.NotNull(representation);
            Assert.Equal(ApprovalState.Approved, representation!.ApprovalState);
        }

        [Fact]
        public async Task DeprecateRepresentation_NonAdmin_IsRejected()
        {
            using var factory = new McpWebApplicationFactory();
            factory.Seed();

            using (var scope = factory.Services.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                context.RepresentationRegistrations.Add(new RepresentationRegistration
                {
                    Irdi = "urn:irdi:us.testorg:representation:boolean:1.0",
                    AgencyId = "us.testorg",
                    Name = "boolean",
                    Version = "1.0",
                    Type = "Code",
                    JsonSchema = "{\"type\":\"boolean\"}",
                    ApprovalState = ApprovalState.Approved
                });
                await context.SaveChangesAsync();
            }

            var client = await McpHttpTestClient.ConnectAsync(factory, "full");
            var result = await client.CallToolAsync("deprecate_representation", new Dictionary<string, object>
            {
                ["irdi"] = "urn:irdi:us.testorg:representation:boolean:1.0"
            });

            Assert.Contains("admin", result.Content);
        }

        [Fact]
        public async Task DeprecateRepresentation_Admin_TransitionsToDeprecated()
        {
            using var factory = new McpWebApplicationFactory();
            factory.Seed();

            using (var scope = factory.Services.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                context.RepresentationRegistrations.Add(new RepresentationRegistration
                {
                    Irdi = "urn:irdi:us.testorg:representation:boolean:1.0",
                    AgencyId = "us.testorg",
                    Name = "boolean",
                    Version = "1.0",
                    Type = "Code",
                    JsonSchema = "{\"type\":\"boolean\"}",
                    ApprovalState = ApprovalState.Approved
                });
                await context.SaveChangesAsync();
            }

            var client = await McpHttpTestClient.ConnectAsync(factory, "admin");
            var result = await client.CallToolAsync("deprecate_representation", new Dictionary<string, object>
            {
                ["irdi"] = "urn:irdi:us.testorg:representation:boolean:1.0"
            });

            Assert.False(result.IsError);
            Assert.Contains("Deprecated", result.Content);

            using var verifyScope = factory.Services.CreateScope();
            var verifyContext = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var representation = await verifyContext.RepresentationRegistrations.FirstOrDefaultAsync(r => r.Irdi == "urn:irdi:us.testorg:representation:boolean:1.0");
            Assert.NotNull(representation);
            Assert.Equal(ApprovalState.Deprecated, representation!.ApprovalState);
        }

        [Fact]
        public async Task ApproveVariable_NonAdmin_IsRejected()
        {
            using var factory = new McpWebApplicationFactory();
            factory.Seed();

            using (var scope = factory.Services.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                context.ConceptRegistrations.Add(new ConceptRegistration
                {
                    Irdi = "urn:irdi:us.testorg:concept:worker-status:1.0",
                    AgencyId = "us.testorg",
                    Name = "worker-status",
                    Version = "1.0",
                    Label = "Worker Status",
                    ApprovalState = ApprovalState.Approved
                });
                context.RepresentationRegistrations.Add(new RepresentationRegistration
                {
                    Irdi = "urn:irdi:us.testorg:representation:boolean:1.0",
                    AgencyId = "us.testorg",
                    Name = "boolean",
                    Version = "1.0",
                    Type = "Code",
                    JsonSchema = "{\"type\":\"boolean\"}",
                    ApprovalState = ApprovalState.Approved
                });
                context.VariableRegistrations.Add(new VariableRegistration
                {
                    Irdi = "urn:irdi:us.testorg:variable:employment:1.0",
                    AgencyId = "us.testorg",
                    Name = "employment",
                    Version = "1.0",
                    ConceptIrdi = "urn:irdi:us.testorg:concept:worker-status:1.0",
                    RepresentationIrdi = "urn:irdi:us.testorg:representation:boolean:1.0",
                    ApprovalState = ApprovalState.Requested
                });
                await context.SaveChangesAsync();
            }

            var client = await McpHttpTestClient.ConnectAsync(factory, "full");
            var result = await client.CallToolAsync("approve_variable", new Dictionary<string, object>
            {
                ["irdi"] = "urn:irdi:us.testorg:variable:employment:1.0"
            });

            Assert.Contains("admin", result.Content);
        }

        [Fact]
        public async Task ApproveVariable_Admin_TransitionsToApproved()
        {
            using var factory = new McpWebApplicationFactory();
            factory.Seed();

            using (var scope = factory.Services.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                context.ConceptRegistrations.Add(new ConceptRegistration
                {
                    Irdi = "urn:irdi:us.testorg:concept:worker-status:1.0",
                    AgencyId = "us.testorg",
                    Name = "worker-status",
                    Version = "1.0",
                    Label = "Worker Status",
                    ApprovalState = ApprovalState.Approved
                });
                context.RepresentationRegistrations.Add(new RepresentationRegistration
                {
                    Irdi = "urn:irdi:us.testorg:representation:boolean:1.0",
                    AgencyId = "us.testorg",
                    Name = "boolean",
                    Version = "1.0",
                    Type = "Code",
                    JsonSchema = "{\"type\":\"boolean\"}",
                    ApprovalState = ApprovalState.Approved
                });
                context.VariableRegistrations.Add(new VariableRegistration
                {
                    Irdi = "urn:irdi:us.testorg:variable:employment:1.0",
                    AgencyId = "us.testorg",
                    Name = "employment",
                    Version = "1.0",
                    ConceptIrdi = "urn:irdi:us.testorg:concept:worker-status:1.0",
                    RepresentationIrdi = "urn:irdi:us.testorg:representation:boolean:1.0",
                    ApprovalState = ApprovalState.Requested
                });
                await context.SaveChangesAsync();
            }

            var client = await McpHttpTestClient.ConnectAsync(factory, "admin");
            var result = await client.CallToolAsync("approve_variable", new Dictionary<string, object>
            {
                ["irdi"] = "urn:irdi:us.testorg:variable:employment:1.0"
            });

            Assert.False(result.IsError);
            Assert.Contains("Approved", result.Content);

            using var verifyScope = factory.Services.CreateScope();
            var verifyContext = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var variable = await verifyContext.VariableRegistrations.FirstOrDefaultAsync(v => v.Irdi == "urn:irdi:us.testorg:variable:employment:1.0");
            Assert.NotNull(variable);
            Assert.Equal(ApprovalState.Approved, variable!.ApprovalState);
        }

        [Fact]
        public async Task DeprecateVariable_NonAdmin_IsRejected()
        {
            using var factory = new McpWebApplicationFactory();
            factory.Seed();

            using (var scope = factory.Services.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                context.ConceptRegistrations.Add(new ConceptRegistration
                {
                    Irdi = "urn:irdi:us.testorg:concept:worker-status:1.0",
                    AgencyId = "us.testorg",
                    Name = "worker-status",
                    Version = "1.0",
                    Label = "Worker Status",
                    ApprovalState = ApprovalState.Approved
                });
                context.RepresentationRegistrations.Add(new RepresentationRegistration
                {
                    Irdi = "urn:irdi:us.testorg:representation:boolean:1.0",
                    AgencyId = "us.testorg",
                    Name = "boolean",
                    Version = "1.0",
                    Type = "Code",
                    JsonSchema = "{\"type\":\"boolean\"}",
                    ApprovalState = ApprovalState.Approved
                });
                context.VariableRegistrations.Add(new VariableRegistration
                {
                    Irdi = "urn:irdi:us.testorg:variable:employment:1.0",
                    AgencyId = "us.testorg",
                    Name = "employment",
                    Version = "1.0",
                    ConceptIrdi = "urn:irdi:us.testorg:concept:worker-status:1.0",
                    RepresentationIrdi = "urn:irdi:us.testorg:representation:boolean:1.0",
                    ApprovalState = ApprovalState.Approved
                });
                await context.SaveChangesAsync();
            }

            var client = await McpHttpTestClient.ConnectAsync(factory, "full");
            var result = await client.CallToolAsync("deprecate_variable", new Dictionary<string, object>
            {
                ["irdi"] = "urn:irdi:us.testorg:variable:employment:1.0"
            });

            Assert.Contains("admin", result.Content);
        }

        [Fact]
        public async Task DeprecateVariable_Admin_TransitionsToDeprecated()
        {
            using var factory = new McpWebApplicationFactory();
            factory.Seed();

            using (var scope = factory.Services.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                context.ConceptRegistrations.Add(new ConceptRegistration
                {
                    Irdi = "urn:irdi:us.testorg:concept:worker-status:1.0",
                    AgencyId = "us.testorg",
                    Name = "worker-status",
                    Version = "1.0",
                    Label = "Worker Status",
                    ApprovalState = ApprovalState.Approved
                });
                context.RepresentationRegistrations.Add(new RepresentationRegistration
                {
                    Irdi = "urn:irdi:us.testorg:representation:boolean:1.0",
                    AgencyId = "us.testorg",
                    Name = "boolean",
                    Version = "1.0",
                    Type = "Code",
                    JsonSchema = "{\"type\":\"boolean\"}",
                    ApprovalState = ApprovalState.Approved
                });
                context.VariableRegistrations.Add(new VariableRegistration
                {
                    Irdi = "urn:irdi:us.testorg:variable:employment:1.0",
                    AgencyId = "us.testorg",
                    Name = "employment",
                    Version = "1.0",
                    ConceptIrdi = "urn:irdi:us.testorg:concept:worker-status:1.0",
                    RepresentationIrdi = "urn:irdi:us.testorg:representation:boolean:1.0",
                    ApprovalState = ApprovalState.Approved
                });
                await context.SaveChangesAsync();
            }

            var client = await McpHttpTestClient.ConnectAsync(factory, "admin");
            var result = await client.CallToolAsync("deprecate_variable", new Dictionary<string, object>
            {
                ["irdi"] = "urn:irdi:us.testorg:variable:employment:1.0"
            });

            Assert.False(result.IsError);
            Assert.Contains("Deprecated", result.Content);

            using var verifyScope = factory.Services.CreateScope();
            var verifyContext = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var variable = await verifyContext.VariableRegistrations.FirstOrDefaultAsync(v => v.Irdi == "urn:irdi:us.testorg:variable:employment:1.0");
            Assert.NotNull(variable);
            Assert.Equal(ApprovalState.Deprecated, variable!.ApprovalState);
        }

        [Fact]
        public async Task RequestVariable_UnknownIdentity_ReturnsMappedIdentityErrorBeforeDuplicateSignal()
        {
            using var factory = new McpWebApplicationFactory();
            factory.Seed();

            using (var scope = factory.Services.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                context.ConceptRegistrations.Add(new ConceptRegistration
                {
                    Irdi = "urn:irdi:us.testorg:concept:worker-status:1.0",
                    AgencyId = "us.testorg",
                    Name = "worker-status",
                    Version = "1.0",
                    Label = "Worker Status",
                    ApprovalState = ApprovalState.Approved
                });
                context.RepresentationRegistrations.Add(new RepresentationRegistration
                {
                    Irdi = "urn:irdi:us.testorg:representation:boolean:1.0",
                    AgencyId = "us.testorg",
                    Name = "boolean",
                    Version = "1.0",
                    Type = "Code",
                    JsonSchema = "{\"type\":\"boolean\"}",
                    ApprovalState = ApprovalState.Approved
                });
                context.VariableRegistrations.Add(new VariableRegistration
                {
                    Irdi = "urn:irdi:us.testorg:variable:employment:1.0",
                    AgencyId = "us.testorg",
                    Name = "employment",
                    Version = "1.0",
                    ConceptIrdi = "urn:irdi:us.testorg:concept:worker-status:1.0",
                    RepresentationIrdi = "urn:irdi:us.testorg:representation:boolean:1.0",
                    ApprovalState = ApprovalState.Requested
                });
                await context.SaveChangesAsync();
            }

            var client = await McpHttpTestClient.ConnectAsync(factory, "unknown");
            var result = await client.CallToolAsync("request_variable", new Dictionary<string, object>
            {
                ["agencyId"] = "us.testorg",
                ["name"] = "employment",
                ["version"] = "1.0",
                ["conceptIrdi"] = "urn:irdi:us.testorg:concept:worker-status:1.0",
                ["representationIrdi"] = "urn:irdi:us.testorg:representation:boolean:1.0"
            });

            Assert.Contains("could not be mapped", result.Content);
            Assert.DoesNotContain("already exists", result.Content);
        }

        [Fact]
        public async Task RequestVariable_WithWriteScope_CreatesRequestedVariable()
        {
            using var factory = new McpWebApplicationFactory();
            factory.Seed();

            using (var scope = factory.Services.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                context.ConceptRegistrations.Add(new ConceptRegistration
                {
                    Irdi = "urn:irdi:us.testorg:concept:worker-status:1.0",
                    AgencyId = "us.testorg",
                    Name = "worker-status",
                    Version = "1.0",
                    Label = "Worker Status",
                    ApprovalState = ApprovalState.Approved
                });
                context.RepresentationRegistrations.Add(new RepresentationRegistration
                {
                    Irdi = "urn:irdi:us.testorg:representation:boolean:1.0",
                    AgencyId = "us.testorg",
                    Name = "boolean",
                    Version = "1.0",
                    Type = "Code",
                    JsonSchema = "{\"type\":\"boolean\"}",
                    ApprovalState = ApprovalState.Approved
                });
                await context.SaveChangesAsync();
            }

            var client = await McpHttpTestClient.ConnectAsync(factory, "full");
            var result = await client.CallToolAsync("request_variable", new Dictionary<string, object>
            {
                ["agencyId"] = "us.testorg",
                ["name"] = "employment",
                ["version"] = "1.0",
                ["conceptIrdi"] = "urn:irdi:us.testorg:concept:worker-status:1.0",
                ["representationIrdi"] = "urn:irdi:us.testorg:representation:boolean:1.0"
            });

            Assert.False(result.IsError);
            Assert.Contains("Requested", result.Content);
            Assert.Contains("urn:irdi:us.testorg:variable:employment:1.0", result.Content);

            using var verifyScope = factory.Services.CreateScope();
            var verifyContext = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var variable = await verifyContext.VariableRegistrations.FirstOrDefaultAsync(v => v.Irdi == "urn:irdi:us.testorg:variable:employment:1.0");
            Assert.NotNull(variable);
            Assert.Equal(ApprovalState.Requested, variable!.ApprovalState);
        }

        [Fact]
        public async Task UpdateVariableRequest_WithWriteScope_UpdatesRequestedVariable()
        {
            using var factory = new McpWebApplicationFactory();
            factory.Seed();

            using (var scope = factory.Services.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                context.ConceptRegistrations.Add(new ConceptRegistration
                {
                    Irdi = "urn:irdi:us.testorg:concept:worker-status:1.0",
                    AgencyId = "us.testorg",
                    Name = "worker-status",
                    Version = "1.0",
                    Label = "Worker Status",
                    ApprovalState = ApprovalState.Approved
                });
                context.RepresentationRegistrations.Add(new RepresentationRegistration
                {
                    Irdi = "urn:irdi:us.testorg:representation:boolean:1.0",
                    AgencyId = "us.testorg",
                    Name = "boolean",
                    Version = "1.0",
                    Type = "Code",
                    JsonSchema = "{\"type\":\"boolean\"}",
                    ApprovalState = ApprovalState.Approved
                });
                context.VariableRegistrations.Add(new VariableRegistration
                {
                    Irdi = "urn:irdi:us.testorg:variable:employment:1.0",
                    AgencyId = "us.testorg",
                    Name = "employment",
                    Version = "1.0",
                    ConceptIrdi = "urn:irdi:us.testorg:concept:worker-status:1.0",
                    RepresentationIrdi = "urn:irdi:us.testorg:representation:boolean:1.0",
                    CollectionMethod = "survey",
                    ApprovalState = ApprovalState.Requested
                });
                await context.SaveChangesAsync();
            }

            var client = await McpHttpTestClient.ConnectAsync(factory, "full");
            var result = await client.CallToolAsync("update_variable_request", new Dictionary<string, object>
            {
                ["irdi"] = "urn:irdi:us.testorg:variable:employment:1.0",
                ["collectionMethod"] = "api"
            });

            Assert.False(result.IsError);
            Assert.Contains("updated", result.Content);

            using var verifyScope = factory.Services.CreateScope();
            var verifyContext = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var variable = await verifyContext.VariableRegistrations.FirstOrDefaultAsync(v => v.Irdi == "urn:irdi:us.testorg:variable:employment:1.0");
            Assert.NotNull(variable);
            Assert.Equal("api", variable!.CollectionMethod);
        }

        [Fact]
        public async Task UpdateVariableRequest_ApprovedVariable_IsRejected()
        {
            using var factory = new McpWebApplicationFactory();
            factory.Seed();

            using (var scope = factory.Services.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                context.ConceptRegistrations.Add(new ConceptRegistration
                {
                    Irdi = "urn:irdi:us.testorg:concept:worker-status:1.0",
                    AgencyId = "us.testorg",
                    Name = "worker-status",
                    Version = "1.0",
                    Label = "Worker Status",
                    ApprovalState = ApprovalState.Approved
                });
                context.RepresentationRegistrations.Add(new RepresentationRegistration
                {
                    Irdi = "urn:irdi:us.testorg:representation:boolean:1.0",
                    AgencyId = "us.testorg",
                    Name = "boolean",
                    Version = "1.0",
                    Type = "Code",
                    JsonSchema = "{\"type\":\"boolean\"}",
                    ApprovalState = ApprovalState.Approved
                });
                context.VariableRegistrations.Add(new VariableRegistration
                {
                    Irdi = "urn:irdi:us.testorg:variable:employment:1.0",
                    AgencyId = "us.testorg",
                    Name = "employment",
                    Version = "1.0",
                    ConceptIrdi = "urn:irdi:us.testorg:concept:worker-status:1.0",
                    RepresentationIrdi = "urn:irdi:us.testorg:representation:boolean:1.0",
                    CollectionMethod = "survey",
                    ApprovalState = ApprovalState.Approved
                });
                await context.SaveChangesAsync();
            }

            var client = await McpHttpTestClient.ConnectAsync(factory, "full");
            var result = await client.CallToolAsync("update_variable_request", new Dictionary<string, object>
            {
                ["irdi"] = "urn:irdi:us.testorg:variable:employment:1.0",
                ["collectionMethod"] = "api"
            });

            Assert.Contains("Requested", result.Content);
            Assert.Contains("Only Requested variables can be updated", result.Content);
        }

        [Fact]
        public async Task RequestVariable_CrossAgencyReference_IsRejected()
        {
            using var factory = new McpWebApplicationFactory();
            factory.Seed();

            using (var scope = factory.Services.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                context.ConceptRegistrations.Add(new ConceptRegistration
                {
                    Irdi = "urn:irdi:uk.testorg:concept:worker-status:1.0",
                    AgencyId = "uk.testorg",
                    Name = "worker-status",
                    Version = "1.0",
                    Label = "Worker Status",
                    ApprovalState = ApprovalState.Approved
                });
                context.RepresentationRegistrations.Add(new RepresentationRegistration
                {
                    Irdi = "urn:irdi:us.testorg:representation:boolean:1.0",
                    AgencyId = "us.testorg",
                    Name = "boolean",
                    Version = "1.0",
                    Type = "Code",
                    JsonSchema = "{\"type\":\"boolean\"}",
                    ApprovalState = ApprovalState.Approved
                });
                await context.SaveChangesAsync();
            }

            var client = await McpHttpTestClient.ConnectAsync(factory, "full");
            var result = await client.CallToolAsync("request_variable", new Dictionary<string, object>
            {
                ["agencyId"] = "us.testorg",
                ["name"] = "employment",
                ["version"] = "1.0",
                ["conceptIrdi"] = "urn:irdi:uk.testorg:concept:worker-status:1.0",
                ["representationIrdi"] = "urn:irdi:us.testorg:representation:boolean:1.0"
            });

            Assert.Contains("CrossAgencyReference", result.Content);
        }

        [Fact]
        public async Task ListConcepts_WithReadScope_ReturnsRecords()
        {
            using var factory = new McpWebApplicationFactory();
            factory.Seed();

            using (var scope = factory.Services.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                context.ConceptRegistrations.Add(new ConceptRegistration
                {
                    Irdi = "urn:irdi:us.testorg:concept:worker-status:1.0",
                    AgencyId = "us.testorg",
                    Name = "worker-status",
                    Version = "1.0",
                    Label = "Worker Status",
                    ApprovalState = ApprovalState.Requested
                });
                await context.SaveChangesAsync();
            }

            var client = await McpHttpTestClient.ConnectAsync(factory, "read");
            var result = await client.CallToolAsync("list_concepts", new Dictionary<string, object>());

            Assert.False(result.IsError);
            Assert.Contains("worker-status", result.Content);
            Assert.Contains("urn:irdi:us.testorg:concept:worker-status:1.0", result.Content);
        }

        [Fact]
        public async Task GetVariablePublishability_WithRequestedVariable_ReturnsBlockedState()
        {
            using var factory = new McpWebApplicationFactory();
            factory.Seed();

            using (var scope = factory.Services.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                context.ConceptRegistrations.Add(new ConceptRegistration
                {
                    Irdi = "urn:irdi:us.testorg:concept:worker-status:1.0",
                    AgencyId = "us.testorg",
                    Name = "worker-status",
                    Version = "1.0",
                    Label = "Worker Status",
                    ApprovalState = ApprovalState.Approved
                });
                context.RepresentationRegistrations.Add(new RepresentationRegistration
                {
                    Irdi = "urn:irdi:us.testorg:representation:boolean:1.0",
                    AgencyId = "us.testorg",
                    Name = "boolean",
                    Version = "1.0",
                    ApprovalState = ApprovalState.Approved
                });
                context.VariableRegistrations.Add(new VariableRegistration
                {
                    Irdi = "urn:irdi:us.testorg:variable:employment:1.0",
                    AgencyId = "us.testorg",
                    Name = "employment",
                    Version = "1.0",
                    ConceptIrdi = "urn:irdi:us.testorg:concept:worker-status:1.0",
                    RepresentationIrdi = "urn:irdi:us.testorg:representation:boolean:1.0",
                    ApprovalState = ApprovalState.Requested
                });
                await context.SaveChangesAsync();
            }

            var client = await McpHttpTestClient.ConnectAsync(factory, "read");
            var result = await client.CallToolAsync("get_variable_publishability", new Dictionary<string, object>
            {
                ["irdi"] = "urn:irdi:us.testorg:variable:employment:1.0"
            });

            Assert.False(result.IsError);
            Assert.Contains("employment", result.Content);
            Assert.Contains("isPublishable", result.Content);
            Assert.Contains("false", result.Content);
        }

        [Fact]
        public async Task ListVariables_WithReadScope_ReturnsPublishabilityProjection()
        {
            using var factory = new McpWebApplicationFactory();
            factory.Seed();

            using (var scope = factory.Services.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                context.ConceptRegistrations.Add(new ConceptRegistration
                {
                    Irdi = "urn:irdi:us.testorg:concept:worker-status:1.0",
                    AgencyId = "us.testorg",
                    Name = "worker-status",
                    Version = "1.0",
                    Label = "Worker Status",
                    ApprovalState = ApprovalState.Approved
                });
                context.RepresentationRegistrations.Add(new RepresentationRegistration
                {
                    Irdi = "urn:irdi:us.testorg:representation:boolean:1.0",
                    AgencyId = "us.testorg",
                    Name = "boolean",
                    Version = "1.0",
                    ApprovalState = ApprovalState.Approved
                });
                context.VariableRegistrations.Add(new VariableRegistration
                {
                    Irdi = "urn:irdi:us.testorg:variable:employment:1.0",
                    AgencyId = "us.testorg",
                    Name = "employment",
                    Version = "1.0",
                    ConceptIrdi = "urn:irdi:us.testorg:concept:worker-status:1.0",
                    RepresentationIrdi = "urn:irdi:us.testorg:representation:boolean:1.0",
                    ApprovalState = ApprovalState.Requested
                });
                await context.SaveChangesAsync();
            }

            var client = await McpHttpTestClient.ConnectAsync(factory, "read");
            var result = await client.CallToolAsync("list_variables", new Dictionary<string, object>());

            Assert.False(result.IsError);
            Assert.Contains("employment", result.Content);
            Assert.Contains("isPublishable", result.Content);
            Assert.Contains("false", result.Content);
        }

        [Fact]
        public async Task GetConcept_WithReadScope_ReturnsRecord()
        {
            using var factory = new McpWebApplicationFactory();
            factory.Seed();

            using (var scope = factory.Services.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                context.ConceptRegistrations.Add(new ConceptRegistration
                {
                    Irdi = "urn:irdi:us.testorg:concept:worker-status:1.0",
                    AgencyId = "us.testorg",
                    Name = "worker-status",
                    Version = "1.0",
                    Label = "Worker Status",
                    ApprovalState = ApprovalState.Requested
                });
                await context.SaveChangesAsync();
            }

            var client = await McpHttpTestClient.ConnectAsync(factory, "read");
            var result = await client.CallToolAsync("get_concept", new Dictionary<string, object>
            {
                ["irdi"] = "urn:irdi:us.testorg:concept:worker-status:1.0"
            });

            Assert.False(result.IsError);
            Assert.Contains("worker-status", result.Content);
            Assert.Contains("Worker Status", result.Content);
        }

        [Fact]
        public async Task ListRepresentations_WithReadScope_ReturnsRecords()
        {
            using var factory = new McpWebApplicationFactory();
            factory.Seed();

            using (var scope = factory.Services.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                context.RepresentationRegistrations.Add(new RepresentationRegistration
                {
                    Irdi = "urn:irdi:us.testorg:representation:boolean:1.0",
                    AgencyId = "us.testorg",
                    Name = "boolean",
                    Version = "1.0",
                    Type = "Code",
                    JsonSchema = "{\"type\":\"boolean\"}",
                    ApprovalState = ApprovalState.Requested
                });
                await context.SaveChangesAsync();
            }

            var client = await McpHttpTestClient.ConnectAsync(factory, "read");
            var result = await client.CallToolAsync("list_representations", new Dictionary<string, object>());

            Assert.False(result.IsError);
            Assert.Contains("boolean", result.Content);
            Assert.Contains("representation:boolean:1.0", result.Content);
        }

        [Fact]
        public async Task GetRepresentation_WithReadScope_ReturnsRecord()
        {
            using var factory = new McpWebApplicationFactory();
            factory.Seed();

            using (var scope = factory.Services.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                context.RepresentationRegistrations.Add(new RepresentationRegistration
                {
                    Irdi = "urn:irdi:us.testorg:representation:boolean:1.0",
                    AgencyId = "us.testorg",
                    Name = "boolean",
                    Version = "1.0",
                    Type = "Code",
                    JsonSchema = "{\"type\":\"boolean\"}",
                    ApprovalState = ApprovalState.Requested
                });
                await context.SaveChangesAsync();
            }

            var client = await McpHttpTestClient.ConnectAsync(factory, "read");
            var result = await client.CallToolAsync("get_representation", new Dictionary<string, object>
            {
                ["irdi"] = "urn:irdi:us.testorg:representation:boolean:1.0"
            });

            Assert.False(result.IsError);
            Assert.Contains("boolean", result.Content);
            Assert.Contains("jsonSchema", result.Content);
        }

        [Fact]
        public async Task GetVariable_WithReadScope_ReturnsRecordAndPublishability()
        {
            using var factory = new McpWebApplicationFactory();
            factory.Seed();

            using (var scope = factory.Services.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                context.ConceptRegistrations.Add(new ConceptRegistration
                {
                    Irdi = "urn:irdi:us.testorg:concept:worker-status:1.0",
                    AgencyId = "us.testorg",
                    Name = "worker-status",
                    Version = "1.0",
                    Label = "Worker Status",
                    ApprovalState = ApprovalState.Approved
                });
                context.RepresentationRegistrations.Add(new RepresentationRegistration
                {
                    Irdi = "urn:irdi:us.testorg:representation:boolean:1.0",
                    AgencyId = "us.testorg",
                    Name = "boolean",
                    Version = "1.0",
                    Type = "Code",
                    JsonSchema = "{\"type\":\"boolean\"}",
                    ApprovalState = ApprovalState.Approved
                });
                context.VariableRegistrations.Add(new VariableRegistration
                {
                    Irdi = "urn:irdi:us.testorg:variable:employment:1.0",
                    AgencyId = "us.testorg",
                    Name = "employment",
                    Version = "1.0",
                    ConceptIrdi = "urn:irdi:us.testorg:concept:worker-status:1.0",
                    RepresentationIrdi = "urn:irdi:us.testorg:representation:boolean:1.0",
                    ApprovalState = ApprovalState.Approved
                });
                await context.SaveChangesAsync();
            }

            var client = await McpHttpTestClient.ConnectAsync(factory, "read");
            var result = await client.CallToolAsync("get_variable", new Dictionary<string, object>
            {
                ["irdi"] = "urn:irdi:us.testorg:variable:employment:1.0"
            });

            Assert.False(result.IsError);
            Assert.Contains("employment", result.Content);
            Assert.Contains("isPublishable", result.Content);
            Assert.Contains("true", result.Content);
        }
    }
}