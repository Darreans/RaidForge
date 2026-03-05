using BepInEx.Configuration;

namespace RaidForge.Config
{
	public static class MapIconsConfig
	{
		public static ConfigEntry<bool> EnableOfflineRaidMapIcon;
		public static ConfigEntry<int> OfflineRaidMapIconPrefabGuid;

		public static ConfigEntry<bool> EnableDecayRaidMapIcon;
		public static ConfigEntry<int> DecayRaidMapIconPrefabGuid;

		public static ConfigEntry<int> RaidMapIconTimeoutSeconds;

		public static void Initialize(ConfigFile config)
		{
			EnableOfflineRaidMapIcon = config.Bind("MapIcons", "EnableOfflineRaidMapIcon", true, "Display a map icon on the map when an offline base is being raided.");
			OfflineRaidMapIconPrefabGuid = config.Bind("MapIcons", "OfflineRaidMapIconPrefabGuid", -2066471106, "The PrefabGUID for the map icon to display for offline raids (Default is crossed swords).");

			EnableDecayRaidMapIcon = config.Bind("MapIcons", "EnableDecayRaidMapIcon", true, "Display a map icon on the map when a decayed base is being raided.");
			DecayRaidMapIconPrefabGuid = config.Bind("MapIcons", "DecayRaidMapIconPrefabGuid", -2066471106, "The PrefabGUID for the map icon to display for decay raids.");

			RaidMapIconTimeoutSeconds = config.Bind("MapIcons", "RaidMapIconTimeoutSeconds", 300, "How many seconds (default 300 = 5 mins) the map icon remains after the last hit.");
		}
	}
}