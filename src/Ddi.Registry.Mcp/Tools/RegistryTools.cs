using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Ddi.Registry.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
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

    [McpServerTool(Name = "list_concepts", Title = "List Concepts")]
    [Description("List registered DDI concepts across all approval states. Requires scope ddi.registry.read.")]
    public async Task<ListConceptsResult> ListConcepts()
    {
        if (!HasScope("ddi.registry.read"))
            return new ListConceptsResult { Ok = false, Message = "Missing required scope 'ddi.registry.read'." };

        var concepts = await _dbContext.ConceptRegistrations
            .OrderBy(c => c.AgencyId)
            .ThenBy(c => c.Name)
            .ThenBy(c => c.Version)
            .Select(c => new ConceptSummary
            {
                Irdi = c.Irdi,
                AgencyId = c.AgencyId,
                Name = c.Name,
                Version = c.Version,
                Label = c.Label,
                ApprovalState = c.ApprovalState,
                CreatedAt = c.CreatedAt
            })
            .ToListAsync();

        return new ListConceptsResult { Ok = true, Concepts = concepts };
    }

    [McpServerTool(Name = "get_concept", Title = "Get Concept")]
    [Description("Get a registered DDI concept by IRDI. Requires scope ddi.registry.read.")]
    public async Task<GetConceptResult> GetConcept(
        [Description("Concept IRDI, e.g. urn:irdi:us.foo:concept:worker-status:1.0")] string irdi)
    {
        if (!HasScope("ddi.registry.read"))
            return new GetConceptResult { Ok = false, Message = "Missing required scope 'ddi.registry.read'." };

        var concept = await _dbContext.ConceptRegistrations.FirstOrDefaultAsync(c => c.Irdi == irdi);
        if (concept == null)
            return new GetConceptResult { Ok = false, Message = $"Concept {irdi} was not found." };

        return new GetConceptResult
        {
            Ok = true,
            Concept = new ConceptSummary
            {
                Irdi = concept.Irdi,
                AgencyId = concept.AgencyId,
                Name = concept.Name,
                Version = concept.Version,
                Label = concept.Label,
                ApprovalState = concept.ApprovalState,
                CreatedAt = concept.CreatedAt
            }
        };
    }

    [McpServerTool(Name = "list_representations", Title = "List Representations")]
    [Description("List registered DDI representations across all approval states. Requires scope ddi.registry.read.")]
    public async Task<ListRepresentationsResult> ListRepresentations()
    {
        if (!HasScope("ddi.registry.read"))
            return new ListRepresentationsResult { Ok = false, Message = "Missing required scope 'ddi.registry.read'." };

        var representations = await _dbContext.RepresentationRegistrations
            .OrderBy(r => r.AgencyId)
            .ThenBy(r => r.Name)
            .ThenBy(r => r.Version)
            .Select(r => new RepresentationSummary
            {
                Irdi = r.Irdi,
                AgencyId = r.AgencyId,
                Name = r.Name,
                Version = r.Version,
                Type = r.Type,
                JsonSchema = r.JsonSchema,
                ShaclTemplateIrdi = r.ShaclTemplateIrdi,
                ApprovalState = r.ApprovalState,
                CreatedAt = r.CreatedAt
            })
            .ToListAsync();

        return new ListRepresentationsResult { Ok = true, Representations = representations };
    }

    [McpServerTool(Name = "get_representation", Title = "Get Representation")]
    [Description("Get a registered DDI representation by IRDI. Requires scope ddi.registry.read.")]
    public async Task<GetRepresentationResult> GetRepresentation(
        [Description("Representation IRDI, e.g. urn:irdi:us.foo:representation:boolean:1.0")] string irdi)
    {
        if (!HasScope("ddi.registry.read"))
            return new GetRepresentationResult { Ok = false, Message = "Missing required scope 'ddi.registry.read'." };

        var representation = await _dbContext.RepresentationRegistrations.FirstOrDefaultAsync(r => r.Irdi == irdi);
        if (representation == null)
            return new GetRepresentationResult { Ok = false, Message = $"Representation {irdi} was not found." };

        return new GetRepresentationResult
        {
            Ok = true,
            Representation = new RepresentationSummary
            {
                Irdi = representation.Irdi,
                AgencyId = representation.AgencyId,
                Name = representation.Name,
                Version = representation.Version,
                Type = representation.Type,
                JsonSchema = representation.JsonSchema,
                ShaclTemplateIrdi = representation.ShaclTemplateIrdi,
                ApprovalState = representation.ApprovalState,
                CreatedAt = representation.CreatedAt
            }
        };
    }

    [McpServerTool(Name = "get_variable_publishability", Title = "Get Variable Publishability")]
    [Description("Return whether a variable is publishable based on the approval state of the variable, its concept, and its representation. Requires scope ddi.registry.read.")]
    public async Task<GetVariablePublishabilityResult> GetVariablePublishability(
        [Description("Variable IRDI, e.g. urn:irdi:us.foo:variable:employment:1.0")] string irdi)
    {
        if (!HasScope("ddi.registry.read"))
            return new GetVariablePublishabilityResult { Ok = false, Message = "Missing required scope 'ddi.registry.read'." };

        var variable = await _dbContext.VariableRegistrations.FirstOrDefaultAsync(v => v.Irdi == irdi);
        if (variable == null)
            return new GetVariablePublishabilityResult { Ok = false, Message = $"Variable {irdi} was not found." };

        var concept = await _dbContext.ConceptRegistrations.FirstOrDefaultAsync(c => c.Irdi == variable.ConceptIrdi);
        var representation = await _dbContext.RepresentationRegistrations.FirstOrDefaultAsync(r => r.Irdi == variable.RepresentationIrdi);

        if (concept == null || representation == null)
            return new GetVariablePublishabilityResult { Ok = false, Message = $"Variable {irdi} has missing concept or representation dependencies." };

        return new GetVariablePublishabilityResult
        {
            Ok = true,
            Irdi = variable.Irdi,
            Name = variable.Name,
            VariableApprovalState = variable.ApprovalState,
            ConceptApprovalState = concept.ApprovalState,
            RepresentationApprovalState = representation.ApprovalState,
            IsPublishable = RegistrationValidation.IsVariablePublishable(
                variable.ApprovalState,
                concept.ApprovalState,
                representation.ApprovalState)
        };
    }

    [McpServerTool(Name = "list_variables", Title = "List Variables")]
    [Description("List registered DDI variables with derived publishability. Requires scope ddi.registry.read.")]
    public async Task<ListVariablesResult> ListVariables()
    {
        if (!HasScope("ddi.registry.read"))
            return new ListVariablesResult { Ok = false, Message = "Missing required scope 'ddi.registry.read'." };

        var variables = await (
            from variable in _dbContext.VariableRegistrations
            join concept in _dbContext.ConceptRegistrations on variable.ConceptIrdi equals concept.Irdi
            join representation in _dbContext.RepresentationRegistrations on variable.RepresentationIrdi equals representation.Irdi
            orderby variable.AgencyId, variable.Name, variable.Version
            select new VariableSummary
            {
                Irdi = variable.Irdi,
                AgencyId = variable.AgencyId,
                Name = variable.Name,
                Version = variable.Version,
                ConceptIrdi = variable.ConceptIrdi,
                RepresentationIrdi = variable.RepresentationIrdi,
                ApprovalState = variable.ApprovalState,
                IsPublishable = RegistrationValidation.IsVariablePublishable(
                    variable.ApprovalState,
                    concept.ApprovalState,
                    representation.ApprovalState)
            }).ToListAsync();

        return new ListVariablesResult { Ok = true, Variables = variables };
    }

    [McpServerTool(Name = "get_variable", Title = "Get Variable")]
    [Description("Get a registered DDI variable by IRDI with derived publishability. Requires scope ddi.registry.read.")]
    public async Task<GetVariableResult> GetVariable(
        [Description("Variable IRDI, e.g. urn:irdi:us.foo:variable:employment:1.0")] string irdi)
    {
        if (!HasScope("ddi.registry.read"))
            return new GetVariableResult { Ok = false, Message = "Missing required scope 'ddi.registry.read'." };

        var result = await (
            from variable in _dbContext.VariableRegistrations
            join concept in _dbContext.ConceptRegistrations on variable.ConceptIrdi equals concept.Irdi
            join representation in _dbContext.RepresentationRegistrations on variable.RepresentationIrdi equals representation.Irdi
            where variable.Irdi == irdi
            select new VariableSummary
            {
                Irdi = variable.Irdi,
                AgencyId = variable.AgencyId,
                Name = variable.Name,
                Version = variable.Version,
                ConceptIrdi = variable.ConceptIrdi,
                RepresentationIrdi = variable.RepresentationIrdi,
                ApprovalState = variable.ApprovalState,
                IsPublishable = RegistrationValidation.IsVariablePublishable(
                    variable.ApprovalState,
                    concept.ApprovalState,
                    representation.ApprovalState)
            }).FirstOrDefaultAsync();

        if (result == null)
            return new GetVariableResult { Ok = false, Message = $"Variable {irdi} was not found." };

        return new GetVariableResult { Ok = true, Variable = result };
    }

    [McpServerTool(Name = "request_concept", Title = "Request Concept")]
    [Description("Submit a new DDI concept registration request (state=Requested). Requires scope ddi.registry.write.")]
    public async Task<RequestConceptResult> RequestConcept(
        [Description("Owning agency id, e.g. us.foo")] string agencyId,
        [Description("Concept short name, e.g. worker-status")] string name,
        [Description("Version, e.g. 1.0")] string version,
        [Description("Display label")] string label)
    {
        if (!HasScope("ddi.registry.write"))
            return new RequestConceptResult { Success = false, Message = "Missing required scope 'ddi.registry.write'." };

        if (string.IsNullOrWhiteSpace(agencyId) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(version) || string.IsNullOrWhiteSpace(label))
            return new RequestConceptResult { Success = false, Message = "agencyId, name, version, and label are required." };

        var user = _httpContextAccessor.HttpContext?.User;
        if (user == null || user.Identity?.IsAuthenticated != true)
            return new RequestConceptResult { Success = false, Message = "No valid identity token presented." };

        var email = user.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? user.FindFirst("email")?.Value;
        var sub = user.FindFirst("sub")?.Value;
        var account = !string.IsNullOrWhiteSpace(email)
            ? await _dbContext.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == email.ToUpperInvariant())
            : null;
        account ??= !string.IsNullOrWhiteSpace(sub) ? await _dbContext.Users.FindAsync(sub) : null;
        if (account == null)
            return new RequestConceptResult { Success = false, Message = "Caller identity could not be mapped to an existing AspNetUsers row." };

        var irdi = RegistryIrdi.BuildConceptIrdi(agencyId, name, version);
        var existing = await _dbContext.ConceptRegistrations.FirstOrDefaultAsync(c => c.Irdi == irdi);
        if (existing != null)
            return new RequestConceptResult { Success = false, Message = $"Concept {irdi} already exists." };

        var concept = new ConceptRegistration
        {
            Irdi = irdi,
            AgencyId = agencyId,
            Name = name,
            Version = version,
            Label = label,
            ApprovalState = ApprovalState.Requested
        };

        _dbContext.ConceptRegistrations.Add(concept);
        await _dbContext.SaveChangesAsync();

        return new RequestConceptResult
        {
            Success = true,
            Irdi = irdi,
            ApprovalState = ApprovalState.Requested,
            Message = $"Concept {irdi} submitted with state Requested; pending admin approval."
        };
    }

    [McpServerTool(Name = "update_concept_request", Title = "Update Concept Request")]
    [Description("Update a Requested DDI concept registration request. Requires scope ddi.registry.write.")]
    public async Task<UpdateConceptRequestResult> UpdateConceptRequest(
        [Description("Concept IRDI, e.g. urn:irdi:us.foo:concept:worker-status:1.0")] string irdi,
        [Description("Updated display label")] string label)
    {
        if (!HasScope("ddi.registry.write"))
            return new UpdateConceptRequestResult { Success = false, Message = "Missing required scope 'ddi.registry.write'." };

        if (string.IsNullOrWhiteSpace(irdi) || string.IsNullOrWhiteSpace(label))
            return new UpdateConceptRequestResult { Success = false, Message = "irdi and label are required." };

        var user = _httpContextAccessor.HttpContext?.User;
        if (user == null || user.Identity?.IsAuthenticated != true)
            return new UpdateConceptRequestResult { Success = false, Message = "No valid identity token presented." };

        var email = user.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? user.FindFirst("email")?.Value;
        var sub = user.FindFirst("sub")?.Value;
        var account = !string.IsNullOrWhiteSpace(email)
            ? await _dbContext.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == email.ToUpperInvariant())
            : null;
        account ??= !string.IsNullOrWhiteSpace(sub) ? await _dbContext.Users.FindAsync(sub) : null;
        if (account == null)
            return new UpdateConceptRequestResult { Success = false, Message = "Caller identity could not be mapped to an existing AspNetUsers row." };

        var concept = await _dbContext.ConceptRegistrations.FirstOrDefaultAsync(c => c.Irdi == irdi);
        if (concept == null)
            return new UpdateConceptRequestResult { Success = false, Message = $"Concept {irdi} was not found." };

        if (concept.ApprovalState != ApprovalState.Requested)
            return new UpdateConceptRequestResult { Success = false, Message = "Only Requested concepts can be updated via update_concept_request." };

        concept.Label = label;
        concept.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        return new UpdateConceptRequestResult
        {
            Success = true,
            Irdi = concept.Irdi,
            ApprovalState = concept.ApprovalState,
            Message = $"Concept {concept.Irdi} updated."
        };
    }

    [McpServerTool(Name = "request_representation", Title = "Request Representation")]
    [Description("Submit a new DDI representation registration request (state=Requested). Requires scope ddi.registry.write.")]
    public async Task<RequestRepresentationResult> RequestRepresentation(
        [Description("Owning agency id, e.g. us.foo")] string agencyId,
        [Description("Representation short name, e.g. boolean")] string name,
        [Description("Version, e.g. 1.0")] string version,
        [Description("Representation type, e.g. Code")] string type,
        [Description("JsonSchema content as a JSON string")] string jsonSchema,
        [Description("Optional SHACL template IRDI")] string? shaclTemplateIrdi = null)
    {
        if (!HasScope("ddi.registry.write"))
            return new RequestRepresentationResult { Success = false, Message = "Missing required scope 'ddi.registry.write'." };

        if (string.IsNullOrWhiteSpace(agencyId) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(version) || string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(jsonSchema))
            return new RequestRepresentationResult { Success = false, Message = "agencyId, name, version, type, and jsonSchema are required." };

        var user = _httpContextAccessor.HttpContext?.User;
        if (user == null || user.Identity?.IsAuthenticated != true)
            return new RequestRepresentationResult { Success = false, Message = "No valid identity token presented." };

        var email = user.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? user.FindFirst("email")?.Value;
        var sub = user.FindFirst("sub")?.Value;
        var account = !string.IsNullOrWhiteSpace(email)
            ? await _dbContext.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == email.ToUpperInvariant())
            : null;
        account ??= !string.IsNullOrWhiteSpace(sub) ? await _dbContext.Users.FindAsync(sub) : null;
        if (account == null)
            return new RequestRepresentationResult { Success = false, Message = "Caller identity could not be mapped to an existing AspNetUsers row." };

        var irdi = RegistryIrdi.BuildRepresentationIrdi(agencyId, name, version);
        var existing = await _dbContext.RepresentationRegistrations.FirstOrDefaultAsync(r => r.Irdi == irdi);
        if (existing != null)
            return new RequestRepresentationResult { Success = false, Message = $"Representation {irdi} already exists." };

        var representation = new RepresentationRegistration
        {
            Irdi = irdi,
            AgencyId = agencyId,
            Name = name,
            Version = version,
            Type = type,
            JsonSchema = jsonSchema,
            ShaclTemplateIrdi = shaclTemplateIrdi,
            ApprovalState = ApprovalState.Requested
        };

        _dbContext.RepresentationRegistrations.Add(representation);
        await _dbContext.SaveChangesAsync();

        return new RequestRepresentationResult
        {
            Success = true,
            Irdi = irdi,
            ApprovalState = ApprovalState.Requested,
            Message = $"Representation {irdi} submitted with state Requested; pending admin approval."
        };
    }

    [McpServerTool(Name = "update_representation_request", Title = "Update Representation Request")]
    [Description("Update a Requested DDI representation registration request. Requires scope ddi.registry.write.")]
    public async Task<UpdateRepresentationRequestResult> UpdateRepresentationRequest(
        [Description("Representation IRDI, e.g. urn:irdi:us.foo:representation:boolean:1.0")] string irdi,
        [Description("Updated representation type")] string type,
        [Description("Updated JsonSchema content as a JSON string")] string jsonSchema)
    {
        if (!HasScope("ddi.registry.write"))
            return new UpdateRepresentationRequestResult { Success = false, Message = "Missing required scope 'ddi.registry.write'." };

        if (string.IsNullOrWhiteSpace(irdi) || string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(jsonSchema))
            return new UpdateRepresentationRequestResult { Success = false, Message = "irdi, type, and jsonSchema are required." };

        var user = _httpContextAccessor.HttpContext?.User;
        if (user == null || user.Identity?.IsAuthenticated != true)
            return new UpdateRepresentationRequestResult { Success = false, Message = "No valid identity token presented." };

        var email = user.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? user.FindFirst("email")?.Value;
        var sub = user.FindFirst("sub")?.Value;
        var account = !string.IsNullOrWhiteSpace(email)
            ? await _dbContext.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == email.ToUpperInvariant())
            : null;
        account ??= !string.IsNullOrWhiteSpace(sub) ? await _dbContext.Users.FindAsync(sub) : null;
        if (account == null)
            return new UpdateRepresentationRequestResult { Success = false, Message = "Caller identity could not be mapped to an existing AspNetUsers row." };

        var representation = await _dbContext.RepresentationRegistrations.FirstOrDefaultAsync(r => r.Irdi == irdi);
        if (representation == null)
            return new UpdateRepresentationRequestResult { Success = false, Message = $"Representation {irdi} was not found." };

        if (representation.ApprovalState != ApprovalState.Requested)
            return new UpdateRepresentationRequestResult { Success = false, Message = "Only Requested representations can be updated via update_representation_request." };

        representation.Type = type;
        representation.JsonSchema = jsonSchema;
        representation.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        return new UpdateRepresentationRequestResult
        {
            Success = true,
            Irdi = representation.Irdi,
            ApprovalState = representation.ApprovalState,
            Message = $"Representation {representation.Irdi} updated."
        };
    }

    [McpServerTool(Name = "request_variable", Title = "Request Variable")]
    [Description("Submit a new DDI variable registration request (state=Requested). Requires scope ddi.registry.write.")]
    public async Task<RequestVariableResult> RequestVariable(
        [Description("Owning agency id, e.g. us.foo")] string agencyId,
        [Description("Variable short name, e.g. employment")] string name,
        [Description("Version, e.g. 1.0")] string version,
        [Description("Concept IRDI reference")] string conceptIrdi,
        [Description("Representation IRDI reference")] string representationIrdi)
    {
        if (!HasScope("ddi.registry.write"))
            return new RequestVariableResult { Success = false, Message = "Missing required scope 'ddi.registry.write'." };

        if (string.IsNullOrWhiteSpace(agencyId) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(version)
            || string.IsNullOrWhiteSpace(conceptIrdi) || string.IsNullOrWhiteSpace(representationIrdi))
            return new RequestVariableResult { Success = false, Message = "agencyId, name, version, conceptIrdi, and representationIrdi are required." };

        var user = _httpContextAccessor.HttpContext?.User;
        if (user == null || user.Identity?.IsAuthenticated != true)
            return new RequestVariableResult { Success = false, Message = "No valid identity token presented." };

        var email = user.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? user.FindFirst("email")?.Value;
        var sub = user.FindFirst("sub")?.Value;
        var account = !string.IsNullOrWhiteSpace(email)
            ? await _dbContext.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == email.ToUpperInvariant())
            : null;
        account ??= !string.IsNullOrWhiteSpace(sub) ? await _dbContext.Users.FindAsync(sub) : null;
        if (account == null)
            return new RequestVariableResult { Success = false, Message = "Caller identity could not be mapped to an existing AspNetUsers row." };

        var concept = await _dbContext.ConceptRegistrations.FirstOrDefaultAsync(c => c.Irdi == conceptIrdi);
        var representation = await _dbContext.RepresentationRegistrations.FirstOrDefaultAsync(r => r.Irdi == representationIrdi);
        if (concept == null || representation == null)
            return new RequestVariableResult { Success = false, Message = "Concept or representation reference was not found." };

        var validation = RegistrationValidation.ValidateVariableReferences(
            agencyId,
            concept.AgencyId,
            representation.AgencyId,
            allowCrossAgency: false);
        if (!validation.IsValid)
            return new RequestVariableResult { Success = false, Message = $"{validation.ErrorCode}: {validation.ErrorMessage}" };

        var irdi = RegistryIrdi.BuildVariableIrdi(agencyId, name, version);
        var existing = await _dbContext.VariableRegistrations.FirstOrDefaultAsync(v => v.Irdi == irdi);
        if (existing != null)
            return new RequestVariableResult { Success = false, Message = $"Variable {irdi} already exists." };

        var variable = new VariableRegistration
        {
            Irdi = irdi,
            AgencyId = agencyId,
            Name = name,
            Version = version,
            ConceptIrdi = conceptIrdi,
            RepresentationIrdi = representationIrdi,
            ApprovalState = ApprovalState.Requested
        };

        _dbContext.VariableRegistrations.Add(variable);
        await _dbContext.SaveChangesAsync();

        return new RequestVariableResult
        {
            Success = true,
            Irdi = irdi,
            ApprovalState = ApprovalState.Requested,
            Message = $"Variable {irdi} submitted with state Requested; pending admin approval."
        };
    }

    [McpServerTool(Name = "update_variable_request", Title = "Update Variable Request")]
    [Description("Update a Requested DDI variable registration request. Requires scope ddi.registry.write.")]
    public async Task<UpdateVariableRequestResult> UpdateVariableRequest(
        [Description("Variable IRDI, e.g. urn:irdi:us.foo:variable:employment:1.0")] string irdi,
        [Description("Updated collection method")] string collectionMethod)
    {
        if (!HasScope("ddi.registry.write"))
            return new UpdateVariableRequestResult { Success = false, Message = "Missing required scope 'ddi.registry.write'." };

        if (string.IsNullOrWhiteSpace(irdi) || string.IsNullOrWhiteSpace(collectionMethod))
            return new UpdateVariableRequestResult { Success = false, Message = "irdi and collectionMethod are required." };

        var user = _httpContextAccessor.HttpContext?.User;
        if (user == null || user.Identity?.IsAuthenticated != true)
            return new UpdateVariableRequestResult { Success = false, Message = "No valid identity token presented." };

        var email = user.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? user.FindFirst("email")?.Value;
        var sub = user.FindFirst("sub")?.Value;
        var account = !string.IsNullOrWhiteSpace(email)
            ? await _dbContext.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == email.ToUpperInvariant())
            : null;
        account ??= !string.IsNullOrWhiteSpace(sub) ? await _dbContext.Users.FindAsync(sub) : null;
        if (account == null)
            return new UpdateVariableRequestResult { Success = false, Message = "Caller identity could not be mapped to an existing AspNetUsers row." };

        var variable = await _dbContext.VariableRegistrations.FirstOrDefaultAsync(v => v.Irdi == irdi);
        if (variable == null)
            return new UpdateVariableRequestResult { Success = false, Message = $"Variable {irdi} was not found." };

        if (variable.ApprovalState != ApprovalState.Requested)
            return new UpdateVariableRequestResult { Success = false, Message = "Only Requested variables can be updated via update_variable_request." };

        variable.CollectionMethod = collectionMethod;
        variable.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        return new UpdateVariableRequestResult
        {
            Success = true,
            Irdi = variable.Irdi,
            ApprovalState = variable.ApprovalState,
            Message = $"Variable {variable.Irdi} updated."
        };
    }

    [McpServerTool(Name = "approve_concept", Title = "Approve Concept")]
    [Description("Approve a Requested DDI concept registration. Requires scope ddi.registry.write and admin role.")]
    public async Task<ApproveConceptResult> ApproveConcept(
        [Description("Concept IRDI, e.g. urn:irdi:us.foo:concept:worker-status:1.0")] string irdi)
    {
        if (!HasScope("ddi.registry.write"))
            return new ApproveConceptResult { Success = false, Message = "Missing required scope 'ddi.registry.write'." };

        if (!IsAdmin())
            return new ApproveConceptResult { Success = false, Message = "Only admin or SuperAdmin can approve concepts." };

        if (string.IsNullOrWhiteSpace(irdi))
            return new ApproveConceptResult { Success = false, Message = "irdi is required." };

        var concept = await _dbContext.ConceptRegistrations.FirstOrDefaultAsync(c => c.Irdi == irdi);
        if (concept == null)
            return new ApproveConceptResult { Success = false, Message = $"Concept {irdi} was not found." };

        concept.ApprovalState = ApprovalState.Approved;
        concept.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        return new ApproveConceptResult
        {
            Success = true,
            Irdi = concept.Irdi,
            ApprovalState = concept.ApprovalState,
            Message = $"Concept {concept.Irdi} approved."
        };
    }

    [McpServerTool(Name = "deprecate_concept", Title = "Deprecate Concept")]
    [Description("Deprecate a DDI concept registration. Requires scope ddi.registry.write and admin role.")]
    public async Task<DeprecateConceptResult> DeprecateConcept(
        [Description("Concept IRDI, e.g. urn:irdi:us.foo:concept:worker-status:1.0")] string irdi)
    {
        if (!HasScope("ddi.registry.write"))
            return new DeprecateConceptResult { Success = false, Message = "Missing required scope 'ddi.registry.write'." };

        if (!IsAdmin())
            return new DeprecateConceptResult { Success = false, Message = "Only admin or SuperAdmin can deprecate concepts." };

        if (string.IsNullOrWhiteSpace(irdi))
            return new DeprecateConceptResult { Success = false, Message = "irdi is required." };

        var concept = await _dbContext.ConceptRegistrations.FirstOrDefaultAsync(c => c.Irdi == irdi);
        if (concept == null)
            return new DeprecateConceptResult { Success = false, Message = $"Concept {irdi} was not found." };

        concept.ApprovalState = ApprovalState.Deprecated;
        concept.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        return new DeprecateConceptResult
        {
            Success = true,
            Irdi = concept.Irdi,
            ApprovalState = concept.ApprovalState,
            Message = $"Concept {concept.Irdi} deprecated."
        };
    }

    [McpServerTool(Name = "approve_representation", Title = "Approve Representation")]
    [Description("Approve a Requested DDI representation registration. Requires scope ddi.registry.write and admin role.")]
    public async Task<ApproveRepresentationResult> ApproveRepresentation(
        [Description("Representation IRDI, e.g. urn:irdi:us.foo:representation:boolean:1.0")] string irdi)
    {
        if (!HasScope("ddi.registry.write"))
            return new ApproveRepresentationResult { Success = false, Message = "Missing required scope 'ddi.registry.write'." };

        if (!IsAdmin())
            return new ApproveRepresentationResult { Success = false, Message = "Only admin or SuperAdmin can approve representations." };

        if (string.IsNullOrWhiteSpace(irdi))
            return new ApproveRepresentationResult { Success = false, Message = "irdi is required." };

        var representation = await _dbContext.RepresentationRegistrations.FirstOrDefaultAsync(r => r.Irdi == irdi);
        if (representation == null)
            return new ApproveRepresentationResult { Success = false, Message = $"Representation {irdi} was not found." };

        representation.ApprovalState = ApprovalState.Approved;
        representation.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        return new ApproveRepresentationResult
        {
            Success = true,
            Irdi = representation.Irdi,
            ApprovalState = representation.ApprovalState,
            Message = $"Representation {representation.Irdi} approved."
        };
    }

    [McpServerTool(Name = "deprecate_representation", Title = "Deprecate Representation")]
    [Description("Deprecate a DDI representation registration. Requires scope ddi.registry.write and admin role.")]
    public async Task<DeprecateRepresentationResult> DeprecateRepresentation(
        [Description("Representation IRDI, e.g. urn:irdi:us.foo:representation:boolean:1.0")] string irdi)
    {
        if (!HasScope("ddi.registry.write"))
            return new DeprecateRepresentationResult { Success = false, Message = "Missing required scope 'ddi.registry.write'." };

        if (!IsAdmin())
            return new DeprecateRepresentationResult { Success = false, Message = "Only admin or SuperAdmin can deprecate representations." };

        if (string.IsNullOrWhiteSpace(irdi))
            return new DeprecateRepresentationResult { Success = false, Message = "irdi is required." };

        var representation = await _dbContext.RepresentationRegistrations.FirstOrDefaultAsync(r => r.Irdi == irdi);
        if (representation == null)
            return new DeprecateRepresentationResult { Success = false, Message = $"Representation {irdi} was not found." };

        representation.ApprovalState = ApprovalState.Deprecated;
        representation.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        return new DeprecateRepresentationResult
        {
            Success = true,
            Irdi = representation.Irdi,
            ApprovalState = representation.ApprovalState,
            Message = $"Representation {representation.Irdi} deprecated."
        };
    }

    [McpServerTool(Name = "approve_variable", Title = "Approve Variable")]
    [Description("Approve a Requested DDI variable registration. Requires scope ddi.registry.write and admin role.")]
    public async Task<ApproveVariableResult> ApproveVariable(
        [Description("Variable IRDI, e.g. urn:irdi:us.foo:variable:employment:1.0")] string irdi)
    {
        if (!HasScope("ddi.registry.write"))
            return new ApproveVariableResult { Success = false, Message = "Missing required scope 'ddi.registry.write'." };

        if (!IsAdmin())
            return new ApproveVariableResult { Success = false, Message = "Only admin or SuperAdmin can approve variables." };

        if (string.IsNullOrWhiteSpace(irdi))
            return new ApproveVariableResult { Success = false, Message = "irdi is required." };

        var variable = await _dbContext.VariableRegistrations.FirstOrDefaultAsync(v => v.Irdi == irdi);
        if (variable == null)
            return new ApproveVariableResult { Success = false, Message = $"Variable {irdi} was not found." };

        variable.ApprovalState = ApprovalState.Approved;
        variable.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        return new ApproveVariableResult
        {
            Success = true,
            Irdi = variable.Irdi,
            ApprovalState = variable.ApprovalState,
            Message = $"Variable {variable.Irdi} approved."
        };
    }

    [McpServerTool(Name = "deprecate_variable", Title = "Deprecate Variable")]
    [Description("Deprecate a DDI variable registration. Requires scope ddi.registry.write and admin role.")]
    public async Task<DeprecateVariableResult> DeprecateVariable(
        [Description("Variable IRDI, e.g. urn:irdi:us.foo:variable:employment:1.0")] string irdi)
    {
        if (!HasScope("ddi.registry.write"))
            return new DeprecateVariableResult { Success = false, Message = "Missing required scope 'ddi.registry.write'." };

        if (!IsAdmin())
            return new DeprecateVariableResult { Success = false, Message = "Only admin or SuperAdmin can deprecate variables." };

        if (string.IsNullOrWhiteSpace(irdi))
            return new DeprecateVariableResult { Success = false, Message = "irdi is required." };

        var variable = await _dbContext.VariableRegistrations.FirstOrDefaultAsync(v => v.Irdi == irdi);
        if (variable == null)
            return new DeprecateVariableResult { Success = false, Message = $"Variable {irdi} was not found." };

        variable.ApprovalState = ApprovalState.Deprecated;
        variable.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        return new DeprecateVariableResult
        {
            Success = true,
            Irdi = variable.Irdi,
            ApprovalState = variable.ApprovalState,
            Message = $"Variable {variable.Irdi} deprecated."
        };
    }

    [McpServerTool(Name = "request_agency", Title = "Request New Agency")]
    [Description("Submit a new DDI agency identifier request (state=Requested). org is the suggested AgencyId, validated exactly like the web app (ISO 3166 / int / uk). Caller identity is mapped from the validated external IdP token to an existing AspNetUsers row. Requires scope ddi.registry.write. No email is sent.")]
    public async Task<RequestAgencyResult> RequestAgency(
        [Description("Agency display label")] string label,
        [Description("Suggested AgencyId, e.g. us.myorg")] string org)
    {
        if (!HasScope("ddi.registry.write"))
            return new RequestAgencyResult { Success = false, Message = "Missing required scope 'ddi.registry.write'." };

        var validation = AgencyIdValidator.Validate(org, label);
        if (!validation.Ok) return new RequestAgencyResult { Success = false, Message = validation.Error };

        // Identity mapping BEFORE the duplicate check (spec §6): a caller must be mapped to an
        // existing AspNetUsers row before any agency-ID existence signal is emitted, so an
        // unmappable caller cannot probe existing IDs via the "already exists" vs
        // "could not be mapped" error split.
        var user = _httpContextAccessor.HttpContext?.User;
        if (user == null || user.Identity?.IsAuthenticated != true)
            return new RequestAgencyResult { Success = false, Message = "No valid identity token presented." };

        var email = user.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? user.FindFirst("email")?.Value;
        var sub = user.FindFirst("sub")?.Value;
        var account = !string.IsNullOrWhiteSpace(email)
            ? await _dbContext.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == email.ToUpperInvariant())
            : null;
        account ??= !string.IsNullOrWhiteSpace(sub) ? await _dbContext.Users.FindAsync(sub) : null;
        if (account == null)
            return new RequestAgencyResult { Success = false, Message = "Caller identity could not be mapped to an existing AspNetUsers row." };

        // Duplicate check AFTER identity mapping.
        var existing = await _dbContext.Agencies.FindAsync(org);
        if (existing != null) return new RequestAgencyResult { Success = false, Message = $"Agency identifier {org} already exists." };

        var agency = new Agency
        {
            AgencyId = org,
            Label = label,
            ApprovalState = ApprovalState.Requested,
            CreatorId = account.Id,
            AdminContactId = account.Id,
            TechnicalContactId = account.Id
        };
        _dbContext.Agencies.Add(agency);

        try
        {
            await _dbContext.SaveChangesAsync();
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException ex) when (IsAgencyPrimaryKeyViolation(ex))
        {
            // Concurrent duplicate request: return the same rejection as a pre-existing agency.
            return new RequestAgencyResult { Success = false, Message = $"Agency identifier {org} already exists." };
        }

        return new RequestAgencyResult
        {
            Success = true,
            AgencyId = org,
            ApprovalState = ApprovalState.Requested,
            Message = $"Agency {org} submitted with state Requested; pending admin approval."
        };
    }

    // Detect a Postgres unique-constraint violation (23505, PK_Agencies); when the inner
    // exception is not a Postgres unique violation, conservatively let it propagate.
    private static bool IsAgencyPrimaryKeyViolation(Microsoft.EntityFrameworkCore.DbUpdateException ex)
    {
        var pgEx = ex.InnerException as Npgsql.PostgresException;
        return pgEx?.SqlState == "23505" && pgEx.ConstraintName == "PK_Agencies";
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

    private bool IsAdmin()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user == null || user.Identity?.IsAuthenticated != true) return false;

        return user.Claims
            .Where(c => c.Type == ClaimTypes.Role || c.Type == "role")
            .Select(c => c.Value)
            .Any(value => string.Equals(value, "admin", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "SuperAdmin", StringComparison.OrdinalIgnoreCase));
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

