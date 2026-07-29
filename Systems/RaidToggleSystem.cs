using System;
using ProjectM;
using RaidForge.Services;
using RaidForge.Utils;

namespace RaidForge.Systems
{
    public static class RaidToggleSystem
    {
        private static readonly TimeSpan RaidStateCacheDuration = TimeSpan.FromSeconds(1);
        private static bool _cachedRaidsEnabled = false;
        private static DateTime _raidStateCacheExpiresUtc = DateTime.MinValue;

        public static bool AreRaidsEnabled()
        {
            DateTime nowUtc = DateTime.UtcNow;

            if (nowUtc < _raidStateCacheExpiresUtc)
            {
                return _cachedRaidsEnabled;
            }

            RefreshRaidStateCache(nowUtc);
            return _cachedRaidsEnabled;
        }

        public static bool AreRaidsEnabledLive()
        {
            RefreshRaidStateCache(DateTime.UtcNow);
            return _cachedRaidsEnabled;
        }

        public static bool EnableRaids()
        {
            return SetCastleDamageMode(CastleDamageMode.Always);
        }

        public static bool DisableRaids()
        {
            return SetCastleDamageMode(CastleDamageMode.TimeRestricted);
        }

        private static bool SetCastleDamageMode(CastleDamageMode newMode)
        {
            try
            {
                if (!SetServerGameSettingsCastleDamageMode(newMode, out CastleDamageMode serverSettingsPreviousMode))
                {
                    return false;
                }

                CastleDamageMode balancePreviousMode = default;
                if (VWorld.GameBalanceSettings(
                    out var currentSettings,
                    settings =>
                    {
                        balancePreviousMode = settings.CastleDamageMode;
                        if (settings.CastleDamageMode == newMode)
                        {
                            return settings;
                        }

                        settings.CastleDamageMode = newMode;
                        return settings;
                    }))
                {
                    SetRaidStateCache(newMode == CastleDamageMode.Always);
                    RaidMapIconService.MarkPersistentStateIconsDirty();
                    RaidMapIconService.ProcessCleanup();
                    LoggingHelper.Info($"[RaidToggleSystem] CastleDamageMode set to {newMode} (ServerGameSettings: {serverSettingsPreviousMode} -> {newMode}, BalanceSettings: {balancePreviousMode} -> {currentSettings.CastleDamageMode}).");
                    return true;
                }
                else
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
                LoggingHelper.Error($"[RaidToggleSystem] Failed to set CastleDamageMode to {newMode}.", ex);
                return false;
            }
        }

        private static bool SetServerGameSettingsCastleDamageMode(CastleDamageMode newMode, out CastleDamageMode previousMode)
        {
            previousMode = default;

            try
            {
                if (!VWorld.IsServerWorldReady())
                {
                    LoggingHelper.Error("[RaidToggleSystem] Server world not ready while setting ServerGameSettings CastleDamageMode.");
                    return false;
                }

                var settingsSystem = VWorld.Server.GetExistingSystemManaged<ServerGameSettingsSystem>();
                if (settingsSystem == null)
                {
                    LoggingHelper.Error("[RaidToggleSystem] ServerGameSettingsSystem not found while setting CastleDamageMode.");
                    return false;
                }

                var serverSettings = settingsSystem._Settings;
                previousMode = serverSettings.CastleDamageMode;

                if (serverSettings.CastleDamageMode != newMode)
                {
                    serverSettings.CastleDamageMode = newMode;
                    settingsSystem._Settings = serverSettings;
                }

                return true;
            }
            catch (Exception ex)
            {
                LoggingHelper.Error("[RaidToggleSystem] Failed to set ServerGameSettings CastleDamageMode.", ex);
                return false;
            }
        }

        private static void RefreshRaidStateCache(DateTime nowUtc)
        {
            if (VWorld.GameBalanceSettings(out var currentSettings))
            {
                _cachedRaidsEnabled = currentSettings.CastleDamageMode == CastleDamageMode.Always;
            }

            _raidStateCacheExpiresUtc = nowUtc.Add(RaidStateCacheDuration);
        }

        private static void SetRaidStateCache(bool raidsEnabled)
        {
            _cachedRaidsEnabled = raidsEnabled;
            _raidStateCacheExpiresUtc = DateTime.UtcNow.Add(RaidStateCacheDuration);
        }
    }
}
