using System;
using System.IO;
using NISOCountries.Core;
using NISOCountries.Ripe;

namespace Ddi.Registry.Data
{
    public static class AgencyIdValidator
    {
        public static (bool Ok, string Error) Validate(string agencyId, string label)
        {
            if (string.IsNullOrWhiteSpace(agencyId)) return (false, "An agency name is required.");
            if (string.IsNullOrWhiteSpace(label)) return (false, "An agency label is required.");
            if (agencyId.Length > 50) return (false, "The agency name must be 50 characters or fewer.");
            if (!System.Text.RegularExpressions.Regex.IsMatch(agencyId, @"^[a-zA-Z]{2,3}\.[a-zA-Z0-9](-?[a-zA-Z0-9]+)*$"))
                return (false, "The agency name should be in the form [country code] dot [name], e.g. us.agencyname.");

            int index = agencyId.IndexOf('.');
            string code = agencyId.Substring(0, index);

            if (index == 2)
            {
                if (code.ToLowerInvariant() == "uk") return (true, null);
                try
                {
                    var isoFile = Path.Combine(AppContext.BaseDirectory, "iso3166-countrycodes.txt");
                    if (!File.Exists(isoFile)) return (false, "ISO country-code validation data is unavailable.");
                    var isoCountries = new RipeISOCountryReader().Parse(isoFile);
                    var isoLookup = new ISOCountryLookup<RipeCountry>(isoCountries);
                    if (isoLookup.TryGetByAlpha2(code, out _)) return (true, null);
                }
                catch (Exception) { return (false, "ISO country-code validation data is unavailable."); }
                return (false, $"{code} is not a valid country code. Use a 2-char ISO 3166 code or 'uk'.");
            }
            else if (index == 3 && code.ToLowerInvariant() == "int")
            {
                return (true, null);
            }
            return (false, "The agency id must start with a 2 character ISO 3166 country code or 'int', e.g. us.agencyname.");
        }
    }
}
