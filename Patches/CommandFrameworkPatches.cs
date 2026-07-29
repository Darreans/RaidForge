using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RaidForge.Config;
using RaidForge.Services;
using VampireCommandFramework;

namespace RaidForge.Patches
{
    /*
        VCF remains responsible for rendering and paginating .help. When custom
        command settings are enabled, this patch temporarily filters only
        RaidForge's metadata during the two VCF help calls, then restores the
        registry immediately. Command execution and admin middleware are never
        changed by this visibility filter.
    */
    [HarmonyPatch]
    internal static class RaidForgeVcfHelpVisibilityPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            Type helpCommandsType = AccessTools.TypeByName(
                "VampireCommandFramework.Basics.HelpCommands");

            if (helpCommandsType == null)
            {
                return Array.Empty<MethodBase>();
            }

            return helpCommandsType
                .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(method =>
                    method.Name == "HelpCommand" ||
                    method.Name == "HelpAllCommand")
                .Cast<MethodBase>();
        }

        private static void Prefix(ICommandContext __0, out HiddenHelpState __state)
        {
            __state = null;

            if (!CommandSettingsConfig.CustomSettingsEnabled ||
                !CommandRegistryCustomizationService.TryGetRaidForgeCommandMap(
                    out IDictionary commandMap))
            {
                return;
            }

            var state = new HiddenHelpState(commandMap);
            __state = state;

            foreach (DictionaryEntry entry in commandMap)
            {
                string commandName =
                    CommandRegistryCustomizationService.GetRegisteredCommandName(entry.Key);

                if (!CommandSettingsConfig.ShouldShowInVcfHelp(
                    commandName,
                    __0.IsAdmin))
                {
                    state.RemovedEntries.Add(entry);
                }
            }

            foreach (DictionaryEntry entry in state.RemovedEntries)
            {
                commandMap.Remove(entry.Key);
            }
        }

        private static void Postfix(HiddenHelpState __state)
        {
            __state?.Restore();
        }

        private static Exception Finalizer(
            Exception __exception,
            HiddenHelpState __state)
        {
            __state?.Restore();
            return __exception;
        }

        private sealed class HiddenHelpState
        {
            private bool _restored;

            public IDictionary CommandMap { get; }
            public List<DictionaryEntry> RemovedEntries { get; } = new();

            public HiddenHelpState(IDictionary commandMap)
            {
                CommandMap = commandMap;
            }

            public void Restore()
            {
                if (_restored)
                {
                    return;
                }

                foreach (DictionaryEntry entry in RemovedEntries)
                {
                    if (!CommandMap.Contains(entry.Key))
                    {
                        CommandMap.Add(entry.Key, entry.Value);
                    }
                }

                _restored = true;
            }
        }
    }
}
