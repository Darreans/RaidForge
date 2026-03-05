using BepInEx.Configuration;

namespace RaidForge.Config
{
	public static class ShardConfig
	{
		public static ConfigEntry<int> MaxAllowedShardsPerType { get; private set; }
		public static ConfigEntry<bool> DisableOrpForShardHolders { get; private set; }

		public static void Initialize(ConfigFile config)
		{
			MaxAllowedShardsPerType = config.Bind(
				"Soul Shards",
				"MaxAllowedShardsPerType",
				1,
				"The maximum unique number of the same soul shard type allowed on the server. You must change this if you allow more than one type of the same soul shard."
			);

			DisableOrpForShardHolders = config.Bind(
				"Soul Shards",
				"DisableOrpForShardHolders",
				true,
				"If true, players or clans holding a soul shard will NOT receive Offline Raid Protection. If false, they get ORP like everyone else."
			);
		}
	}
}