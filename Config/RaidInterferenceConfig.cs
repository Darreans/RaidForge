using BepInEx.Configuration;
using BepInEx.Logging;

namespace RaidForge.Config
{
	public static class RaidInterferenceConfig
	{
		public static ConfigFile ConfigFileInstance { get; private set; }

		public static ConfigEntry<bool> EnableRaidInterference { get; private set; }

		public static ConfigEntry<bool> DisableInterferenceForOfflineRaids { get; private set; }
		public static ConfigEntry<bool> DisableInterferenceForDecayingBases { get; private set; }
		public static ConfigEntry<bool> ExemptBearFormUsers { get; private set; }

		private const string SECTION_MAIN = "Raid Interference";

		public static void Initialize(ConfigFile configFile, ManualLogSource logger = null)
		{
			ConfigFileInstance = configFile;

			EnableRaidInterference = configFile.Bind(
				SECTION_MAIN,
				"EnableRaidInterference",
				false,
				"If true, vampires trying to interfer will be burned til their hp hits 0 or until they leave the base.(base must be breached first) ");

			DisableInterferenceForOfflineRaids = configFile.Bind(
				SECTION_MAIN,
				"DisableInterferenceForOfflineRaids",
				true,
				"If true, third parties will NOT get burned if the base being raided is offline (all defenders offline).");

			DisableInterferenceForDecayingBases = configFile.Bind(
				SECTION_MAIN,
				"DisableInterferenceForDecayingBases",
				true,
				"If true, third parties will NOT get burned if the base being raided is in a decaying state (castle heart is out of blood).");

			ExemptBearFormUsers = configFile.Bind(
				SECTION_MAIN,
				"ExemptBearFormUsers",
				true,
				"If true, players in Bear Form are immune to the  burn. Good for spectators.");

			logger?.LogInfo("[RaidInterferenceConfig] Initialized.");
		}
	}
}