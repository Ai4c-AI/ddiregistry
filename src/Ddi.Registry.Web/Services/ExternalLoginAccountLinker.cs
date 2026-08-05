using System.Threading.Tasks;
using Ddi.Registry.Data;
using Microsoft.AspNetCore.Identity;

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

        public ExternalLoginAccountLinker(UserManager<ApplicationUser> userManager)
        {
            _userManager = userManager;
        }

        public async Task<ExternalLoginLinkResult> LinkAsync(ExternalLoginInfo info, string email)
        {
            var user = await _userManager.FindByEmailAsync(email);
            if (user == null)
            {
                return ExternalLoginLinkResult.MissingUser;
            }

            var result = await _userManager.AddLoginAsync(user, info);
            return result.Succeeded ? ExternalLoginLinkResult.Linked : ExternalLoginLinkResult.Failed;
        }
    }
}