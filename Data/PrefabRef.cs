using Stunlock.Core;

namespace RaidForge.Data
{
    public readonly struct PrefabRef
    {
        public string Name { get; }
        public PrefabGUID Guid { get; }
        public int GuidHash => Guid.GuidHash;

        public PrefabRef(string name, int guidHash)
        {
            Name = name;
            Guid = new PrefabGUID(guidHash);
        }

        public override string ToString() => $"{Name} ({GuidHash})";
    }
}
