using System;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;

namespace Ddi.Registry.Web.Controllers
{
    public class LanguageController : Controller
    {
        private static readonly string[] SupportedCultures = { "zh-CN", "en" };

        [HttpPost]
        public IActionResult SetLanguage(string culture, string returnUrl)
        {
            var resolved = culture != null &&
                SupportedCultures.Any(c => string.Equals(c, culture, StringComparison.OrdinalIgnoreCase))
                ? culture
                : "zh-CN";

            Response.Cookies.Append(
                CookieRequestCultureProvider.DefaultCookieName,
                CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(resolved)),
                new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true });

            if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return LocalRedirect(returnUrl);
            }

            return RedirectToAction("Index", "Home");
        }
    }
}
