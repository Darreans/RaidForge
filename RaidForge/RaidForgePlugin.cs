using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using VampireCommandFramework;
using HarmonyLib;
using Bloodstone.API;

namespace RaidForge
{
    [BepInPlugin("RaidForge", "RaidForge Mod", "1.0.0")]
    [BepInDependency("gg.deca.VampireCommandFramework")]
    [BepInDependency("gg.deca.Bloodstone")]
    [Reloadable]
    public class RaidForgePlugin : BasePlugin
    {
        public static RaidForgePlugin Instance { get; private set; }
        public static new ManualLogSource Logger;

        private Harmony _harmony;
        
        public override void Load()
        {
            Instance = this;
            Logger = Log;

            _harmony = new Harmony("raidforge.plugin");
            _harmony.PatchAll();


            CommandRegistry.RegisterAll();

            

            Logger.LogInfo("[RaidForge] Loaded.");
        }

        public override bool Unload()
        {
          
            CommandRegistry.UnregisterAssembly();
            _harmony?.UnpatchSelf();

            return true;
        }
    }
}
