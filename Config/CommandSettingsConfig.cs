using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using BepInEx.Logging;

namespace RaidForge.Config
{
    public static class CommandSettingsConfig
    {
        private const string MasterSection = "00 - Enable Custom Commands";
        private const string VisibilitySection = "01 - VCF Help Visibility";
        private const string LegacyListsSection = "00 - Command Lists";
        private const string LegacyGlobalSection = "00 - Custom Command Settings";
        private const string ObsoleteListSection = "01 - raidcommands";

        private static readonly CommandDefinition[] Definitions =
        {
            Admin("raidrefreshcache", null, "Clears and rebuilds the RaidForge ownership caches."),
            Admin("raidforge", null, "Shows RaidForge status, help, and cache information.", "[?|status|cache]"),
            Public("raidoptin", null, "Opts you and your clan into being raidable."),
            Public("raidoptout", null, "Opts you and your clan out of being raidable when the lock permits."),
            Public("raidoptstatus", null, "Checks your current opt-in raiding status."),
            Public("raidoptlist", null, "Lists manually opted-in raidable players and clans.", "[page]"),
            Admin("forceopt", null, "Forces a player or clan's opt-in status.", "<PlayerName> <in|out>"),
            Admin("reloadraidforge", null, "Reloads RaidForge configuration files except startup-only command settings."),
            Admin("raidon", null, "Manually turns raids on."),
            Admin("raidoff", null, "Manually turns raids off."),
            Admin("raidauto", null, "Clears the manual raid override and resumes the schedule."),
            Admin("removeorp", null, "Removes offline protection from a player or clan until they reconnect.", "<PlayerName>"),
            Public("raidtime", "raidt", "Shows the time until the next scheduled raid window."),
            Public("raiddays", "raidd", "Shows the raid schedule for the week."),
            Public("raidstatus", "raids", "Shows a player or clan's raid vulnerability status.", "<PlayerName>"),
            Admin("raidstatusreason", null, "Shows the detailed reason for a player or clan's ORP status.", "<PlayerName>"),
            Admin("golemstartdate", null, "Sets the golem automation start date to the current time."),
            Admin("golemcurrent", null, "Shows the current golem health settings."),
            Admin("golemsethp", null, "Sets and persists a siege golem health level.", "<LevelName>"),
            Admin("golemauto", null, "Clears the manual golem health override."),
            Admin("golemlist", null, "Lists available siege golem health levels."),
            Admin("golem", null, "Transforms the target player into a siege golem.", "[PlayerName]"),
            Admin("clearraidforgeicons", null, "Clears all active RaidForge map icons."),
            Public("buyorp", null, "Buys raid-day ORP protection for you or your clan.", "<days>"),
            Public("buyorpstatus", "orpamount", "Shows your purchased ORP protection days."),
            Admin("givebuyorp", null, "Gives purchased ORP raid days to a player or clan.", "<PlayerName> <amount>"),
            Admin("removebuyorp", null, "Removes purchased ORP raid days from a player or clan.", "<PlayerName> <amount>"),
            Admin("raidconfigview", null, "Shows a specific RaidForge configuration section.", "<?|number>"),
            Admin("raidconfigviewall", null, "Shows all currently loaded RaidForge configuration values.")
        };

        private static volatile RuntimeSnapshot _snapshot = RuntimeSnapshot.Empty;

        public static ConfigFile ConfigFileInstance { get; private set; }
        public static ConfigEntry<bool> EnableCustomCommandSettings { get; private set; }
        public static ConfigEntry<bool> ShowPlayerCommandList { get; private set; }
        public static ConfigEntry<bool> ShowAdminCommandList { get; private set; }

        public static bool CustomSettingsEnabled => _snapshot.CustomSettingsEnabled;
        public static IReadOnlyList<RuntimeCommand> RuntimeCommands => _snapshot.Commands;

