using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using VampireCommandFramework;
using System;
using Bloodstone.API;
using Bloodstone.Hooks; 
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

        private static float _timeAccumulator = 0f;
        private static readonly float TICK_INTERVAL = 0.1f;
        

        public override void Load()
        {
            Instance = this;
            Logger = Log;
            Logger.LogInfo("[RaidForge] Plugin is loading...");

            Raidschedule.Initialize(Config);
            Config.Save();

            CommandRegistry.RegisterAll();

            RaidtimeManager.Initialize();

            GameFrame.Initialize();
            GameFrame.OnUpdate += GameFrame_OnUpdate;

            Logger.LogInfo("[RaidForge] Plugin load finished using Bloodstone GameFrame updates.");
        }

        public override bool Unload()
        {
            Logger.LogInfo("[RaidForge] Unloading plugin...");

            GameFrame.OnUpdate -= GameFrame_OnUpdate;
            GameFrame.Uninitialize();

            CommandRegistry.UnregisterAssembly();

            RaidtimeManager.Dispose();

            return true;
        }

        private void GameFrame_OnUpdate()
        {
            _timeAccumulator += UnityEngine.Time.deltaTime;

            if (_timeAccumulator >= 5f)
            {
                _timeAccumulator = 0f;
                RaidtimeManager.OnServerTick();
            }
        }
    }
}
