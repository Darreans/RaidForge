using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using VampireCommandFramework;
using HarmonyLib;
using System;
// For chat reload
using Bloodstone.API;
using Bloodstone.Hooks;

namespace RaidForge
{
    [BepInPlugin("RaidForge", "RaidForge Mod", "1.0.0")]
    [BepInDependency("gg.deca.VampireCommandFramework")]
    [BepInDependency("gg.deca.Bloodstone")]
    public class RaidForgePlugin : BasePlugin
    {
        public static RaidForgePlugin Instance { get; private set; }
        public static new ManualLogSource Logger;

        private Harmony _harmony;

        public override void Load()
        {
            Instance = this;
            Logger = Log;
            Logger.LogInfo("[RaidForge] Plugin is loading...");

            // 1) Create/load config
            Raidschedule.Initialize(Config);
            _ = Raidschedule.OverrideMode.Value;
            _ = Raidschedule.RaidCheckInterval.Value;
            Config.Save();

            // 2) Register commands (RaidCommand.cs)
            CommandRegistry.RegisterAll();

            // 3) Setup Harmony for the VivoxPatch
            _harmony = new Harmony("com.myserver.RaidForge");
            _harmony.PatchAll();

            // 4) Hook chat for "!reload"
            Chat.OnChatMessage += HandleReloadCommand;

            Logger.LogInfo("[RaidForge] Plugin load finished. Using Vivox OnUpdate patch for scheduling.");
        }

        public override bool Unload()
        {
            Logger.LogInfo("[RaidForge] Unloading plugin...");

            // Unhook
            Chat.OnChatMessage -= HandleReloadCommand;

            // Unregister commands & unpatch
            CommandRegistry.UnregisterAssembly();
            _harmony?.UnpatchSelf();

            return true;
        }

        /// <summary>
        /// Reloads the config on-the-fly when an admin types "!reload" in chat.
        /// Ensures ForceOn/ForceOff changes apply immediately.
        /// </summary>
        private void HandleReloadCommand(VChatEvent ev)
        {
            if (ev.Message == "!reload" && ev.User.IsAdmin)
            {
                Logger.LogInfo("[RaidForge] Reload command received...");
                try
                {
                    Config.Reload();
                    Config.Save();

                    // Re-init schedule
                    Raidschedule.Initialize(Config);
                    Raidschedule.LoadFromConfig();

                    // IMPORTANT: apply ForceOn/ForceOff logic immediately
                    RaidtimeManager.ReloadFromConfig(true);

                    ev.User.SendSystemMessage("<color=#00FF00>RaidForge config reloaded successfully.</color>");
                    Logger.LogInfo("[RaidForge] Config reloaded via !reload command.");
                }
                catch (Exception ex)
                {
                    ev.User.SendSystemMessage($"<color=#FF0000>Failed to reload config:</color> {ex.Message}");
                    Logger.LogError($"[RaidForge] Error reloading config: {ex}");
                }
            }
        }
    }
}
