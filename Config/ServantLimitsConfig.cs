using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Configuration;

namespace RaidForge.Config
{
    public static class ServantLimitsConfig
    {
        private const string SectionGeneral = "00 - General";
        private const string LegacySectionGeneral = "General";
        private const string SectionCharacterLimits = "Character Limits";

        private static readonly Dictionary<string, ConfigEntry<string>> _characterLimitEntries =
            new(StringComparer.OrdinalIgnoreCase);

        public static ConfigFile ConfigFileInstance { get; private set; }
        public static ConfigEntry<bool> EnableServantLimits { get; private set; }
        public static ConfigEntry<bool> EnableDetailedLogging { get; private set; }
        public static IReadOnlyDictionary<string, ConfigEntry<string>> CharacterLimitEntries =>
            _characterLimitEntries;

        public static void Initialize(ConfigFile configFile)
        {
            ConfigFileInstance = configFile;
            _characterLimitEntries.Clear();

            bool hasCurrentGeneralSection =
                ContainsSection(configFile.ConfigFilePath, SectionGeneral);
            bool hasLegacyGeneralSection =
                ContainsSection(configFile.ConfigFilePath, LegacySectionGeneral);
            bool saveOnConfigSet = configFile.SaveOnConfigSet;

            try
            {
                configFile.SaveOnConfigSet = false;

                EnableServantLimits = configFile.Bind(
                    SectionGeneral,
                    "EnableServantLimits",
                    false,
                    "Enforces nonblank entries under Character Limits. Blank character entries are unlimited.");

                EnableDetailedLogging = configFile.Bind(
                    SectionGeneral,
                    "EnableDetailedLogging",
                    false,
                    "Logs servant-limit mappings, matching coffin states, castle-heart connections, reservations, and allow/block decisions. Enable only while diagnosing.");

                MigrateLegacyGeneralSection(
                    configFile,
                    hasCurrentGeneralSection,
                    hasLegacyGeneralSection);
            }
            finally
            {
                configFile.SaveOnConfigSet = saveOnConfigSet;
            }

            configFile.Save();
        }

        public static void RegisterCharacterLimits(
            IReadOnlyList<KeyValuePair<string, string>> convertibleCharacters)
        {
            if (ConfigFileInstance == null || convertibleCharacters == null)
            {
                return;
            }

            bool saveOnConfigSet = ConfigFileInstance.SaveOnConfigSet;

            try
            {
                ConfigFileInstance.SaveOnConfigSet = false;

                foreach (KeyValuePair<string, string> character in convertibleCharacters)
                {
                    if (_characterLimitEntries.ContainsKey(character.Key))
                    {
                        continue;
                    }

                    ConfigEntry<string> entry = ConfigFileInstance.Bind(
                        SectionCharacterLimits,
                        character.Key,
                        string.Empty,
                        $"Maximum per castle after conversion to {character.Value}. " +
                        "Leave blank for unlimited; use 0 to block this character completely.");

                    _characterLimitEntries.Add(character.Key, entry);
                }
            }
            finally
            {
                ConfigFileInstance.SaveOnConfigSet = saveOnConfigSet;
            }

            ConfigFileInstance.Save();
        }

        private static void MigrateLegacyGeneralSection(
            ConfigFile configFile,
            bool hasCurrentGeneralSection,
            bool hasLegacyGeneralSection)
        {
            if (!hasLegacyGeneralSection)
            {
                return;
            }

            ConfigEntry<bool> legacyEnable = configFile.Bind(
                LegacySectionGeneral,
                "EnableServantLimits",
                false,
                string.Empty);
            ConfigEntry<bool> legacyLogging = configFile.Bind(
                LegacySectionGeneral,
                "EnableDetailedLogging",
                false,
                string.Empty);

            if (!hasCurrentGeneralSection)
            {
                EnableServantLimits.Value = legacyEnable.Value;
                EnableDetailedLogging.Value = legacyLogging.Value;
            }

            configFile.Remove(legacyEnable.Definition);
            configFile.Remove(legacyLogging.Definition);
        }

        private static bool ContainsSection(string configFilePath, string section)
        {
            if (string.IsNullOrWhiteSpace(configFilePath) ||
                string.IsNullOrWhiteSpace(section) ||
                !File.Exists(configFilePath))
            {
                return false;
            }

            try
            {
                string sectionHeader = $"[{section}]";
                foreach (string line in File.ReadLines(configFilePath))
                {
                    if (string.Equals(
                            line.Trim(),
                            sectionHeader,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }

            return false;
        }
    }
}