        public static void Initialize(ConfigFile configFile, ManualLogSource logger = null)
        {
            ConfigFileInstance = configFile;
            LegacyGlobalSettings legacySettings =
                ReadAndRemoveObsoleteCommandSettings(configFile);
            LegacyCommandSettings legacyRaidTimeSettings =
                ReadAndRemoveLegacyRaidTimerSettings(configFile);

            EnableCustomCommandSettings = configFile.Bind(
                MasterSection,
                "EnableCustomCommandSettings",
                legacySettings.EnableCustomCommandSettings,
                "Master switch for all command customization below. Changes only take effect after a full server restart. If false, RaidForge uses its built-in command names, shorthands, enabled states, and normal VCF help behavior.");

            ShowPlayerCommandList = configFile.Bind(
                VisibilitySection,
                "ShowPlayerCommandList",
                legacySettings.ShowPlayerCommandList,
                "When custom command settings are enabled, controls whether RaidForge appears in normal VCF help for non-admin players. Requires a server restart.");

            ShowAdminCommandList = configFile.Bind(
                VisibilitySection,
                "ShowAdminCommandList",
                legacySettings.ShowAdminCommandList,
                "When custom command settings are enabled, controls whether RaidForge appears in normal VCF help for admins. Requires a server restart.");

            for (int index = 0; index < Definitions.Length; index++)
            {
                CommandDefinition definition = Definitions[index];

                // Start at 02 so existing 3.2.0 sections keep their numbers after
                // the obsolete raidcommands entry is removed.
                string section = $"{index + 2:00} - {definition.CanonicalName}";
                bool useLegacyRaidTimeSettings =
                    string.Equals(
                        definition.CanonicalName,
                        "raidtime",
                        StringComparison.OrdinalIgnoreCase);

                definition.Enabled = configFile.Bind(
                    section,
                    "Enabled",
                    useLegacyRaidTimeSettings
                        ? legacyRaidTimeSettings.Enabled
                        : true,
                    "If custom command settings are enabled, false removes this command and its shorthand from VCF until the next server restart.");

                definition.Name = configFile.Bind(
                    section,
                    "Name",
                    useLegacyRaidTimeSettings
                        ? legacyRaidTimeSettings.Name
                        : definition.CanonicalName,
                    "Primary command name without the leading period. Requires EnableCustomCommandSettings=true and a server restart.");

                definition.ShortHand = configFile.Bind(
                    section,
                    "ShortHand",
                    useLegacyRaidTimeSettings
                        ? legacyRaidTimeSettings.ShortHand
                        : definition.DefaultShortHand ?? string.Empty,
                    "Optional shorthand without the leading period. Leave blank to disable it. Requires EnableCustomCommandSettings=true and a server restart.");

                definition.ShowInPlayerList = configFile.Bind(
                    section,
                    "ShowInPlayerList",
                    useLegacyRaidTimeSettings
                        ? legacyRaidTimeSettings.ShowInPlayerList
                        : !definition.AdminOnly,
                    "If custom command settings are enabled, controls visibility in normal VCF help for non-admin players. VCF still hides commands the player cannot execute. Requires a server restart.");

                definition.ShowInAdminList = configFile.Bind(
                    section,
                    "ShowInAdminList",
                    useLegacyRaidTimeSettings
                        ? legacyRaidTimeSettings.ShowInAdminList
                        : true,
                    "If custom command settings are enabled, controls visibility in normal VCF help for admins. Requires a server restart.");
            }

            BuildStartupSnapshot(logger);
            logger?.LogInfo(
                $"[CommandSettings] Loaded {Definitions.Length} RaidForge command definitions. " +
                $"Custom settings enabled={CustomSettingsEnabled}; changes require a full server restart.");
        }

