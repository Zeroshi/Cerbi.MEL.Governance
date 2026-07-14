using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace Cerbi
{
    /// <summary>
    /// Parses fieldAliases from a governance config JSON and expands aliased field values
    /// into their canonical names so the RuntimeGovernanceValidator can match them.
    /// </summary>
    internal sealed class FieldAliasExpander
    {
        private readonly Dictionary<string, List<string>> _aliases;

        /// <summary>
        /// Builds a reverse map: alias → canonical field name, for O(1) expansion.
        /// </summary>
        private readonly Dictionary<string, string> _reverseMap;

        public FieldAliasExpander(Dictionary<string, List<string>> aliases)
        {
            _aliases = aliases ?? new Dictionary<string, List<string>>();
            _reverseMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in _aliases)
            {
                foreach (var alias in kvp.Value)
                {
                    // First canonical field wins if multiple map the same alias
                    if (!_reverseMap.ContainsKey(alias))
                        _reverseMap[alias] = kvp.Key;
                }
            }
        }

        public bool HasAliases => _reverseMap.Count > 0;

        /// <summary>
        /// Expands aliased fields in the dictionary: if a key matches an alias but the canonical
        /// key is missing, copies the value to the canonical key so the validator sees it.
        /// </summary>
        public void ExpandAliases(Dictionary<string, object> dict)
        {
            if (_reverseMap.Count == 0) return;

            // Snapshot keys to avoid modifying collection during iteration
            var keys = new List<string>(dict.Keys);
            foreach (var key in keys)
            {
                if (_reverseMap.TryGetValue(key, out var canonical) &&
                    !string.Equals(key, canonical, StringComparison.OrdinalIgnoreCase) &&
                    !dict.ContainsKey(canonical))
                {
                    dict[canonical] = dict[key];
                }
            }
        }

        /// <summary>
        /// Parses fieldAliases from a governance JSON config file.
        /// Supports both canonical format (root-level fieldAliases) and wrapper format
        /// (LoggingProfiles → selected profile → fieldAliases).
        /// </summary>
        public static FieldAliasExpander LoadFromConfig(string configPath, string profileName)
        {
            var aliases = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(configPath))
                    return new FieldAliasExpander(aliases);

                using var fs = File.OpenRead(configPath);
                using var doc = JsonDocument.Parse(fs);
                var root = doc.RootElement;

                if (TryGetPropertyCaseInsensitive(root, "LoggingProfiles", out var profiles))
                {
                    var profile = SelectWrappedProfile(profiles, profileName);
                    TryParseAliasesFromElement(profile, aliases);
                    return new FieldAliasExpander(aliases);
                }

                // No wrapper means Runtime 2.0.43 canonical root profile semantics.
                TryParseAliasesFromElement(root, aliases);
            }
            catch (InvalidDataException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CerbiGovernance] Failed to parse fieldAliases: {ex.Message}");
            }

            return new FieldAliasExpander(aliases);
        }

        private static JsonElement SelectWrappedProfile(JsonElement profiles, string profileName)
        {
            if (profiles.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("LoggingProfiles must be a JSON object.");

            if (string.IsNullOrWhiteSpace(profileName))
                throw new InvalidDataException("A profile name is required when LoggingProfiles is present.");

            JsonElement? caseInsensitiveMatch = null;
            var caseInsensitiveMatchCount = 0;

            foreach (var profile in profiles.EnumerateObject())
            {
                if (string.Equals(profile.Name, profileName, StringComparison.Ordinal))
                    return profile.Value;

                if (string.Equals(profile.Name, profileName, StringComparison.OrdinalIgnoreCase))
                {
                    caseInsensitiveMatch = profile.Value;
                    caseInsensitiveMatchCount++;
                }
            }

            return caseInsensitiveMatchCount switch
            {
                1 => caseInsensitiveMatch!.Value,
                > 1 => throw new InvalidDataException($"Multiple LoggingProfiles match '{profileName}' case-insensitively."),
                _ => throw new InvalidDataException($"LoggingProfiles does not contain requested profile '{profileName}'.")
            };
        }

        private static bool TryGetPropertyCaseInsensitive(JsonElement element, string propertyName, out JsonElement value)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                {
                    if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                    {
                        value = property.Value;
                        return true;
                    }
                }
            }

            value = default;
            return false;
        }

        private static bool TryParseAliasesFromElement(JsonElement element, Dictionary<string, List<string>> aliases)
        {
            if (element.ValueKind != JsonValueKind.Object)
                return false;

            // Try both casings: "fieldAliases" and "FieldAliases"
            JsonElement aliasEl;
            if (!element.TryGetProperty("fieldAliases", out aliasEl) || aliasEl.ValueKind != JsonValueKind.Object)
            {
                if (!element.TryGetProperty("FieldAliases", out aliasEl) || aliasEl.ValueKind != JsonValueKind.Object)
                    return false;
            }

            foreach (var prop in aliasEl.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Array)
                    continue;

                var list = new List<string>();
                foreach (var item in prop.Value.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        var val = item.GetString();
                        if (!string.IsNullOrWhiteSpace(val))
                            list.Add(val!);
                    }
                }
                if (list.Count > 0)
                    aliases[prop.Name] = list;
            }

            return aliases.Count > 0;
        }
    }
}