public class ListConceptsResult { public bool Ok { get; set; } public string? Message { get; set; } public List<ConceptSummary> Concepts { get; set; } = new(); }
public class ConceptSummary { public string? Irdi { get; set; } public string? AgencyId { get; set; } public string? Name { get; set; } public string? Version { get; set; } public string? Label { get; set; } public ApprovalState ApprovalState { get; set; } public DateTime CreatedAt { get; set; } }
public class GetConceptResult { public bool Ok { get; set; } public string? Message { get; set; } public ConceptSummary? Concept { get; set; } }

public class ListRepresentationsResult { public bool Ok { get; set; } public string? Message { get; set; } public List<RepresentationSummary> Representations { get; set; } = new(); }
public class RepresentationSummary { public string? Irdi { get; set; } public string? AgencyId { get; set; } public string? Name { get; set; } public string? Version { get; set; } public string? Type { get; set; } public string? JsonSchema { get; set; } public string? ShaclTemplateIrdi { get; set; } public ApprovalState ApprovalState { get; set; } public DateTime CreatedAt { get; set; } }
public class GetRepresentationResult { public bool Ok { get; set; } public string? Message { get; set; } public RepresentationSummary? Representation { get; set; } }

public class GetVariablePublishabilityResult { public bool Ok { get; set; } public string? Message { get; set; } public string? Irdi { get; set; } public string? Name { get; set; } public ApprovalState VariableApprovalState { get; set; } public ApprovalState ConceptApprovalState { get; set; } public ApprovalState RepresentationApprovalState { get; set; } public bool IsPublishable { get; set; } }

