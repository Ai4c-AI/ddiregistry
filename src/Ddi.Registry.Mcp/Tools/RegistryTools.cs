using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Ddi.Registry.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;

namespace Ddi.Registry.Mcp.Tools;

[McpServerToolType]
public sealed class RegistryTools
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public RegistryTools(ApplicationDbContext dbContext, IHttpContextAccessor httpContextAccessor)
    {
        _dbContext = dbContext;
        _httpContextAccessor = httpContextAccessor;
    }

    [McpServerTool(Name = "resolve_urn", Title = "Resolve DDI URN")]
    [Description("Resolve a DDI URN (urn:ddi:{agency}:{identifier}:{version}) to its HTTP resolution endpoints. Only works for Approved agencies. Requires scope ddi.registry.read.")]
    public async Task<ResolveUrnResult> ResolveUrn(
        [Description("DDI URN, e.g. urn:ddi:us.foo:bar:1")] string urn)
    {
        if (!HasScope("ddi.registry.read"))
            return new ResolveUrnResult { Found = false, Message = "Missing required scope 'ddi.registry.read'." };

        if (!DdiUrn.TryParse(urn, out var ddiUrn))
            return new ResolveUrnResult { Found = false, Message = $"Cannot parse URN: {urn}. Expected urn:ddi:{{agency}}:{{identifier}}:{{version}}." };

        var assignment = await _dbContext.Assignments
            .Include(a => a.HttpResolvers)
            .Include(a => a.Agency)
            .FirstOrDefaultAsync(a => a.AssignmentId == ddiUrn.Agency &&
                a.Agency.ApprovalState == ApprovalState.Approved);
        if (assignment == null)
            return new ResolveUrnResult { Found = false, Message = $"No agency assignment found for {ddiUrn.Agency}. The URN may not be approved." };

        var endpoints = new List<ResolveEndpoint>();
        foreach (var r in assignment.HttpResolvers)
            endpoints.Add(new ResolveEndpoint { ResolutionType = r.ResolutionType, Url = r.ResolveUrl(ddiUrn) });

        return new ResolveUrnResult { Found = true, AgencyId = ddiUrn.Agency, AgencyLabel = assignment.Agency.Label, Endpoints = endpoints };
    }

    [McpServerTool(Name = "list_agencies", Title = "List Agencies")]
    [Description("List DDI agencies. Optional country filters by AgencyId prefix ({countryCode}.). Returns all approval states (Requested/Approved/Deprecated/None). Requires scope ddi.registry.read.")]
    public async Task<ListAgenciesResult> ListAgencies(
        [Description("ISO country-code prefix, e.g. \"us\"; empty returns all")] string? country = null)
    {
        if (!HasScope("ddi.registry.read"))
            return new ListAgenciesResult { Ok = false, Message = "Missing required scope 'ddi.registry.read'." };

        var query = _dbContext.Agencies.AsQueryable();
        if (!string.IsNullOrWhiteSpace(country))
        {
            var escaped = country.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
            query = query.Where(a => EF.Functions.ILike(a.AgencyId, escaped + ".%"));
        }

        var agencies = await query.OrderBy(a => a.AgencyId)
            .Select(a => new AgencySummary
            {
                AgencyId = a.AgencyId, Label = a.Label, ApprovalState = a.ApprovalState,
                DateCreated = a.DateCreated, DateApproved = a.DateApproved
            }).ToListAsync();

        return new ListAgenciesResult { Ok = true, Agencies = agencies };
    }

    [McpServerTool(Name = "get_services", Title = "Get Agency Services")]
    [Description("Return all DNS SRV-style service records for the given Assignment (i.e. AgencyId). Requires scope ddi.registry.read.")]
    public async Task<GetServicesResult> GetServices(
        [Description("AssignmentId, usually equal to AgencyId, e.g. us.foo")] string assignmentId)
    {
        if (!HasScope("ddi.registry.read"))
            return new GetServicesResult { Ok = false, Message = "Missing required scope 'ddi.registry.read'." };

        var services = await _dbContext.GetServicesForAssignment(assignmentId);
        return new GetServicesResult
        {
            Ok = true,
            Services = services.Select(s => new ServiceSummary
            {
                ServiceId = s.ServiceId, Hostname = s.Hostname, Port = s.Port,
                ServiceName = s.ServiceName, Protocol = s.Protocol, Priority = s.Priority,
                Weight = s.Weight, TimeToLive = s.TimeToLive
            }).ToList()
        };
    }

    // Scope values can be emitted as multiple scope/scp claims by an IdP.
    private bool HasScope(string requiredScope)
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user == null || user.Identity?.IsAuthenticated != true) return false;
        return user.Claims
            .Where(c => c.Type == "scope" || c.Type == "scp")
            .SelectMany(c => c.Value.Split((char[]?)null, System.StringSplitOptions.RemoveEmptyEntries))
            .Contains(requiredScope, System.StringComparer.Ordinal);
    }
}

public class ResolveUrnResult
{
    public bool Found { get; set; }
    public string? AgencyId { get; set; }
    public string? AgencyLabel { get; set; }
    public string? Message { get; set; }
    public List<ResolveEndpoint> Endpoints { get; set; } = new();
}

public class ResolveEndpoint
{
    public string? ResolutionType { get; set; }
    public string? Url { get; set; }
}

public class ListAgenciesResult { public bool Ok { get; set; } public string? Message { get; set; } public List<AgencySummary> Agencies { get; set; } = new(); }
public class AgencySummary { public string? AgencyId { get; set; } public string? Label { get; set; } public ApprovalState ApprovalState { get; set; } public DateTime DateCreated { get; set; } public DateTime? DateApproved { get; set; } }

public class GetServicesResult { public bool Ok { get; set; } public string? Message { get; set; } public List<ServiceSummary> Services { get; set; } = new(); }
public class ServiceSummary { public string? ServiceId { get; set; } public string? Hostname { get; set; } public int Port { get; set; } public string? ServiceName { get; set; } public string? Protocol { get; set; } public int Priority { get; set; } public int Weight { get; set; } public int TimeToLive { get; set; } }
