using System;

namespace RaidForge.Data
{
    public class RaidWindow 
    {
        public DayOfWeek Day { get; set; }
        public TimeSpan Start { get; set; }
        public TimeSpan End { get; set; }
    }
}