public class ListVariablesResult { public bool Ok { get; set; } public string? Message { get; set; } public List<VariableSummary> Variables { get; set; } = new(); }
public class VariableSummary { public string? Irdi { get; set; } public string? AgencyId { get; set; } public string? Name { get; set; } public string? Version { get; set; } public string? ConceptIrdi { get; set; } public string? RepresentationIrdi { get; set; } public ApprovalState ApprovalState { get; set; } public bool IsPublishable { get; set; } }
public class GetVariableResult { public bool Ok { get; set; } public string? Message { get; set; } public VariableSummary? Variable { get; set; } }

public class RequestConceptResult { public bool Success { get; set; } public string? Irdi { get; set; } public ApprovalState ApprovalState { get; set; } public string? Message { get; set; } }

public class UpdateConceptRequestResult { public bool Success { get; set; } public string? Irdi { get; set; } public ApprovalState ApprovalState { get; set; } public string? Message { get; set; } }

public class RequestRepresentationResult { public bool Success { get; set; } public string? Irdi { get; set; } public ApprovalState ApprovalState { get; set; } public string? Message { get; set; } }

public class UpdateRepresentationRequestResult { public bool Success { get; set; } public string? Irdi { get; set; } public ApprovalState ApprovalState { get; set; } public string? Message { get; set; } }

