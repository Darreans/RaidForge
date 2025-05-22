using System;
using BepInEx.Logging; 
using Unity.Entities;
using ProjectM;
using RaidForge.Utils; 

namespace RaidForge.Systems
{
    public static class SiegeWeaponSystem
    {
        public static bool SetSiegeWeaponHealth(SiegeWeaponHealth newValue, ManualLogSource loggerContext = null)
        {
            try
            {
                if (VWorld.GameBalanceSettings(
                    out var originalSettings, 
                    settings =>
                    {
                        if (settings.SiegeWeaponHealth == newValue)
                        {
                            LoggingHelper.Info($"[SiegeWeaponSystem] SiegeWeaponHealth already set to {newValue}. No change made.");
                            return settings; 
                        }

                        settings.SiegeWeaponHealth = newValue;
                        LoggingHelper.Info($"[SiegeWeaponSystem] Updated Golem Health => {newValue}");
                        return settings; // Modified settings
                    }))
                {
                    return true;
                }
                else
                {
                    LoggingHelper.Error($"[SiegeWeaponSystem] Failed to apply GameBalanceSettings for SiegeWeaponHealth {newValue}.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                LoggingHelper.Error($"[SiegeWeaponSystem] Could not update Golem Health to {newValue}", ex);
                return false;
            }
        }

        public static SiegeWeaponHealth? GetSiegeWeaponHealth(ManualLogSource loggerContext = null)
        {
            try
            {
                if (VWorld.GameBalanceSettings(out var settings))
                {
                    return settings.SiegeWeaponHealth;
                }
                LoggingHelper.Error($"[SiegeWeaponSystem] Could not retrieve ServerGameBalanceSettings to get SiegeWeaponHealth.");
                return null;
            }
            catch (Exception ex)
            {
                LoggingHelper.Error($"[SiegeWeaponSystem] Error reading SGB.SiegeWeaponHealth", ex);
                return null;
            }
        }
    }
}