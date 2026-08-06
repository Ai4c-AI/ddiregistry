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
using Xunit;

namespace Ddi.Registry.Web.Tests;

public class TripleRegistryAdminApprovalTests
{
    [Fact]
    public async Task Index_IncludesRequestedTripleRegistryRecords()
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
        context.Users.Add(new ApplicationUser
        {
            Id = "user-1",
            UserName = "admin@example.com",
            Email = "admin@example.com",
            Name = "Admin User"
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

        var result = await controller.Index();

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ApproveModel>(view.Model);
        Assert.Single(model.RequestedConcepts);
        Assert.Single(model.RequestedRepresentations);
        Assert.Single(model.RequestedVariables);
    }

    [Fact]
    public async Task ApproveConceptRegistration_TransitionsToApproved()
    {
        await using var context = CreateContext();
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

        var result = await controller.ApproveConceptRegistration("urn:irdi:us.testorg:concept:worker-status:1.0");

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);

        var concept = await context.ConceptRegistrations.FirstAsync();
        Assert.Equal(ApprovalState.Approved, concept.ApprovalState);
    }

    [Fact]
    public async Task DeprecateConceptRegistration_TransitionsToDeprecated()
    {
        await using var context = CreateContext();
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

        var result = await controller.DeprecateConceptRegistration("urn:irdi:us.testorg:concept:worker-status:1.0");

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);

        var concept = await context.ConceptRegistrations.FirstAsync();
        Assert.Equal(ApprovalState.Deprecated, concept.ApprovalState);
    }

    [Fact]
    public async Task ApproveRepresentationRegistration_TransitionsToApproved()
    {
        await using var context = CreateContext();
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

        var controller = CreateController(context, "user-1");

        var result = await controller.ApproveRepresentationRegistration("urn:irdi:us.testorg:representation:boolean:1.0");

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);

        var representation = await context.RepresentationRegistrations.FirstAsync();
        Assert.Equal(ApprovalState.Approved, representation.ApprovalState);
    }

    [Fact]
    public async Task DeprecateRepresentationRegistration_TransitionsToDeprecated()
    {
        await using var context = CreateContext();
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

        var result = await controller.DeprecateRepresentationRegistration("urn:irdi:us.testorg:representation:boolean:1.0");

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);

        var representation = await context.RepresentationRegistrations.FirstAsync();
        Assert.Equal(ApprovalState.Deprecated, representation.ApprovalState);
    }

    [Fact]
    public async Task ApproveVariableRegistration_TransitionsToApproved()
    {
        await using var context = CreateContext();
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

        var result = await controller.ApproveVariableRegistration("urn:irdi:us.testorg:variable:employment:1.0");

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);

        var variable = await context.VariableRegistrations.FirstAsync();
        Assert.Equal(ApprovalState.Approved, variable.ApprovalState);
    }

    [Fact]
    public async Task DeprecateVariableRegistration_TransitionsToDeprecated()
    {
        await using var context = CreateContext();
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

        var controller = CreateController(context, "user-1");

        var result = await controller.DeprecateVariableRegistration("urn:irdi:us.testorg:variable:employment:1.0");

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);

        var variable = await context.VariableRegistrations.FirstAsync();
        Assert.Equal(ApprovalState.Deprecated, variable.ApprovalState);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"web-admin-triple-registry-{System.Guid.NewGuid():N}")
            .Options;

        return new ApplicationDbContext(options);
    }

    private static AdminController CreateController(ApplicationDbContext context, string userId)
    {
        var controller = new AdminController(context, null!, null!, new NoOpEmailSender());
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, userId),
                    new Claim(ClaimTypes.Role, "admin")
                ], "Test"))
            }
        };

        return controller;
    }

    private sealed class NoOpEmailSender : IEmailSender
    {
        public Task SendEmailAsync(string email, string subject, string htmlMessage) => Task.CompletedTask;
    }
}