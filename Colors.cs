using System;
using System.Drawing;

namespace TTMulti
{
    public static class Colors
    {
        public static Color LeftGroup => Color.FromArgb(Properties.Settings.Default.multiModeLeftBorderColor);
        public static Color RightGroup => Color.FromArgb(Properties.Settings.Default.multiModeRightBorderColor);
        public static Color AllGroups => Color.FromArgb(Properties.Settings.Default.mirrorModeBorderColor);
        public static readonly Color Individual = Color.Lime;
        public static readonly Color Multiclick = Color.IndianRed;
        public static readonly Color ChromaKey = Color.Fuchsia;
        public static readonly Color Focused = Color.DarkBlue;
        public static Color SwitchingMode => Color.FromArgb(Properties.Settings.Default.switchingModeColor);
        public static Color SwitchingSelected => Color.FromArgb(Properties.Settings.Default.switchingSelectedColor);
        public static Color SwitchingSwitched => Color.FromArgb(Properties.Settings.Default.switchingSwitchedColor);
        public static Color SwitchingMarkedForRemoval => Color.FromArgb(Properties.Settings.Default.switchingRemovedColor);
        
        /// <summary>
        /// Darken a color by a specified factor (0.0 = no change, 1.0 = black)
        /// </summary>
        public static Color Darken(Color color, float factor)
        {
            factor = Math.Max(0f, Math.Min(1f, factor)); // Clamp between 0 and 1
            int r = (int)(color.R * (1 - factor));
            int g = (int)(color.G * (1 - factor));
            int b = (int)(color.B * (1 - factor));
            return Color.FromArgb(color.A, r, g, b);
        }
    }
}