public class RequestVariableResult { public bool Success { get; set; } public string? Irdi { get; set; } public ApprovalState ApprovalState { get; set; } public string? Message { get; set; } }

public class UpdateVariableRequestResult { public bool Success { get; set; } public string? Irdi { get; set; } public ApprovalState ApprovalState { get; set; } public string? Message { get; set; } }

public class ApproveConceptResult { public bool Success { get; set; } public string? Irdi { get; set; } public ApprovalState ApprovalState { get; set; } public string? Message { get; set; } }

public class DeprecateConceptResult { public bool Success { get; set; } public string? Irdi { get; set; } public ApprovalState ApprovalState { get; set; } public string? Message { get; set; } }

public class ApproveRepresentationResult { public bool Success { get; set; } public string? Irdi { get; set; } public ApprovalState ApprovalState { get; set; } public string? Message { get; set; } }

public class DeprecateRepresentationResult { public bool Success { get; set; } public string? Irdi { get; set; } public ApprovalState ApprovalState { get; set; } public string? Message { get; set; } }

public class ApproveVariableResult { public bool Success { get; set; } public string? Irdi { get; set; } public ApprovalState ApprovalState { get; set; } public string? Message { get; set; } }

public class DeprecateVariableResult { public bool Success { get; set; } public string? Irdi { get; set; } public ApprovalState ApprovalState { get; set; } public string? Message { get; set; } }

public class RequestAgencyResult { public bool Success { get; set; } public string? AgencyId { get; set; } public ApprovalState ApprovalState { get; set; } public string? Message { get; set; } }
