using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Ddi.Registry.Data;
using Ddi.Registry.Web.Controllers;
using Ddi.Registry.Web.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Xunit;

namespace Ddi.Registry.Web.Tests;

public class TripleRegistryManageTests
{
    [Fact]
    public async Task AddConceptRegistration_ForManagedAgency_ReturnsPrefilledViewModel()
    {
        await using var context = CreateContext();
        context.Agencies.Add(new Agency
        {
            AgencyId = "us.testorg",
            Label = "Test Org",
            CreatorId = "user-1",
            AdminContactId = "user-1",
            TechnicalContactId = "user-1"
        });
        await context.SaveChangesAsync();

        var controller = CreateController(context, "user-1");

        var result = await controller.AddConceptRegistration("us.testorg");

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ConceptRegistrationModel>(view.Model);
        Assert.Equal("us.testorg", model.AgencyId);
    }

    [Fact]
    public async Task AddConceptRegistration_ForUnmanagedAgency_ReturnsForbid()
    {
        await using var context = CreateContext();
        context.Agencies.Add(new Agency
        {
            AgencyId = "us.testorg",
            Label = "Test Org",
            CreatorId = "owner-1",
            AdminContactId = "owner-1",
            TechnicalContactId = "owner-1"
        });
        await context.SaveChangesAsync();

        var controller = CreateController(context, "user-2");

        var result = await controller.AddConceptRegistration("us.testorg");

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task AddConceptRegistration_Post_ForManagedAgency_CreatesRequestedConcept()
    {
        await using var context = CreateContext();
        context.Agencies.Add(new Agency
        {
            AgencyId = "us.testorg",
            Label = "Test Org",
            CreatorId = "user-1",
            AdminContactId = "user-1",
            TechnicalContactId = "user-1"
        });
        await context.SaveChangesAsync();

        var controller = CreateController(context, "user-1");

        var result = await controller.AddConceptRegistration(new ConceptRegistrationModel
        {
            AgencyId = "us.testorg",
            Name = "worker-status",
            Version = "1.0",
            Label = "Worker Status"
        });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("ViewAgency", redirect.ActionName);
        Assert.Equal("us.testorg", redirect.RouteValues!["agencyId"]);

        var concept = await context.ConceptRegistrations.FirstOrDefaultAsync(c => c.Irdi == "urn:irdi:us.testorg:concept:worker-status:1.0");
        Assert.NotNull(concept);
        Assert.Equal(ApprovalState.Requested, concept!.ApprovalState);
    }

    [Fact]
    public async Task AddConceptRegistration_Post_ForUnmanagedAgency_ReturnsForbid()
    {
        await using var context = CreateContext();
        context.Agencies.Add(new Agency
        {
            AgencyId = "us.testorg",
            Label = "Test Org",
            CreatorId = "owner-1",
            AdminContactId = "owner-1",
            TechnicalContactId = "owner-1"
        });
        await context.SaveChangesAsync();

        var controller = CreateController(context, "user-2");

        var result = await controller.AddConceptRegistration(new ConceptRegistrationModel
        {
            AgencyId = "us.testorg",
            Name = "worker-status",
            Version = "1.0",
            Label = "Worker Status"
        });

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task AddRepresentationRegistration_ForManagedAgency_ReturnsPrefilledViewModel()
    {
        await using var context = CreateContext();
        context.Agencies.Add(new Agency
        {
            AgencyId = "us.testorg",
            Label = "Test Org",
            CreatorId = "user-1",
            AdminContactId = "user-1",
            TechnicalContactId = "user-1"
        });
        await context.SaveChangesAsync();

        var controller = CreateController(context, "user-1");

        var result = await controller.AddRepresentationRegistration("us.testorg");

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<RepresentationRegistrationModel>(view.Model);
        Assert.Equal("us.testorg", model.AgencyId);
    }

    [Fact]
    public async Task AddRepresentationRegistration_Post_ForManagedAgency_CreatesRequestedRepresentation()
    {
        await using var context = CreateContext();
        context.Agencies.Add(new Agency
        {
            AgencyId = "us.testorg",
            Label = "Test Org",
            CreatorId = "user-1",
            AdminContactId = "user-1",
            TechnicalContactId = "user-1"
        });
        await context.SaveChangesAsync();

        var controller = CreateController(context, "user-1");

        var result = await controller.AddRepresentationRegistration(new RepresentationRegistrationModel
        {
            AgencyId = "us.testorg",
            Name = "boolean",
            Version = "1.0",
            Type = "Code",
            JsonSchema = "{\"type\":\"boolean\"}"
        });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("ViewAgency", redirect.ActionName);
        Assert.Equal("us.testorg", redirect.RouteValues!["agencyId"]);

        var representation = await context.RepresentationRegistrations.FirstOrDefaultAsync(r => r.Irdi == "urn:irdi:us.testorg:representation:boolean:1.0");
        Assert.NotNull(representation);
        Assert.Equal(ApprovalState.Requested, representation!.ApprovalState);
    }

    [Fact]
    public async Task AddVariableRegistration_ForManagedAgency_ReturnsPrefilledViewModel()
    {
        await using var context = CreateContext();
        context.Agencies.Add(new Agency
        {
            AgencyId = "us.testorg",
            Label = "Test Org",
            CreatorId = "user-1",
            AdminContactId = "user-1",
            TechnicalContactId = "user-1"
        });
        await context.SaveChangesAsync();

        var controller = CreateController(context, "user-1");

        var result = await controller.AddVariableRegistration("us.testorg");

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<VariableRegistrationModel>(view.Model);
        Assert.Equal("us.testorg", model.AgencyId);
    }

    [Fact]
    public async Task AddVariableRegistration_Post_ForManagedAgency_CreatesRequestedVariable()
    {
        await using var context = CreateContext();
        context.Agencies.Add(new Agency
        {
            AgencyId = "us.testorg",
            Label = "Test Org",
            CreatorId = "user-1",
            AdminContactId = "user-1",
            TechnicalContactId = "user-1"
        });
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

        var controller = CreateController(context, "user-1");

        var result = await controller.AddVariableRegistration(new VariableRegistrationModel
        {
            AgencyId = "us.testorg",
            Name = "employment",
            Version = "1.0",
            ConceptIrdi = "urn:irdi:us.testorg:concept:worker-status:1.0",
            RepresentationIrdi = "urn:irdi:us.testorg:representation:boolean:1.0",
            CollectionMethod = "survey"
        });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("ViewAgency", redirect.ActionName);
        Assert.Equal("us.testorg", redirect.RouteValues!["agencyId"]);

        var variable = await context.VariableRegistrations.FirstOrDefaultAsync(v => v.Irdi == "urn:irdi:us.testorg:variable:employment:1.0");
        Assert.NotNull(variable);
        Assert.Equal(ApprovalState.Requested, variable!.ApprovalState);
    }

    [Fact]
    public async Task EditConceptRegistration_ForManagedAgency_ReturnsPrefilledViewModel()
    {
        await using var context = CreateContext();
        context.Agencies.Add(new Agency
        {
            AgencyId = "us.testorg",
            Label = "Test Org",
            CreatorId = "user-1",
            AdminContactId = "user-1",
            TechnicalContactId = "user-1"
        });
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

        var controller = CreateController(context, "user-1");

        var result = await controller.EditConceptRegistration("urn:irdi:us.testorg:concept:worker-status:1.0");

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ConceptRegistrationModel>(view.Model);
        Assert.Equal("Worker Status", model.Label);
    }

    [Fact]
    public async Task EditConceptRegistration_Post_ForRequestedConcept_UpdatesLabel()
    {
        await using var context = CreateContext();
        context.Agencies.Add(new Agency
        {
            AgencyId = "us.testorg",
            Label = "Test Org",
            CreatorId = "user-1",
            AdminContactId = "user-1",
            TechnicalContactId = "user-1"
        });
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

        var controller = CreateController(context, "user-1");

        var result = await controller.EditConceptRegistration(new ConceptRegistrationModel
        {
            AgencyId = "us.testorg",
            Name = "worker-status",
            Version = "1.0",
            Label = "Worker Status Updated"
        });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("ViewAgency", redirect.ActionName);

        var concept = await context.ConceptRegistrations.FirstAsync();
        Assert.Equal("Worker Status Updated", concept.Label);
    }

    [Fact]
    public async Task EditConceptRegistration_Post_ForApprovedConcept_ReturnsForbid()
    {
        await using var context = CreateContext();
        context.Agencies.Add(new Agency
        {
            AgencyId = "us.testorg",
            Label = "Test Org",
            CreatorId = "user-1",
            AdminContactId = "user-1",
            TechnicalContactId = "user-1"
        });
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

        var controller = CreateController(context, "user-1");

        var result = await controller.EditConceptRegistration(new ConceptRegistrationModel
        {
            AgencyId = "us.testorg",
            Name = "worker-status",
            Version = "1.0",
            Label = "Worker Status Updated"
        });

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task ViewAgency_ForManagedAgency_ExposesTripleRegistryCollections()
    {
        await using var context = CreateContext();
        context.Agencies.Add(new Agency
        {
            AgencyId = "us.testorg",
            Label = "Test Org",
            CreatorId = "user-1",
            AdminContactId = "user-1",
            TechnicalContactId = "user-1"
        });
        context.Assignments.Add(new Assignment
        {
            AssignmentId = "us.testorg",
            AgencyId = "us.testorg"
        });
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

        var controller = CreateController(context, "user-1");

        var result = await controller.ViewAgency("us.testorg");

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<AgencyOverviewModel>(view.Model);
        Assert.Single(model.Concepts);
        Assert.Single(model.Representations);
        Assert.Single(model.Variables);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"web-triple-registry-{System.Guid.NewGuid():N}")
            .Options;

        return new ApplicationDbContext(options);
    }

    private static ManageController CreateController(ApplicationDbContext context, string userId)
    {
        var controller = new ManageController(context, null!, null!, new NoOpEmailSender(), new NullStringLocalizer<ManageController>());
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, userId)
                ], "Test"))
            }
        };

        return controller;
    }

    private sealed class NoOpEmailSender : IEmailSender
    {
        public Task SendEmailAsync(string email, string subject, string htmlMessage) => Task.CompletedTask;
    }

    private sealed class NullStringLocalizer<T> : IStringLocalizer<T>
    {
        public LocalizedString this[string name] => new(name, name);
        public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(name, arguments));
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}