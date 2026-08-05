using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ddi.Registry.Data;
using Ddi.Registry.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using AspNetCoreRateLimit;

namespace Ddi.Registry.Web
{
    public class Startup
    {
        public Startup(IConfiguration configuration, IWebHostEnvironment environment)
        {
            Configuration = configuration;
            Environment = environment;
        }

        public IConfiguration Configuration { get; }
        public IWebHostEnvironment Environment { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddOptions();
            services.AddMemoryCache();
            services.Configure<IpRateLimitOptions>(Configuration.GetSection("IpRateLimiting"));
            services.AddInMemoryRateLimiting();

            services.Configure<CookiePolicyOptions>(options =>
            {
                // This lambda determines whether user consent for non-essential cookies is needed for a given request.
                options.CheckConsentNeeded = context => true;
                options.MinimumSameSitePolicy = SameSiteMode.None;
            });

            services.AddDatabaseDeveloperPageExceptionFilter();

            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseNpgsql(
                    Configuration.GetConnectionString("DefaultConnection")));

            services.AddIdentity<ApplicationUser, IdentityRole>(config => 
                {
                    config.SignIn.RequireConfirmedEmail = false;
                })
                //.AddRoles<IdentityRole>()
                //.AddDefaultUI(UIFramework.Bootstrap4)
                .AddEntityFrameworkStores<ApplicationDbContext>()
                .AddDefaultTokenProviders(); 

            var keycloakAuthority = Configuration["Authentication:Keycloak:Authority"];
            var keycloakClientId = Configuration["Authentication:Keycloak:ClientId"];
            var keycloakClientSecret = Configuration["Authentication:Keycloak:ClientSecret"];
            if (!string.IsNullOrWhiteSpace(keycloakAuthority) &&
                !string.IsNullOrWhiteSpace(keycloakClientId) &&
                !string.IsNullOrWhiteSpace(keycloakClientSecret))
            {
                services.AddAuthentication()
                    .AddOpenIdConnect("Keycloak", options =>
                    {
                        options.SignInScheme = IdentityConstants.ExternalScheme;
                        options.Authority = keycloakAuthority;
                        options.ClientId = keycloakClientId;
                        options.ClientSecret = keycloakClientSecret;
                        options.RequireHttpsMetadata = !Environment.IsDevelopment();
                        options.CallbackPath = "/signin-oidc";
                        options.ResponseType = OpenIdConnectResponseType.Code;
                        options.UsePkce = true;
                        options.SaveTokens = false;
                        options.GetClaimsFromUserInfoEndpoint = true;
                        options.Scope.Clear();
                        options.Scope.Add("openid");
                        options.Scope.Add("profile");
                        options.Scope.Add("email");
                        options.ClaimActions.MapJsonKey(ClaimTypes.Email, "email");
                    });
            }

            services.AddScoped<ExternalLoginAccountLinker>();

            var emailconfig = Configuration.GetSection("EmailConfiguration").Get<EmailConfiguration>();
            services.AddTransient<IEmailSender, EmailSender>(i => new EmailSender(emailconfig));

            services.AddMvc();

            services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();

            /*
            services.AddAuthorization(options =>
            {
                options.AddPolicy("EditPolicy", policy =>
                    policy.Requirements.Add(new AgencyRequirement()));
            });
            services.AddSingleton<IAuthorizationHandler, AgencyAuthorizationHandler>();*/
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            app.UseIpRateLimiting();

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseDeveloperExceptionPage();
                app.UseMigrationsEndPoint();
            }
            else
            {
                app.UseStatusCodePages();
                app.UseExceptionHandler("/Home/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            UpdateDatabase(app, env).Wait();
            CreateRoles(app).Wait();

            app.UseHttpsRedirection();
            app.UseStaticFiles();
            app.UseCookiePolicy();            

            app.UseRouting();

            app.UseAuthentication();
            app.UseAuthorization();

            app.UseEndpoints(endpoints => 
            { 
                endpoints.MapDefaultControllerRoute();
                endpoints.MapRazorPages();
            }); 
        }

        private static async Task UpdateDatabase(IApplicationBuilder app, IWebHostEnvironment env)
        {
            using (var serviceScope = app.ApplicationServices
                .GetRequiredService<IServiceScopeFactory>()
                .CreateScope())
            {
                using (var context = serviceScope.ServiceProvider.GetRequiredService<ApplicationDbContext>())
                {
                    if (!context.Database.IsRelational())
                    {
                        await context.Database.EnsureCreatedAsync();
                    }
                    else
                    {
                        await context.Database.MigrateAsync();
                    }
                }
            }
        }

        public static async Task CreateRoles(IApplicationBuilder app)
        {
            using (var serviceScope = app.ApplicationServices
                .GetRequiredService<IServiceScopeFactory>()
                .CreateScope())
            {
                using (var roleManager = serviceScope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>())
                {
                    string[] roleNames = { "admin", "SuperAdmin" };

                    foreach (var roleName in roleNames)
                    {
                        var exist = await roleManager.RoleExistsAsync(roleName);
                        if (!exist)
                        {
                            var result = await roleManager.CreateAsync(new IdentityRole(roleName));
                        }
                    }
                }
            }
        }

    }
}
