using System;
using System.Collections.Generic;
using System.Globalization;
using ProjectM;
using RaidForge.Config;
using RaidForge.Data;
using Stunlock.Core;

namespace RaidForge.Utils
{
    public static class PrefabGuidResolver
    {
        public static bool TryResolve(string prefabNameOrHash, out PrefabGUID prefabGuid)
        {
            return TryResolve(prefabNameOrHash, out prefabGuid, out _);
        }

        public static bool TryResolve(string prefabNameOrHash, out PrefabGUID prefabGuid, out string resolvedName)
        {
            prefabGuid = default;
            resolvedName = null;

            if (string.IsNullOrWhiteSpace(prefabNameOrHash))
            {
                return false;
            }

            string value = prefabNameOrHash.Trim();

            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int hash))
            {
                prefabGuid = new PrefabGUID(hash);

                if (!TryGetPrefabName(prefabGuid, out resolvedName))
                {
                    prefabGuid = default;
                    return false;
                }

                return true;
            }

            if (!TryGetPrefabGuidByName(value, out prefabGuid))
            {
                return false;
            }

            resolvedName = TryGetPrefabName(prefabGuid, out string canonicalName)
                ? canonicalName
                : value;
            return true;
        }

        public static bool TryResolveMapIcon(string prefabNameOrHash, out PrefabGUID prefabGuid)
        {
            return TryResolveMapIcon(prefabNameOrHash, out prefabGuid, out _);
        }

        public static bool TryResolveMapIcon(string prefabNameOrHash, out PrefabGUID prefabGuid, out string resolvedName)
        {
            prefabGuid = default;
            resolvedName = null;

            if (MapIconPrefabCatalog.TryResolve(prefabNameOrHash, out prefabGuid, out resolvedName))
            {
                return true;
            }

            if (!TryResolve(prefabNameOrHash, out prefabGuid, out resolvedName) ||
                string.IsNullOrWhiteSpace(resolvedName) ||
                !resolvedName.StartsWith("MapIcon_", StringComparison.OrdinalIgnoreCase) ||
                prefabGuid.GuidHash == MapIconPrefabCatalog.MapIconProxyPrefab.GuidHash)
            {
                prefabGuid = default;
                resolvedName = null;
                return false;
            }

            return true;
        }

        public static PrefabGUID ResolveMapIconOrDefault(
            string prefabNameOrHash,
            PrefabRef fallbackPrefab,
            string context)
        {
            return ResolveOrDefault(
                prefabNameOrHash,
                fallbackPrefab.Name,
                fallbackPrefab.Guid,
                context,
                TryResolveMapIcon);
        }

        public static bool TryGetPrefabGuidByName(string prefabName, out PrefabGUID prefabGuid)
        {
            prefabGuid = default;

            if (string.IsNullOrWhiteSpace(prefabName))
            {
                return false;
            }

            string trimmedName = prefabName.Trim();

            if (TryGetPrefabLookup(out PrefabLookupMap prefabLookup) &&
                prefabLookup.TryGetPrefabGuidWithName(trimmedName, out prefabGuid, ignoreCase: true))
            {
                return true;
            }

            if (TryGetKnownPrefabByName(trimmedName, out PrefabRef knownPrefab))
            {
                prefabGuid = knownPrefab.Guid;
                return true;
            }

            return false;
        }

        public static bool TryGetPrefabName(PrefabGUID prefabGuid, out string prefabName)
        {
            prefabName = null;

            if (TryGetPrefabLookup(out PrefabLookupMap prefabLookup) &&
                prefabLookup.TryGetName(prefabGuid, out prefabName) &&
                !string.IsNullOrWhiteSpace(prefabName))
            {
                return true;
            }

            if (TryGetKnownPrefabByHash(prefabGuid.GuidHash, out PrefabRef knownPrefab))
            {
                prefabName = knownPrefab.Name;
                return true;
            }

            return false;
        }

        public static int ValidateKnownPrefabs()
        {
            try
            {
                return ValidateKnownPrefabsCore();
            }
            catch (Exception ex)
            {
                LoggingHelper.Warning("[PrefabCatalog] Runtime name/hash validation failed unexpectedly.", ex);
                return -1;
            }
        }

        private static int ValidateKnownPrefabsCore()
        {
            if (!TryGetPrefabLookup(out PrefabLookupMap prefabLookup))
            {
                LoggingHelper.Warning("[PrefabCatalog] V Rising PrefabLookupMap is unavailable; runtime name/hash validation was skipped.");
                return -1;
            }

            var seenHashes = new HashSet<int>();
            int validated = 0;
            int mismatches = 0;

            foreach (PrefabRef prefab in PrefabData.All)
            {
                ValidateKnownPrefab(prefabLookup, prefab, seenHashes, ref validated, ref mismatches);
            }

            foreach (PrefabRef prefab in MapIconPrefabCatalog.KnownPrefabs)
            {
                ValidateKnownPrefab(prefabLookup, prefab, seenHashes, ref validated, ref mismatches);
            }

            if (mismatches == 0)
            {
                LoggingHelper.Info($"[PrefabCatalog] Validated {validated} unique prefab name/hash pair(s) against V Rising.");
            }
            else
            {
                LoggingHelper.Error($"[PrefabCatalog] Found {mismatches} invalid prefab definition(s) while validating {validated} unique pair(s).");
            }

            return mismatches;
        }

        private static PrefabGUID ResolveOrDefault(
            string prefabNameOrHash,
            string defaultPrefabName,
            PrefabGUID fallbackPrefabGuid,
            string context,
            TryResolveDelegate tryResolve)
        {
            if (tryResolve(prefabNameOrHash, out PrefabGUID prefabGuid, out _))
            {
                return prefabGuid;
            }

            if (tryResolve(defaultPrefabName, out prefabGuid, out _))
            {
                LogInvalidConfig(prefabNameOrHash, defaultPrefabName, context);
                return prefabGuid;
            }

            LogInvalidConfig(prefabNameOrHash, $"{defaultPrefabName} ({fallbackPrefabGuid.GuidHash})", context);
            return fallbackPrefabGuid;
        }

        private static void ValidateKnownPrefab(
            PrefabLookupMap prefabLookup,
            PrefabRef prefab,
            HashSet<int> seenHashes,
            ref int validated,
            ref int mismatches)
        {
            if (!seenHashes.Add(prefab.GuidHash))
            {
                return;
            }

            validated++;

            bool hashResolvesToName =
                prefabLookup.TryGetName(prefab.Guid, out string resolvedName) &&
                string.Equals(resolvedName, prefab.Name, StringComparison.Ordinal);
            bool nameResolvesToHash =
                prefabLookup.TryGetPrefabGuidWithName(prefab.Name, out PrefabGUID resolvedGuid) &&
                resolvedGuid.GuidHash == prefab.GuidHash;

            if (hashResolvesToName && nameResolvesToHash)
            {
                return;
            }

            mismatches++;
            LoggingHelper.Error(
                $"[PrefabCatalog] Invalid definition {prefab}. " +
                $"Hash resolved to '{resolvedName ?? "<missing>"}'; name resolved to {resolvedGuid.GuidHash}.");
        }

        private static bool TryGetPrefabLookup(out PrefabLookupMap prefabLookup)
        {
            prefabLookup = default;

            if (!TryGetPrefabCollectionSystem(out PrefabCollectionSystem prefabCollection))
            {
                return false;
            }

            prefabLookup = prefabCollection._PrefabLookupMap;
            return prefabLookup.IsCreated;
        }

        private static bool TryGetPrefabCollectionSystem(out PrefabCollectionSystem prefabCollection)
        {
            prefabCollection = null;

            if (VWorld.Server == null || !VWorld.Server.IsCreated)
            {
                return false;
            }

            prefabCollection = VWorld.Server.GetExistingSystemManaged<PrefabCollectionSystem>();
            return prefabCollection != null;
        }

        private static bool TryGetKnownPrefabByName(string name, out PrefabRef prefab)
        {
            if (PrefabData.TryGetByName(name, out prefab))
            {
                return true;
            }

            foreach (PrefabRef candidate in MapIconPrefabCatalog.KnownPrefabs)
            {
                if (string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    prefab = candidate;
                    return true;
                }
            }

            prefab = default;
            return false;
        }

        private static bool TryGetKnownPrefabByHash(int guidHash, out PrefabRef prefab)
        {
            return PrefabData.TryGetByHash(guidHash, out prefab) ||
                MapIconPrefabCatalog.TryGetByHash(guidHash, out prefab);
        }

        private static void LogInvalidConfig(string configuredValue, string fallbackValue, string context)
        {
            if (TroubleshootingConfig.EnableVerboseLogging?.Value != true)
            {
                return;
            }

            string value = string.IsNullOrWhiteSpace(configuredValue) ? "<empty>" : configuredValue.Trim();
            LoggingHelper.Warning($"[PrefabGuidResolver] Could not resolve '{value}' for {context}. Using {fallbackValue}.");
        }

        private delegate bool TryResolveDelegate(
            string prefabNameOrHash,
            out PrefabGUID prefabGuid,
            out string resolvedName);
    }
}
