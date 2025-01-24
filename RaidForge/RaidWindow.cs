using System;

namespace RaidForge
{
    /// <summary>
    /// Simple data class for day-of-week scheduling.
    /// </summary>
    public class Raidwindow
    {
        public DayOfWeek Day { get; set; }
        public TimeSpan Start { get; set; }
        public TimeSpan End { get; set; }
    }
}
