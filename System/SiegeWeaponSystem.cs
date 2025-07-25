﻿using System;
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
                            return settings;
                        }
                        settings.SiegeWeaponHealth = newValue;
                        return settings;
                    }))
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
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
                return null;
            }
            catch (Exception ex)
            {
                return null;
            }
        }
    }
}