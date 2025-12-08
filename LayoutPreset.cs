using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace TTMulti
{
    /// <summary>
    /// Represents the mode for setting region bounds
    /// </summary>
    internal enum LayoutRegionMode
    {
        Manual = 0,
        Display = 1
    }

    /// <summary>
    /// Represents a screen region for window placement
    /// </summary>
    internal class LayoutRegion
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public LayoutRegionMode Mode { get; set; }
        public int DisplayIndex { get; set; }

        public LayoutRegion()
        {
            X = 0;
            Y = 0;
            Width = 1920;
            Height = 1080;
            Mode = LayoutRegionMode.Manual;
            DisplayIndex = -1;
        }

        public LayoutRegion(int x, int y, int width, int height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
            Mode = LayoutRegionMode.Manual;
            DisplayIndex = -1;
        }

        public override string ToString()
        {
            return $"({X},{Y}) {Width}x{Height}";
        }
    }

    /// <summary>
    /// Represents a window layout preset with grid-based layout and hotkey configuration
    /// </summary>
    internal class LayoutPreset
    {
        /// <summary>
        /// Whether this preset is enabled
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// Number of columns in the grid
        /// </summary>
        public int Columns { get; set; }

        /// <summary>
        /// Number of rows in the grid
        /// </summary>
        public int Rows { get; set; }

        /// <summary>
        /// List of regions to fill with windows (filled sequentially)
        /// </summary>
        public List<LayoutRegion> Regions { get; set; }

        /// <summary>
        /// Hotkey code (Keys enum value)
        /// </summary>
        public int HotkeyCode { get; set; }

        /// <summary>
        /// Hotkey modifiers (Alt, Ctrl, Shift)
        /// </summary>
        public Win32.KeyModifiers HotkeyModifiers { get; set; }

        public LayoutPreset()
        {
            Enabled = false;
            Columns = 4;
            Rows = 2;
            Regions = new List<LayoutRegion> { new LayoutRegion() }; // Start with 1 region
            HotkeyCode = 0;
            HotkeyModifiers = Win32.KeyModifiers.None;
        }

        /// <summary>
        /// Calculate window size and positions for the given number of windows using grid layout across multiple regions
        /// </summary>
        public (Size windowSize, Point[] positions) CalculateGridLayout(int windowCount)
        {
            if (windowCount == 0 || Columns <= 0 || Rows <= 0 || Regions == null || Regions.Count == 0)
            {
                return (Size.Empty, new Point[0]);
            }

            // Windows 10+ adds invisible resize borders (~7px on each side)
            // We need to compensate for these to achieve perfect tiling
            const int borderCompensation = 7;

            List<Point> allPositions = new List<Point>();
            int windowsPlaced = 0;
            int slotsPerRegion = Columns * Rows;

            // Process each region sequentially
            foreach (var region in Regions)
            {
                if (windowsPlaced >= windowCount)
                    break;

                // Calculate window dimensions for this region
                int windowWidth = region.Width / Columns;
                int windowHeight = region.Height / Rows;

                // Calculate how many windows to place in this region
                int windowsInThisRegion = Math.Min(slotsPerRegion, windowCount - windowsPlaced);

                // Calculate positions for windows in this region
                for (int i = 0; i < windowsInThisRegion; i++)
                {
                    int row = i / Columns;
                    int col = i % Columns;

                    allPositions.Add(new Point(
                        region.X + (col * windowWidth) - borderCompensation, // Shift left by border
                        region.Y + (row * windowHeight)
                    ));
                }

                windowsPlaced += windowsInThisRegion;
            }

            // Use the first region's dimensions for window size (assumes all regions use same grid)
            var firstRegion = Regions[0];
            int baseWidth = firstRegion.Width / Columns;
            int baseHeight = firstRegion.Height / Rows;
            
            Size windowSize = new Size(
                baseWidth + (borderCompensation * 2 + 1), // Add to both left and right
                baseHeight + borderCompensation + 1       // Add to bottom (no top border)
            );

            return (windowSize, allPositions.ToArray());
        }

        /// <summary>
        /// Get display name for this preset's hotkey
        /// </summary>
        public string GetHotkeyDisplayName()
        {
            if (HotkeyCode == 0)
            {
                return "None";
            }

            string modifiers = "";
            if ((HotkeyModifiers & Win32.KeyModifiers.Alt) != 0)
                modifiers += "Alt+";
            if ((HotkeyModifiers & Win32.KeyModifiers.Control) != 0)
                modifiers += "Ctrl+";
            if ((HotkeyModifiers & Win32.KeyModifiers.Shift) != 0)
                modifiers += "Shift+";

            Keys key = (Keys)HotkeyCode;
            return modifiers + key.ToString();
        }

        public override string ToString()
        {
            if (!Enabled)
                return "(Disabled)";

            string regionInfo = Regions != null && Regions.Count > 0
                ? Regions[0].ToString()
                : "No regions";
                
            if (Regions != null && Regions.Count > 1)
                regionInfo += $" +{Regions.Count - 1} more";

            return $"{Columns}x{Rows} grid @ {regionInfo} [{GetHotkeyDisplayName()}]";
        }

        /// <summary>
        /// Serialize regions to a string format: "x,y,w,h,mode,displayIndex;x,y,w,h,mode,displayIndex;..."
        /// mode: 0 = Manual, 1 = Display
        /// displayIndex: index of selected display (-1 if Manual)
        /// </summary>
        public static string SerializeRegions(List<LayoutRegion> regions)
        {
            if (regions == null || regions.Count == 0)
                return "0,0,1920,1080,0,-1"; // Default single region (Manual mode)

            return string.Join(";", regions.Select(r => 
            {
                int mode = r.Mode == LayoutRegionMode.Display ? 1 : 0;
                return $"{r.X},{r.Y},{r.Width},{r.Height},{mode},{r.DisplayIndex}";
            }));
        }

        /// <summary>
        /// Deserialize regions from string format: "x,y,w,h;x,y,w,h;..." (old) or "x,y,w,h,mode,displayIndex;..." (new)
        /// </summary>
        public static List<LayoutRegion> DeserializeRegions(string regionsString)
        {
            if (string.IsNullOrWhiteSpace(regionsString))
                return new List<LayoutRegion> { new LayoutRegion() };

            try
            {
                var regions = new List<LayoutRegion>();
                var regionStrings = regionsString.Split(';');

                foreach (var regionStr in regionStrings)
                {
                    var parts = regionStr.Split(',');
                    if (parts.Length >= 4 &&
                        int.TryParse(parts[0], out int x) &&
                        int.TryParse(parts[1], out int y) &&
                        int.TryParse(parts[2], out int w) &&
                        int.TryParse(parts[3], out int h))
                    {
                        var region = new LayoutRegion(x, y, w, h);
                        
                        // Check if new format with mode and display index (6 parts)
                        if (parts.Length >= 6 &&
                            int.TryParse(parts[4], out int mode) &&
                            int.TryParse(parts[5], out int displayIndex))
                        {
                            region.Mode = mode == 1 ? LayoutRegionMode.Display : LayoutRegionMode.Manual;
                            region.DisplayIndex = displayIndex;
                        }
                        // Old format (4 parts) defaults to Manual mode
                        
                        regions.Add(region);
                    }
                }

                return regions.Count > 0 ? regions : new List<LayoutRegion> { new LayoutRegion() };
            }
            catch
            {
                return new List<LayoutRegion> { new LayoutRegion() };
            }
        }

        /// <summary>
        /// Load a layout preset from settings
        /// </summary>
        public static LayoutPreset LoadFromSettings(int presetNumber)
        {
            if (presetNumber < 1 || presetNumber > 4)
                throw new ArgumentOutOfRangeException(nameof(presetNumber), "Preset number must be between 1 and 4");

            var settings = Properties.Settings.Default;
            var preset = new LayoutPreset();

            switch (presetNumber)
            {
                case 1:
                    preset.Enabled = settings.layoutPreset1Enabled;
                    preset.Columns = settings.layoutPreset1Columns;
                    preset.Rows = settings.layoutPreset1Rows;
                    preset.Regions = DeserializeRegions(settings.layoutPreset1Regions);
                    preset.HotkeyCode = settings.layoutPreset1HotkeyCode;
                    preset.HotkeyModifiers = (Win32.KeyModifiers)settings.layoutPreset1HotkeyModifiers;
                    break;
                case 2:
                    preset.Enabled = settings.layoutPreset2Enabled;
                    preset.Columns = settings.layoutPreset2Columns;
                    preset.Rows = settings.layoutPreset2Rows;
                    preset.Regions = DeserializeRegions(settings.layoutPreset2Regions);
                    preset.HotkeyCode = settings.layoutPreset2HotkeyCode;
                    preset.HotkeyModifiers = (Win32.KeyModifiers)settings.layoutPreset2HotkeyModifiers;
                    break;
                case 3:
                    preset.Enabled = settings.layoutPreset3Enabled;
                    preset.Columns = settings.layoutPreset3Columns;
                    preset.Rows = settings.layoutPreset3Rows;
                    preset.Regions = DeserializeRegions(settings.layoutPreset3Regions);
                    preset.HotkeyCode = settings.layoutPreset3HotkeyCode;
                    preset.HotkeyModifiers = (Win32.KeyModifiers)settings.layoutPreset3HotkeyModifiers;
                    break;
                case 4:
                    preset.Enabled = settings.layoutPreset4Enabled;
                    preset.Columns = settings.layoutPreset4Columns;
                    preset.Rows = settings.layoutPreset4Rows;
                    preset.Regions = DeserializeRegions(settings.layoutPreset4Regions);
                    preset.HotkeyCode = settings.layoutPreset4HotkeyCode;
                    preset.HotkeyModifiers = (Win32.KeyModifiers)settings.layoutPreset4HotkeyModifiers;
                    break;
            }

            return preset;
        }

        /// <summary>
        /// Save a layout preset to settings
        /// </summary>
        public void SaveToSettings(int presetNumber)
        {
            if (presetNumber < 1 || presetNumber > 4)
                throw new ArgumentOutOfRangeException(nameof(presetNumber), "Preset number must be between 1 and 4");

            var settings = Properties.Settings.Default;

            switch (presetNumber)
            {
                case 1:
                    settings.layoutPreset1Enabled = Enabled;
                    settings.layoutPreset1Columns = Columns;
                    settings.layoutPreset1Rows = Rows;
                    settings.layoutPreset1Regions = SerializeRegions(Regions);
                    settings.layoutPreset1HotkeyCode = HotkeyCode;
                    settings.layoutPreset1HotkeyModifiers = (int)HotkeyModifiers;
                    break;
                case 2:
                    settings.layoutPreset2Enabled = Enabled;
                    settings.layoutPreset2Columns = Columns;
                    settings.layoutPreset2Rows = Rows;
                    settings.layoutPreset2Regions = SerializeRegions(Regions);
                    settings.layoutPreset2HotkeyCode = HotkeyCode;
                    settings.layoutPreset2HotkeyModifiers = (int)HotkeyModifiers;
                    break;
                case 3:
                    settings.layoutPreset3Enabled = Enabled;
                    settings.layoutPreset3Columns = Columns;
                    settings.layoutPreset3Rows = Rows;
                    settings.layoutPreset3Regions = SerializeRegions(Regions);
                    settings.layoutPreset3HotkeyCode = HotkeyCode;
                    settings.layoutPreset3HotkeyModifiers = (int)HotkeyModifiers;
                    break;
                case 4:
                    settings.layoutPreset4Enabled = Enabled;
                    settings.layoutPreset4Columns = Columns;
                    settings.layoutPreset4Rows = Rows;
                    settings.layoutPreset4Regions = SerializeRegions(Regions);
                    settings.layoutPreset4HotkeyCode = HotkeyCode;
                    settings.layoutPreset4HotkeyModifiers = (int)HotkeyModifiers;
                    break;
            }

            settings.Save();
        }
    }
}

