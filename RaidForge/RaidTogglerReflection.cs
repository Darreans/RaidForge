using BepInEx.Logging;
using Unity.Entities;
using Bloodstone.API;
using ProjectM;
using System;
using System.Linq;
using System.Reflection;

namespace RaidForge
{
    public static class RaidTogglerReflection
    {
        private static bool _initialized;
        private static FieldInfo _settingsField; // The field that holds e.g. ._Settings
        private static FieldInfo _castleDamageModeField;

        // ================== PUBLIC METHODS ==================

        /// <summary>
        /// Matches your snippet: sets CastleDamageMode to "Always".
        /// Also updates ServerGameBalanceSettings to "Always".
        /// </summary>
        public static void EnableRaid(ManualLogSource logger)
        {
            logger.LogInfo("[RaidForge] Attempting to set raids ON => CastleDamageMode.Always");
            bool success1 = SetCastleDamageMode("Always", logger);
            bool success2 = SetServerGameBalanceCastleDamage("Always", logger);
            if (!success1 || !success2)
                logger.LogWarning("[RaidForge] Could not fully enable raids!");
        }

        /// <summary>
        /// Matches your snippet: sets CastleDamageMode to "TimeRestricted".
        /// Also updates ServerGameBalanceSettings to "TimeRestricted".
        /// </summary>
        public static void DisableRaid(ManualLogSource logger)
        {
            logger.LogInfo("[RaidForge] Attempting to set raids OFF => CastleDamageMode.TimeRestricted");
            bool success1 = SetCastleDamageMode("TimeRestricted", logger);
            bool success2 = SetServerGameBalanceCastleDamage("TimeRestricted", logger);
            if (!success1 || !success2)
                logger.LogWarning("[RaidForge] Could not fully disable raids!");
        }

        /// <summary>
        /// Reflection read: returns current in-memory CastleDamageMode or null if not found.
        /// </summary>
        public static string? GetCurrentCastleDamageMode(ManualLogSource logger)
        {
            var world = VWorld.Server;
            if (world == null)
            {
                logger.LogError("[RaidForge] No VWorld.Server, can't read CastleDamageMode!");
                return null;
            }

            var sgsSystem = world.GetExistingSystemManaged<ServerGameSettingsSystem>();
            if (sgsSystem == null)
            {
                logger.LogError("[RaidForge] No ServerGameSettingsSystem found!");
                return null;
            }

            InitializeReflection(sgsSystem, logger);
            if (_settingsField == null || _castleDamageModeField == null)
            {
                logger.LogError("[RaidForge] No ._Settings or .CastleDamageMode discovered via reflection!");
                return null;
            }

            var settingsObj = _settingsField.GetValue(sgsSystem);
            if (settingsObj == null)
            {
                logger.LogError("[RaidForge] ._Settings object is null!");
                return null;
            }

            var currentVal = _castleDamageModeField.GetValue(settingsObj);
            return currentVal?.ToString();
        }

        // ================== PRIVATE METHODS ==================

        private static bool SetCastleDamageMode(string enumValue, ManualLogSource logger)
        {
            var world = VWorld.Server;
            if (world == null)
            {
                logger.LogError("[RaidForge] VWorld.Server is null, can't set CastleDamageMode!");
                return false;
            }

            var sgsSystem = world.GetExistingSystemManaged<ServerGameSettingsSystem>();
            if (sgsSystem == null)
            {
                logger.LogError("[RaidForge] No ServerGameSettingsSystem found!");
                return false;
            }

            InitializeReflection(sgsSystem, logger);
            if (_settingsField == null || _castleDamageModeField == null)
            {
                logger.LogError("[RaidForge] Could not find ._Settings or .CastleDamageMode in reflection!");
                return false;
            }

            var settingsObj = _settingsField.GetValue(sgsSystem);
            if (settingsObj == null)
            {
                logger.LogError("[RaidForge] ._Settings is null!");
                return false;
            }

            // Attempt to parse e.g. "Always" or "TimeRestricted"
            object cdmValue = Enum.Parse(_castleDamageModeField.FieldType, enumValue);

            _castleDamageModeField.SetValue(settingsObj, cdmValue);
            logger.LogInfo($"[RaidForge] Set ._Settings.CastleDamageMode => {enumValue} via reflection.");
            return true;
        }

        private static bool SetServerGameBalanceCastleDamage(string enumValue, ManualLogSource logger)
        {
            logger.LogInfo($"[RaidForge] Also updating ServerGameBalanceSettings => {enumValue}");

            var world = VWorld.Server;
            if (world == null)
            {
                logger.LogError("[RaidForge] No VWorld.Server, can't update SGB!");
                return false;
            }

            var em = world.EntityManager;
            var query = em.CreateEntityQuery(ComponentType.ReadWrite<ServerGameBalanceSettings>());
            var eArray = query.ToEntityArray(Unity.Collections.Allocator.Temp);
            try
            {
                if (eArray.Length < 1)
                {
                    logger.LogWarning("[RaidForge] No entity with ServerGameBalanceSettings found!");
                    return false;
                }

                var entity = eArray[0];
                var sgb = em.GetComponentData<ServerGameBalanceSettings>(entity);

                var cdmType = sgb.CastleDamageMode.GetType();
                object cdmValue = Enum.Parse(cdmType, enumValue);

                sgb.CastleDamageMode = (CastleDamageMode)cdmValue;
                em.SetComponentData(entity, sgb);

                logger.LogInfo($"[RaidForge] Updated SGB.CastleDamageMode => {enumValue}");
                return true;
            }
            finally
            {
                eArray.Dispose();
            }
        }

        /// <summary>
        /// Find a field named "_Settings" in ServerGameSettingsSystem, 
        /// then locate "CastleDamageMode" in that object. 
        /// </summary>
        private static void InitializeReflection(ServerGameSettingsSystem sgsSystem, ManualLogSource logger)
        {
            if (_initialized) return;
            _initialized = true;

            var sgsType = sgsSystem.GetType();
            // Typically it's public / private "._Settings"
            _settingsField = sgsType.GetField("_Settings", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            if (_settingsField == null)
            {
                logger.LogError("[RaidForge] No field named '_Settings' in ServerGameSettingsSystem. We'll try fallback...");
                // If it's not named _Settings, we might look for any field with "settings" in it:
                _settingsField = sgsType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .FirstOrDefault(f => f.Name.ToLower().Contains("settings"));
            }

            if (_settingsField == null)
            {
                logger.LogError("[RaidForge] Still no field with 'settings' in name!");
                return;
            }

            // Now find CastleDamageMode
            var settingsType = _settingsField.FieldType;
            _castleDamageModeField = settingsType.GetField("CastleDamageMode", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            if (_castleDamageModeField == null)
            {
                logger.LogError("[RaidForge] Could not find 'CastleDamageMode' in the _Settings object!");
            }
            else
            {
                logger.LogInfo("[RaidForge] Reflection found ._Settings + .CastleDamageMode fields!");
            }
        }
    }
}
