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
    }
}