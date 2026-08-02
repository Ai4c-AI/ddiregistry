using System;
using Ddi.Registry.Data;
using Ddi.Registry.Mcp.Tools;            // WithTools<RegistryTools>() 所需类型可见性
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// AWS ALB 转发头：仅信任部署配置中的反向代理，不可清空 KnownNetworks/KnownProxies。
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    var trustedProxy = builder.Configuration["MCP:ReverseProxy:TrustedProxy"];
    System.Net.IPAddress? proxyAddress = null;
    if (!System.Net.IPAddress.TryParse(trustedProxy, out var parsedProxy))
    {
        if (!builder.Environment.IsDevelopment())
            throw new InvalidOperationException("MCP:ReverseProxy:TrustedProxy must be a trusted ALB/reverse-proxy IP address outside Development.");
    }
    else
    {
        proxyAddress = parsedProxy;
    }
    if (proxyAddress is not null)
        options.KnownProxies.Add(proxyAddress);
});

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddHttpContextAccessor();

// 服务器注册（Streamable HTTP；legacy SSE 默认关闭）
builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.Stateless = true)
    .WithTools<RegistryTools>();

// 认证接线：Bearer 认证；McpAuth 为 401 challenge 提供 protected-resource metadata
var oidcAuthority = builder.Configuration["MCP:Oidc:Authority"];
var oidcAudience = builder.Configuration["MCP:Oidc:Audience"];
var oidcScopes = (builder.Configuration["MCP:Oidc:Scopes"] ?? string.Empty)
    .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
var authenticationEnabled = !string.IsNullOrWhiteSpace(oidcAuthority);

if (!builder.Environment.IsDevelopment() &&
    (string.IsNullOrWhiteSpace(oidcAuthority) || string.IsNullOrWhiteSpace(oidcAudience)))
    throw new InvalidOperationException("MCP:Oidc:Authority and MCP:Oidc:Audience are required outside Development.");

if (authenticationEnabled)
{
    builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = "Bearer";
        options.DefaultChallengeScheme = "McpAuth";
    })
    .AddJwtBearer("Bearer", options =>
    {
        options.Authority = oidcAuthority;
        options.Audience = oidcAudience;
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
    })
    .AddMcp(options =>
    {
        // ResourceMetadata describes this MCP protected resource. Its
        // AuthorizationServers collection contains the external IdP authority.
        options.ResourceMetadata = new()
        {
            AuthorizationServers = [oidcAuthority!],
            ScopesSupported = [.. oidcScopes]
        };
        // Leave ResourceMetadataUri unset: SDK serves the default
        // /.well-known/oauth-protected-resource/mcp resource metadata.
    });

    builder.Services.AddAuthorization();
}

var app = builder.Build();

app.UseForwardedHeaders();

if (authenticationEnabled)
{
    app.UseAuthentication();
    app.UseAuthorization();
}

var mcpEndpoint = app.MapMcp("/mcp");
if (authenticationEnabled)
    mcpEndpoint.RequireAuthorization(new AuthorizeAttribute());

app.Run();

public partial class Program { }   // 供 WebApplicationFactory 使用
