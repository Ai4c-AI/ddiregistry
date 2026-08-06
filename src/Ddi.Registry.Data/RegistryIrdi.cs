using System;

namespace Ddi.Registry.Data
{
    public static class RegistryIrdi
    {
        public static string BuildConceptIrdi(string agencyId, string name, string version)
        {
            return BuildIrdi(agencyId, "concept", name, version);
        }

        public static string BuildRepresentationIrdi(string agencyId, string name, string version)
        {
            return BuildIrdi(agencyId, "representation", name, version);
        }

        public static string BuildVariableIrdi(string agencyId, string name, string version)
        {
            return BuildIrdi(agencyId, "variable", name, version);
        }

        public static bool TryParse(string irdi, out RegistryIrdiParts parts)
        {
            parts = null;

            if (string.IsNullOrWhiteSpace(irdi))
            {
                return false;
            }

            var split = irdi.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (split.Length != 6)
            {
                return false;
            }

            if (!string.Equals(split[0], "urn", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(split[1], "irdi", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            parts = new RegistryIrdiParts(
                split[2].ToLowerInvariant(),
                split[3].ToLowerInvariant(),
                split[4],
                split[5]);

            return true;
        }

        private static string BuildIrdi(string agencyId, string kind, string name, string version)
        {
            return $"urn:irdi:{agencyId.ToLowerInvariant()}:{kind}:{name}:{version}";
        }
    }

    public record RegistryIrdiParts(string AgencyId, string Kind, string Name, string Version);
}