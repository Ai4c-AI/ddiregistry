using System;
using System.IO;
using NISOCountries.Core;
using NISOCountries.Ripe;

namespace Ddi.Registry.Data
{
    public static class AgencyIdValidator
    {
        public static AgencyIdValidationResult Validate(string agencyId, string label)
        {
            if (string.IsNullOrWhiteSpace(agencyId)) return new(false, "AgencyNameRequired", "An agency name is required.");
            if (string.IsNullOrWhiteSpace(label)) return new(false, "AgencyLabelRequired", "An agency label is required.");
            if (agencyId.Length > 50) return new(false, "AgencyNameTooLong", "The agency name must be 50 characters or fewer.");
            if (!System.Text.RegularExpressions.Regex.IsMatch(agencyId, @"^[a-zA-Z]{2,3}\.[a-zA-Z0-9](-?[a-zA-Z0-9]+)*$"))
                return new(false, "AgencyNamePattern", "The agency name should be in the form [country code] dot [name], e.g. us.agencyname.");

            int index = agencyId.IndexOf('.');
            string code = agencyId.Substring(0, index);

            if (index == 2)
            {
                if (code.ToLowerInvariant() == "uk") return new(true, string.Empty, null);
                try
                {
                    var isoFile = Path.Combine(AppContext.BaseDirectory, "iso3166-countrycodes.txt");
                    if (!File.Exists(isoFile)) return new(false, "CountryCodeDataUnavailable", "ISO country-code validation data is unavailable.");
                    var isoCountries = new RipeISOCountryReader().Parse(isoFile);
                    var isoLookup = new ISOCountryLookup<RipeCountry>(isoCountries);
                    if (isoLookup.TryGetByAlpha2(code, out _)) return new(true, string.Empty, null);
                }
                catch (Exception) { return new(false, "CountryCodeDataUnavailable", "ISO country-code validation data is unavailable."); }
                return new(false, "CountryCodeInvalid", $"{code} is not a valid country code. Use a 2-char ISO 3166 code or 'uk'.");
            }
            else if (index == 3 && code.ToLowerInvariant() == "int")
            {
                return new(true, string.Empty, null);
            }
            return new(false, "AgencyPrefixInvalid", "The agency id must start with a 2 character ISO 3166 country code or 'int', e.g. us.agencyname.");
        }
    }

    /// <summary>
    /// Result of <see cref="AgencyIdValidator.Validate"/>. <see cref="ErrorCode"/> is a
    /// stable machine-readable key used by the web app to look up a localized message;
    /// <see cref="Error"/> is the English message retained for the MCP tool surface.
    /// </summary>
    public sealed record AgencyIdValidationResult(bool Ok, string ErrorCode, string Error);
}
