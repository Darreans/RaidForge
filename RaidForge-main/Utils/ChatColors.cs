using System;

namespace RaidForge.Utils
{
    public static class ChatColors
    {
        public const string Error = "#FF4136";     
        public const string Success = "#2ECC40";   
        public const string Info = "#FFFFFF";      
        public const string Warning = "#F00534";   
        public const string Highlight = "#7FDBFF"; 
        public const string Accent = "#FFDC00";    
        public const string Muted = "#AAAAAA";     

        public static string Format(string message, string colorHex)
        {
            if (string.IsNullOrEmpty(colorHex))
            {
                return message; 
            }
            return $"<color={colorHex}>{message}</color>";
        }

        public static string ErrorText(string message) => Format(message, Error);
        public static string SuccessText(string message) => Format(message, Success);
        public static string InfoText(string message) => Format(message, Info);
        public static string WarningText(string message) => Format(message, Warning);
        public static string HighlightText(string message) => Format(message, Highlight);
        public static string AccentText(string message) => Format(message, Accent);
        public static string MutedText(string message) => Format(message, Muted);
    }
}