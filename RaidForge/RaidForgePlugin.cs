using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using VampireCommandFramework;
using System;
using Bloodstone.API;
using Bloodstone.Hooks; // for GameFrame
using HarmonyLib;

namespace RaidForge
{
    [BepInPlugin("RaidForge", "RaidForge Mod", "1.0.0")]
    [BepInDependency("gg.deca.VampireCommandFramework")]
    [BepInDependency("gg.deca.Bloodstone")]
    public class RaidForgePlugin : BasePlugin
    {
        public static RaidForgePlugin Instance { get; private set; }
        public static new ManualLogSource Logger;

        // We remove the old Harmony reference for VivoxPatch; 
        // now we'll do OnUpdate with GameFrame:
        private static float _timeAccumulator = 0f;
        private static readonly float TICK_INTERVAL = 0.1f;
        // We'll run our "check" logic every 0.1s so we can 
        // accumulate up to 5s for the actual raid logic.

        public override void Load()
        {
            Instance = this;
            Logger = Log;
            Logger.LogInfo("[RaidForge] Plugin is loading...");

            // Setup your config (day-of-week times, override mode, etc.)
            Raidschedule.Initialize(Config);
            // We remove the old user config for RaidCheckInterval, so no references here:
            Config.Save();

            // Register commands (RaidCommand)
            CommandRegistry.RegisterAll();

            // Initialize your manager so it can reset
            RaidtimeManager.Initialize();

            // Hook Bloodstone’s GameFrame for updates
            GameFrame.Initialize();
            GameFrame.OnUpdate += GameFrame_OnUpdate;

            Logger.LogInfo("[RaidForge] Plugin load finished using Bloodstone GameFrame updates.");
        }

        public override bool Unload()
        {
            Logger.LogInfo("[RaidForge] Unloading plugin...");

            // Unsubscribe from GameFrame
            GameFrame.OnUpdate -= GameFrame_OnUpdate;
            GameFrame.Uninitialize();

            // Unregister commands
            CommandRegistry.UnregisterAssembly();

            RaidtimeManager.Dispose();

            return true;
        }

        private void GameFrame_OnUpdate()
        {
            // We'll accumulate time in real seconds
            _timeAccumulator += UnityEngine.Time.deltaTime;

            // Once we've accumulated 5 seconds, we run the scheduling check
            if (_timeAccumulator >= 5f)
            {
                _timeAccumulator = 0f;
                RaidtimeManager.OnServerTick();
            }
        }
    }
}
