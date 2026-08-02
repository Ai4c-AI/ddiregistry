using Ddi.Registry.Data;
using Microsoft.AspNetCore.Http;
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
}
