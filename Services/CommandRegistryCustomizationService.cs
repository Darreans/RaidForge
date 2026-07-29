using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RaidForge.Config;
using VampireCommandFramework;

namespace RaidForge.Services
{
    internal static class CommandRegistryCustomizationService
    {
        public static void ApplyStartupConfiguration()
        {
            if (!CommandSettingsConfig.CustomSettingsEnabled)
            {
                Plugin.Logger?.LogInfo(
                    "[CommandSettings] Custom command settings are disabled. VCF is using RaidForge's built-in commands and normal help registration.");
                return;
            }

            Assembly raidForgeAssembly = typeof(Plugin).Assembly;
            bool registryWasRemoved = false;

            try
            {
                IDictionary assemblyCommandMap = GetAssemblyCommandMap();

                if (!assemblyCommandMap.Contains(raidForgeAssembly) ||
                    assemblyCommandMap[raidForgeAssembly] is not IDictionary originalCommandMap)
                {
                    throw new InvalidOperationException("VCF did not expose RaidForge's registered command map.");
                }

                List<PreparedCommand> preparedCommands = PrepareCommands(originalCommandMap);
                Type commandMapType = originalCommandMap.GetType();
                ValidateCommandKeysAvailable(
                    assemblyCommandMap,
                    raidForgeAssembly,
                    preparedCommands);

                CommandRegistry.UnregisterAssembly(raidForgeAssembly);
                registryWasRemoved = true;

                object commandCache = GetCommandCache();
                MethodInfo addCommandMethod = commandCache.GetType().GetMethod(
                    "AddCommand",
                    BindingFlags.Instance | BindingFlags.NonPublic);

                if (addCommandMethod == null)
                {
                    throw new MissingMethodException(commandCache.GetType().FullName, "AddCommand");
                }

                var customizedCommandMap = (IDictionary)Activator.CreateInstance(commandMapType);
                assemblyCommandMap[raidForgeAssembly] = customizedCommandMap;

                foreach (PreparedCommand prepared in preparedCommands)
                {
                    var registeredKeys = new List<string>();
                    customizedCommandMap.Add(prepared.Metadata, registeredKeys);

                    foreach (string commandName in prepared.CommandNames)
                    {
                        string commandKey = $".{commandName}";
                        addCommandMethod.Invoke(
                            commandCache,
                            new object[]
                            {
                                commandKey,
                                prepared.Parameters,
                                prepared.Metadata
                            });
                        registeredKeys.Add(commandKey);
                    }
                }

                int enabledCount = preparedCommands.Count;
                int disabledCount = CommandSettingsConfig.RuntimeCommands.Count - enabledCount;
                Plugin.Logger?.LogInfo(
                    $"[CommandSettings] Applied startup-only VCF registration: enabled={enabledCount}, disabled={disabledCount}. " +
                    "Configured names and shorthands now appear through normal .help.");
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogError(
                    $"[CommandSettings] Could not apply custom VCF registration. Restoring built-in RaidForge commands: {ex}");

                try
                {
                    if (!registryWasRemoved)
                    {
                        Plugin.Logger?.LogWarning(
                            "[CommandSettings] Built-in RaidForge commands remain registered because customization failed before the registry was changed.");
                        return;
                    }

                    CommandRegistry.UnregisterAssembly(raidForgeAssembly);
                    CommandRegistry.RegisterAll(raidForgeAssembly);
                }
                catch (Exception restoreException)
                {
                    Plugin.Logger?.LogError(
                        $"[CommandSettings] Failed to restore built-in VCF commands: {restoreException}");
                }
            }
        }

        private static void ValidateCommandKeysAvailable(
            IDictionary assemblyCommandMap,
            Assembly raidForgeAssembly,
            IReadOnlyList<PreparedCommand> preparedCommands)
        {
            var occupiedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (DictionaryEntry assemblyEntry in assemblyCommandMap)
            {
                if (Equals(assemblyEntry.Key, raidForgeAssembly) ||
                    assemblyEntry.Value is not IDictionary commandMap)
                {
                    continue;
                }

                foreach (DictionaryEntry commandEntry in commandMap)
                {
                    if (commandEntry.Value is not IEnumerable registeredKeys ||
                        commandEntry.Value is string)
                    {
                        continue;
                    }

                    foreach (object registeredKey in registeredKeys)
                    {
                        if (registeredKey is string key)
                        {
                            occupiedKeys.Add(key);
                        }
                    }
                }
            }

            foreach (PreparedCommand prepared in preparedCommands)
            {
                foreach (string commandName in prepared.CommandNames)
                {
                    string commandKey = $".{commandName}";

                    if (occupiedKeys.Contains(commandKey))
                    {
                        throw new InvalidOperationException(
                            $"Configured command '{commandKey}' is already registered by VCF or another mod.");
                    }
                }
            }
        }

