using System.Threading.Tasks;
using Ddi.Registry.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;

namespace Ddi.Registry.Web.Services
{
    public enum ExternalLoginLinkResult
    {
        Linked,
        MissingUser,
        Failed
    }

    public class ExternalLoginAccountLinker
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly IConfiguration _configuration;

        public ExternalLoginAccountLinker(UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager, IConfiguration configuration)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _configuration = configuration;
        }

        public async Task<ExternalLoginLinkResult> LinkAsync(ExternalLoginInfo info, string email)
        {
            var user = await _userManager.FindByEmailAsync(email);
            if (user == null)
            {
                user = new ApplicationUser
                {
                    UserName = email,
                    Email = email,
                    EmailConfirmed = true
                };

                var createResult = await _userManager.CreateAsync(user);
                if (!createResult.Succeeded)
                {
                    return ExternalLoginLinkResult.Failed;
                }
            }

            var result = await _userManager.AddLoginAsync(user, info);
            if (!result.Succeeded)
            {
                return ExternalLoginLinkResult.Failed;
            }

            var adminEmail = _configuration["DefaultUser:Email"] ?? "admin@localhost";
            if (string.Equals(email, adminEmail, System.StringComparison.OrdinalIgnoreCase))
            {
                var roleName = _configuration["DefaultUser:Role"] ?? "admin";

                if (!await _roleManager.RoleExistsAsync(roleName))
                {
                    await _roleManager.CreateAsync(new IdentityRole(roleName));
                }

                if (!await _userManager.IsInRoleAsync(user, roleName))
                {
                    await _userManager.AddToRoleAsync(user, roleName);
                }
            }

            return ExternalLoginLinkResult.Linked;
        }
    }
}