        public static bool TryGetCommand(string token, out RuntimeCommand command)
        {
            command = null;

            if (string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            string normalized = token.Trim().TrimStart('.');
            return _snapshot.TokenLookup.TryGetValue(normalized, out command);
        }

        public static string GetInvocation(string canonicalName)
        {
            return TryGetCommand(canonicalName, out RuntimeCommand command)
                ? $".{command.PrimaryName}"
                : $".{canonicalName}";
        }

        public static bool ShouldShowInVcfHelp(string commandName, bool isAdmin)
        {
            RuntimeSnapshot snapshot = _snapshot;

            if (!snapshot.CustomSettingsEnabled)
            {
                return true;
            }

            if (!snapshot.TokenLookup.TryGetValue(commandName, out RuntimeCommand command))
            {
                return true;
            }

            if (!command.Enabled)
            {
                return false;
            }

            bool listEnabled = isAdmin
                ? snapshot.ShowAdminCommandList
                : snapshot.ShowPlayerCommandList;

            return listEnabled &&
                (isAdmin ? command.ShowInAdminList : command.ShowInPlayerList);
        }

        private static void BuildStartupSnapshot(ManualLogSource logger)
        {
            bool customSettingsEnabled = EnableCustomCommandSettings?.Value ?? false;
            var claimedTokens = new Dictionary<string, CommandDefinition>(StringComparer.OrdinalIgnoreCase);
            var runtimes = new List<RuntimeCommand>(Definitions.Length);

            foreach (CommandDefinition definition in Definitions)
            {
                string primaryName = definition.CanonicalName;
                string shortHand = definition.DefaultShortHand ?? string.Empty;
                bool enabled = true;
                bool showInPlayerList = !definition.AdminOnly;
                bool showInAdminList = true;

                if (customSettingsEnabled)
                {
                    primaryName = NormalizeConfiguredName(
                        definition.Name?.Value,
                        definition.CanonicalName,
                        allowBlank: false,
                        logger,
                        definition.CanonicalName,
                        "Name");

                    if (IsReservedByAnotherDefinition(primaryName, definition))
                    {
                        logger?.LogWarning(
                            $"[CommandSettings] '{primaryName}' is a built-in name for another RaidForge command. " +
                            $"'{definition.CanonicalName}' will keep its default name.");
                        primaryName = definition.CanonicalName;
                    }

                    if (claimedTokens.TryGetValue(primaryName, out CommandDefinition existingPrimary))
                    {
                        logger?.LogWarning(
                            $"[CommandSettings] '{primaryName}' is already used by '{existingPrimary.CanonicalName}'. " +
                            $"'{definition.CanonicalName}' will keep its default name.");
                        primaryName = definition.CanonicalName;
                    }

                    shortHand = NormalizeConfiguredName(
                        definition.ShortHand?.Value,
                        string.Empty,
                        allowBlank: true,
                        logger,
                        definition.CanonicalName,
                        "ShortHand");

                    if (!string.IsNullOrEmpty(shortHand) &&
                        (string.Equals(shortHand, primaryName, StringComparison.OrdinalIgnoreCase) ||
                         IsReservedByAnotherDefinition(shortHand, definition) ||
                         claimedTokens.ContainsKey(shortHand)))
                    {
                        logger?.LogWarning(
                            $"[CommandSettings] Shorthand '{shortHand}' for '{definition.CanonicalName}' conflicts with another command and was disabled.");
                        shortHand = string.Empty;
                    }

                    enabled = definition.Enabled?.Value ?? true;
                    showInPlayerList = definition.ShowInPlayerList?.Value ?? !definition.AdminOnly;
                    showInAdminList = definition.ShowInAdminList?.Value ?? true;
                }

                claimedTokens[primaryName] = definition;

                if (!string.IsNullOrEmpty(shortHand))
                {
                    claimedTokens[shortHand] = definition;
                }

                runtimes.Add(new RuntimeCommand(
                    definition,
                    enabled,
                    primaryName,
                    shortHand,
                    showInPlayerList,
                    showInAdminList));
            }

            var tokenLookup = new Dictionary<string, RuntimeCommand>(StringComparer.OrdinalIgnoreCase);

            foreach (RuntimeCommand runtime in runtimes)
            {
                AddToken(tokenLookup, runtime.Definition.CanonicalName, runtime);
                AddToken(tokenLookup, runtime.Definition.DefaultShortHand, runtime);
                AddToken(tokenLookup, runtime.PrimaryName, runtime);
                AddToken(tokenLookup, runtime.ShortHand, runtime);
            }

            _snapshot = new RuntimeSnapshot(
                customSettingsEnabled,
                ShowPlayerCommandList?.Value ?? true,
                ShowAdminCommandList?.Value ?? true,
                runtimes.ToArray(),
                tokenLookup);
        }

        private static LegacyGlobalSettings ReadAndRemoveObsoleteCommandSettings(
            ConfigFile configFile)
        {
            var legacyEnableDefinition = new ConfigDefinition(
                LegacyGlobalSection,
                "EnableCustomCommandSettings");
            var legacyPlayerVisibilityDefinition = new ConfigDefinition(
                LegacyGlobalSection,
                "ShowPlayerCommandList");
            var legacyAdminVisibilityDefinition = new ConfigDefinition(
                LegacyGlobalSection,
                "ShowAdminCommandList");
            var oldestPlayerVisibilityDefinition = new ConfigDefinition(
                LegacyListsSection,
                "ShowPlayerCommandList");
            var oldestAdminVisibilityDefinition = new ConfigDefinition(
                LegacyListsSection,
                "ShowAdminCommandList");
            var enabledDefinition = new ConfigDefinition(ObsoleteListSection, "Enabled");
            var nameDefinition = new ConfigDefinition(ObsoleteListSection, "Name");
            var shortHandDefinition = new ConfigDefinition(ObsoleteListSection, "ShortHand");
            var playerListDefinition = new ConfigDefinition(ObsoleteListSection, "ShowInPlayerList");
            var adminListDefinition = new ConfigDefinition(ObsoleteListSection, "ShowInAdminList");

            ConfigEntry<bool> legacyEnable = configFile.Bind(
                legacyEnableDefinition,
                false,
                new ConfigDescription("Legacy RaidForge custom-command master switch."));
            ConfigEntry<bool> legacyPlayerVisibility = configFile.Bind(
                legacyPlayerVisibilityDefinition,
                true,
                new ConfigDescription("Legacy RaidForge VCF help setting."));
            ConfigEntry<bool> legacyAdminVisibility = configFile.Bind(
                legacyAdminVisibilityDefinition,
                true,
                new ConfigDescription("Legacy RaidForge VCF help setting."));
            configFile.Bind(
                oldestPlayerVisibilityDefinition,
                true,
                new ConfigDescription("Obsolete RaidForge command-list setting."));
            configFile.Bind(
                oldestAdminVisibilityDefinition,
                true,
                new ConfigDescription("Obsolete RaidForge command-list setting."));
            configFile.Bind(enabledDefinition, true, new ConfigDescription("Obsolete RaidForge command-list setting."));
            configFile.Bind(nameDefinition, "raidcommands", new ConfigDescription("Obsolete RaidForge command-list setting."));
            configFile.Bind(shortHandDefinition, "rc", new ConfigDescription("Obsolete RaidForge command-list setting."));
            configFile.Bind(playerListDefinition, true, new ConfigDescription("Obsolete RaidForge command-list setting."));
            configFile.Bind(adminListDefinition, true, new ConfigDescription("Obsolete RaidForge command-list setting."));

            var settings = new LegacyGlobalSettings(
                legacyEnable.Value,
                legacyPlayerVisibility.Value,
                legacyAdminVisibility.Value);

            configFile.Remove(legacyEnableDefinition);
            configFile.Remove(legacyPlayerVisibilityDefinition);
            configFile.Remove(legacyAdminVisibilityDefinition);
            configFile.Remove(oldestPlayerVisibilityDefinition);
            configFile.Remove(oldestAdminVisibilityDefinition);
            configFile.Remove(enabledDefinition);
            configFile.Remove(nameDefinition);
            configFile.Remove(shortHandDefinition);
            configFile.Remove(playerListDefinition);
            configFile.Remove(adminListDefinition);
            configFile.Save();
            return settings;
        }

        private static LegacyCommandSettings ReadAndRemoveLegacyRaidTimerSettings(
            ConfigFile configFile)
        {
            const string legacySection = "14 - raidtimer";

            var enabledDefinition = new ConfigDefinition(legacySection, "Enabled");
            var nameDefinition = new ConfigDefinition(legacySection, "Name");
            var shortHandDefinition = new ConfigDefinition(legacySection, "ShortHand");
            var playerListDefinition = new ConfigDefinition(legacySection, "ShowInPlayerList");
            var adminListDefinition = new ConfigDefinition(legacySection, "ShowInAdminList");

            ConfigEntry<bool> enabled = configFile.Bind(
                enabledDefinition,
                true,
                new ConfigDescription("Legacy RaidForge raidtimer setting."));
            ConfigEntry<string> name = configFile.Bind(
                nameDefinition,
                "raidtimer",
                new ConfigDescription("Legacy RaidForge raidtimer setting."));
            ConfigEntry<string> shortHand = configFile.Bind(
                shortHandDefinition,
                "raidt",
                new ConfigDescription("Legacy RaidForge raidtimer setting."));
            ConfigEntry<bool> showInPlayerList = configFile.Bind(
                playerListDefinition,
                true,
                new ConfigDescription("Legacy RaidForge raidtimer setting."));
            ConfigEntry<bool> showInAdminList = configFile.Bind(
                adminListDefinition,
                true,
                new ConfigDescription("Legacy RaidForge raidtimer setting."));

            string migratedName = string.Equals(
                name.Value,
                "raidtimer",
                StringComparison.OrdinalIgnoreCase)
                ? "raidtime"
                : name.Value;

            var settings = new LegacyCommandSettings(
                enabled.Value,
                migratedName,
                shortHand.Value,
                showInPlayerList.Value,
                showInAdminList.Value);

            configFile.Remove(enabledDefinition);
            configFile.Remove(nameDefinition);
            configFile.Remove(shortHandDefinition);
            configFile.Remove(playerListDefinition);
            configFile.Remove(adminListDefinition);
            configFile.Save();
            return settings;
        }

        private static void AddToken(
            Dictionary<string, RuntimeCommand> lookup,
            string token,
            RuntimeCommand runtime)
        {
            if (!string.IsNullOrWhiteSpace(token) && !lookup.ContainsKey(token))
            {
                lookup[token] = runtime;
            }
        }

        private static bool IsReservedByAnotherDefinition(string token, CommandDefinition owner)
        {
            if (string.IsNullOrEmpty(token))
            {
                return false;
            }

            foreach (CommandDefinition definition in Definitions)
            {
                if (ReferenceEquals(definition, owner))
                {
                    continue;
                }

                if (string.Equals(token, definition.CanonicalName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(token, definition.DefaultShortHand, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string NormalizeConfiguredName(
            string configuredValue,
            string fallback,
            bool allowBlank,
            ManualLogSource logger,
            string canonicalName,
            string settingName)
        {
            string value = (configuredValue ?? string.Empty).Trim().TrimStart('.');

            if (allowBlank && value.Length == 0)
            {
                return string.Empty;
            }

            if (!IsValidCommandToken(value))
            {
                logger?.LogWarning(
                    $"[CommandSettings] Invalid {settingName} '{configuredValue}' for '{canonicalName}'. Using '{fallback}'.");
                return fallback;
            }

            return value;
        }

        private static bool IsValidCommandToken(string value)
        {
            if (string.IsNullOrEmpty(value) ||
                value.Length > 32 ||
                !char.IsLetter(value[0]))
            {
                return false;
            }

            for (int index = 1; index < value.Length; index++)
            {
                char character = value[index];

                if (!char.IsLetterOrDigit(character) &&
                    character != '_' &&
                    character != '-')
                {
                    return false;
                }
            }

            return true;
        }

        private static CommandDefinition Public(
            string canonicalName,
            string defaultShortHand,
            string description,
            string usage = null)
        {
            return new CommandDefinition(canonicalName, defaultShortHand, description, usage, adminOnly: false);
        }

        private static CommandDefinition Admin(
            string canonicalName,
            string defaultShortHand,
            string description,
            string usage = null)
        {
            return new CommandDefinition(canonicalName, defaultShortHand, description, usage, adminOnly: true);
        }

        public sealed class RuntimeCommand
        {
            public CommandDefinition Definition { get; }
            public bool Enabled { get; }
            public string PrimaryName { get; }
            public string ShortHand { get; }
            public bool ShowInPlayerList { get; }
            public bool ShowInAdminList { get; }

            internal RuntimeCommand(
                CommandDefinition definition,
                bool enabled,
                string primaryName,
                string shortHand,
                bool showInPlayerList,
                bool showInAdminList)
            {
                Definition = definition;
                Enabled = enabled;
                PrimaryName = primaryName;
                ShortHand = shortHand;
                ShowInPlayerList = showInPlayerList;
                ShowInAdminList = showInAdminList;
            }
        }

        public sealed class CommandDefinition
        {
            public string CanonicalName { get; }
            public string DefaultShortHand { get; }
            public string Description { get; }
            public string Usage { get; }
            public bool AdminOnly { get; }

            internal ConfigEntry<bool> Enabled { get; set; }
            internal ConfigEntry<string> Name { get; set; }
            internal ConfigEntry<string> ShortHand { get; set; }
            internal ConfigEntry<bool> ShowInPlayerList { get; set; }
            internal ConfigEntry<bool> ShowInAdminList { get; set; }

            internal CommandDefinition(
                string canonicalName,
                string defaultShortHand,
                string description,
                string usage,
                bool adminOnly)
            {
                CanonicalName = canonicalName;
                DefaultShortHand = defaultShortHand;
                Description = description;
                Usage = usage;
                AdminOnly = adminOnly;
            }
        }

        private sealed class RuntimeSnapshot
        {
            public static readonly RuntimeSnapshot Empty = new(
                false,
                true,
                true,
                Array.Empty<RuntimeCommand>(),
                new Dictionary<string, RuntimeCommand>(StringComparer.OrdinalIgnoreCase));

            public bool CustomSettingsEnabled { get; }
            public bool ShowPlayerCommandList { get; }
            public bool ShowAdminCommandList { get; }
            public RuntimeCommand[] Commands { get; }
            public Dictionary<string, RuntimeCommand> TokenLookup { get; }

            public RuntimeSnapshot(
                bool customSettingsEnabled,
                bool showPlayerCommandList,
                bool showAdminCommandList,
                RuntimeCommand[] commands,
                Dictionary<string, RuntimeCommand> tokenLookup)
            {
                CustomSettingsEnabled = customSettingsEnabled;
                ShowPlayerCommandList = showPlayerCommandList;
                ShowAdminCommandList = showAdminCommandList;
                Commands = commands;
                TokenLookup = tokenLookup;
            }
        }

        private readonly struct LegacyGlobalSettings
        {
            public bool EnableCustomCommandSettings { get; }
            public bool ShowPlayerCommandList { get; }
            public bool ShowAdminCommandList { get; }

            public LegacyGlobalSettings(
                bool enableCustomCommandSettings,
                bool showPlayerCommandList,
                bool showAdminCommandList)
            {
                EnableCustomCommandSettings = enableCustomCommandSettings;
                ShowPlayerCommandList = showPlayerCommandList;
                ShowAdminCommandList = showAdminCommandList;
            }
        }

        private readonly struct LegacyCommandSettings
        {
            public bool Enabled { get; }
            public string Name { get; }
            public string ShortHand { get; }
            public bool ShowInPlayerList { get; }
            public bool ShowInAdminList { get; }

            public LegacyCommandSettings(
                bool enabled,
                string name,
                string shortHand,
                bool showInPlayerList,
                bool showInAdminList)
            {
                Enabled = enabled;
                Name = name;
                ShortHand = shortHand;
                ShowInPlayerList = showInPlayerList;
                ShowInAdminList = showInAdminList;
            }
        }
    }
}