        internal static bool TryGetRaidForgeCommandMap(out IDictionary commandMap)
        {
            commandMap = null;

            try
            {
                IDictionary assemblyCommandMap = GetAssemblyCommandMap();
                Assembly raidForgeAssembly = typeof(Plugin).Assembly;

                if (assemblyCommandMap.Contains(raidForgeAssembly) &&
                    assemblyCommandMap[raidForgeAssembly] is IDictionary raidForgeCommands)
                {
                    commandMap = raidForgeCommands;
                    return true;
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogDebug($"[CommandSettings] VCF command-map lookup failed: {ex.Message}");
            }

            return false;
        }

        internal static string GetRegisteredCommandName(object metadata)
        {
            if (metadata == null)
            {
                return null;
            }

            PropertyInfo attributeProperty = metadata.GetType().GetProperty(
                "Attribute",
                BindingFlags.Instance | BindingFlags.Public);

            return attributeProperty?.GetValue(metadata) is CommandAttribute attribute
                ? attribute.Name
                : null;
        }

        private static List<PreparedCommand> PrepareCommands(IDictionary originalCommandMap)
        {
            var preparedCommands = new List<PreparedCommand>();

            foreach (DictionaryEntry entry in originalCommandMap)
            {
                object originalMetadata = entry.Key;
                Type metadataType = originalMetadata.GetType();
                PropertyInfo attributeProperty = GetRequiredProperty(metadataType, "Attribute");
                var originalAttribute = (CommandAttribute)attributeProperty.GetValue(originalMetadata);

                if (!CommandSettingsConfig.TryGetCommand(
                    originalAttribute.Name,
                    out CommandSettingsConfig.RuntimeCommand runtime))
                {
                    Plugin.Logger?.LogWarning(
                        $"[CommandSettings] Registered command '{originalAttribute.Name}' has no configuration definition and will keep its built-in registration.");
                    runtime = CreateDefaultRuntime(originalAttribute);
                }

                if (!runtime.Enabled)
                {
                    continue;
                }

                string configuredShortHand = string.IsNullOrWhiteSpace(runtime.ShortHand)
                    ? null
                    : runtime.ShortHand;

                var customizedAttribute = new CommandAttribute(
                    runtime.PrimaryName,
                    configuredShortHand,
                    originalAttribute.Usage,
                    originalAttribute.Description,
                    originalAttribute.Id,
                    originalAttribute.AdminOnly);

                object customizedMetadata = CreateMetadataClone(
                    metadataType,
                    originalMetadata,
                    customizedAttribute);

                var commandNames = new List<string> { runtime.PrimaryName };

                if (!string.IsNullOrEmpty(configuredShortHand))
                {
                    commandNames.Add(configuredShortHand);
                }

                preparedCommands.Add(new PreparedCommand(
                    customizedMetadata,
                    (ParameterInfo[])GetRequiredProperty(metadataType, "Parameters").GetValue(originalMetadata),
                    commandNames));
            }

            return preparedCommands;
        }

        private static object CreateMetadataClone(
            Type metadataType,
            object originalMetadata,
            CommandAttribute customizedAttribute)
        {
            ConstructorInfo metadataConstructor = metadataType
                .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(constructor =>
                {
                    ParameterInfo[] parameters = constructor.GetParameters();
                    return parameters.Length == 8 &&
                        parameters[0].ParameterType == typeof(CommandAttribute);
                });

            if (metadataConstructor == null)
            {
                throw new MissingMethodException(metadataType.FullName, ".ctor(CommandAttribute, ...)");
            }

            return metadataConstructor.Invoke(new[]
            {
                customizedAttribute,
                GetRequiredProperty(metadataType, "Assembly").GetValue(originalMetadata),
                GetRequiredProperty(metadataType, "Method").GetValue(originalMetadata),
                GetRequiredProperty(metadataType, "Constructor").GetValue(originalMetadata),
                GetRequiredProperty(metadataType, "Parameters").GetValue(originalMetadata),
                GetRequiredProperty(metadataType, "ContextType").GetValue(originalMetadata),
                GetRequiredProperty(metadataType, "ConstructorType").GetValue(originalMetadata),
                GetRequiredProperty(metadataType, "GroupAttribute").GetValue(originalMetadata)
            });
        }

        private static CommandSettingsConfig.RuntimeCommand CreateDefaultRuntime(
            CommandAttribute attribute)
        {
            var definition = new CommandSettingsConfig.CommandDefinition(
                attribute.Name,
                attribute.ShortHand,
                attribute.Description,
                attribute.Usage,
                attribute.AdminOnly);

            return new CommandSettingsConfig.RuntimeCommand(
                definition,
                enabled: true,
                attribute.Name,
                attribute.ShortHand ?? string.Empty,
                showInPlayerList: !attribute.AdminOnly,
                showInAdminList: true);
        }

        private static IDictionary GetAssemblyCommandMap()
        {
            PropertyInfo mapProperty = typeof(CommandRegistry).GetProperty(
                "AssemblyCommandMap",
                BindingFlags.Static | BindingFlags.NonPublic);

            if (mapProperty?.GetValue(null) is not IDictionary map)
            {
                throw new MissingMemberException(typeof(CommandRegistry).FullName, "AssemblyCommandMap");
            }

            return map;
        }

        private static object GetCommandCache()
        {
            FieldInfo cacheField = typeof(CommandRegistry).GetField(
                "_cache",
                BindingFlags.Static | BindingFlags.NonPublic);

            return cacheField?.GetValue(null)
                ?? throw new MissingFieldException(typeof(CommandRegistry).FullName, "_cache");
        }

        private static PropertyInfo GetRequiredProperty(Type type, string name)
        {
            return type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public)
                ?? throw new MissingMemberException(type.FullName, name);
        }

        private sealed class PreparedCommand
        {
            public object Metadata { get; }
            public ParameterInfo[] Parameters { get; }
            public IReadOnlyList<string> CommandNames { get; }

            public PreparedCommand(
                object metadata,
                ParameterInfo[] parameters,
                IReadOnlyList<string> commandNames)
            {
                Metadata = metadata;
                Parameters = parameters;
                CommandNames = commandNames;
            }
        }
    }
}
