using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Deployment;
using System.Deployment.Application;
using TTMulti.Controls;
using System.Net;
using System.Threading;
using System.Diagnostics;
using System.Reflection;
using System.IO;

namespace TTMulti.Forms
{
    public partial class OptionsDlg : Form
    {
        private bool loaded = false;

        // Layout preset controls
        private TabPage layoutTabPage;
        private List<LayoutPresetControls> presetControls = new List<LayoutPresetControls>();
        private Button addPresetButton;

        // Helper class to hold controls for one region
        private class RegionControls
        {
            public Panel Panel;
            public NumericUpDown XNumeric;
            public NumericUpDown YNumeric;
            public NumericUpDown WidthNumeric;
            public NumericUpDown HeightNumeric;
            public Button RemoveButton;
            public ComboBox DisplayComboBox;
            public RadioButton ManualRadioButton;
            public RadioButton DisplayRadioButton;
            private bool isUpdatingFromDisplay = false;
            
            public bool IsManualMode => ManualRadioButton?.Checked ?? true;
            
            public void UpdateFromDisplay(Screen screen)
            {
                if (isUpdatingFromDisplay) return;
                isUpdatingFromDisplay = true;
                try
                {
                    // Use WorkingArea instead of Bounds to exclude taskbar
                    Rectangle workingArea = screen.WorkingArea;
                    XNumeric.Value = workingArea.X;
                    YNumeric.Value = workingArea.Y;
                    WidthNumeric.Value = workingArea.Width;
                    HeightNumeric.Value = workingArea.Height;
                }
                finally
                {
                    isUpdatingFromDisplay = false;
                }
            }
        }

        // Helper class to hold controls for one preset
        private class LayoutPresetControls
        {
            public int PresetNumber;
            public GroupBox GroupBox;
            public CheckBox EnabledCheckBox;
            public NumericUpDown ColumnsNumeric;
            public NumericUpDown RowsNumeric;
            public Panel RegionsPanel;
            public Button AddRegionButton;
            public List<RegionControls> RegionControlsList;
            public KeyPicker HotkeyPicker;
            public CheckBox AltCheckBox;
            public CheckBox CtrlCheckBox;
            public CheckBox ShiftCheckBox;
        }

        public OptionsDlg()
        {
            InitializeComponent();
            this.Icon = Properties.Resources.icon;
            CreateLayoutPresetsUI();
        }

        // https://docs.microsoft.com/en-us/visualstudio/deployment/how-to-check-for-application-updates-programmatically-using-the-clickonce-deployment-api?view=vs-2015
        private void checkUpdates_ClickOnce()
        {
            UpdateCheckInfo info = null;
            ApplicationDeployment ad = ApplicationDeployment.CurrentDeployment;

            try
            {
                info = ad.CheckForDetailedUpdate();
            }
            catch (DeploymentDownloadException dde)
            {
                MessageBox.Show("The new version of the application cannot be downloaded at this time. \n\nPlease check your network connection, or try again later. Error: " + dde.Message);
                return;
            }
            catch (InvalidDeploymentException ide)
            {
                MessageBox.Show("Cannot check for a new version of the application. The ClickOnce deployment is corrupt. Please redeploy the application and try again. Error: " + ide.Message);
                return;
            }
            catch (InvalidOperationException ioe)
            {
                MessageBox.Show("This application cannot be updated. It is likely not a ClickOnce application. Error: " + ioe.Message);
                return;
            }

            if (info.UpdateAvailable)
            {
                Boolean doUpdate = true;

                if (!info.IsUpdateRequired)
                {
                    DialogResult dr = MessageBox.Show("An update is available. Would you like to update the application now?", "Update Available", MessageBoxButtons.OKCancel);
                    if (!(DialogResult.OK == dr))
                    {
                        doUpdate = false;
                    }
                }
                else
                {
                    // Display a message that the app MUST reboot. Display the minimum required version.
                    MessageBox.Show("This application has detected a mandatory update from your current " +
                        "version to version " + info.MinimumRequiredVersion.ToString() +
                        ". The application will now install the update and restart.",
                        "Update Available", MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }

                if (doUpdate)
                {
                    try
                    {
                        ad.Update();
                        MessageBox.Show("The application has been upgraded, and will now restart.");
                        Application.Restart();
                    }
                    catch (DeploymentDownloadException dde)
                    {
                        MessageBox.Show("Cannot install the latest version of the application. \n\nPlease check your network connection, or try again later. Error: " + dde);
                        return;
                    }
                }
            }
            else
            {
                MessageBox.Show("There are no updates available at the moment.", "No Update Available", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void checkUpdates_Standalone()
        {
            string latestVersion = null;

            Thread fetchVersionThread = new Thread(() =>
            {
                try
                {
                    HttpWebRequest webRequest = (HttpWebRequest)WebRequest.Create(Properties.Settings.Default.homepageUrl + "/version.txt");

                    using (HttpWebResponse response = (HttpWebResponse)webRequest.GetResponse())
                    using (StreamReader sr = new StreamReader(response.GetResponseStream()))
                    {
                        latestVersion = sr.ReadToEnd();
                    }
                }
                catch { }
            })
            { IsBackground = true };

            fetchVersionThread.Start();

            this.Enabled = false;
            this.UseWaitCursor = true;

            Stopwatch sw = Stopwatch.StartNew();
            
            while (fetchVersionThread.IsAlive && sw.ElapsedMilliseconds < 5000)
            {
                Application.DoEvents();
                Thread.Sleep(10);
            }

            if (fetchVersionThread.IsAlive)
            {
                fetchVersionThread.Abort();
                latestVersion = null;
            }

            if (!string.IsNullOrEmpty(latestVersion))
            {
                if (Application.ProductVersion != latestVersion)
                {
                    MessageBox.Show(string.Format("An update is available to version {0}. Click the About button to view the homepage.", latestVersion), "Update available");
                }
                else
                {
                    MessageBox.Show("No updates available.");
                }
            }
            else
            {
                MessageBox.Show("Could not check for a new version of the application.", "Error");
            }

            this.UseWaitCursor = false;
            this.Enabled = true;
        }

        // Auto-find controls
        private GroupBox autoFindGroupBox;
        private KeyPicker autoFindKeyPicker;
        private CheckBox autoFindAltCheckBox;
        private CheckBox autoFindCtrlCheckBox;
        private CheckBox autoFindShiftCheckBox;
        private TextBox autoFindExecutablesTextBox;
        private Label autoFindExecutablesLabel;

        // Layout priority toggle controls
        private GroupBox layoutPriorityGroupBox;
        private KeyPicker layoutPriorityKeyPicker;
        private CheckBox layoutPriorityAltCheckBox;
        private CheckBox layoutPriorityCtrlCheckBox;
        private CheckBox layoutPriorityShiftCheckBox;
        private Label layoutPriorityLabel;

        // Switching Mode controls
        private GroupBox switchingModeGroupBox;
        private CheckBox switchingModeEnabledCheckBox;
        private ComboBox switchingModeSwitchComboBox;
        private KeyPicker switchingModeSwitchKeyPicker;
        private KeyPicker switchingModeRemoveKeyPicker;
        private Label switchingModeDescriptionLabel;

        private void OptionsDlg_Load(object sender, EventArgs e)
        {
            controlsPicker.KeyMappings = Properties.SerializedSettings.Default.Bindings;

            // Load layout presets (only load presets that exist in the UI)
            for (int i = 0; i < presetControls.Count; i++)
            {
                LoadLayoutPreset(i + 1);
            }

            CreateAutoFindTab();
            LoadAutoFindSettings();

            CreateLayoutPriorityUI();
            LoadLayoutPrioritySettings();

            CreateSwitchingModeTab();
            LoadSwitchingModeSettings();

            loaded = true;
        }

        private void CreateAutoFindTab()
        {
            // Create a new tab page for Auto-Find
            var autoFindTab = new TabPage("Auto-Find");
            autoFindTab.AutoScroll = true;
            autoFindTab.Padding = new Padding(10);
            
            // Add the tab to the tab control
            tabControl1.TabPages.Add(autoFindTab);

            // Create main group box
            autoFindGroupBox = new GroupBox
            {
                Text = "Auto-Find Windows",
                Location = new Point(10, 10),
                Size = new Size(720, 200),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            // Description label
            var descLabel = new Label
            {
                Text = "Automatically find and assign windows from recognized game executables. " +
                       "Windows are assigned sequentially:\n Group 1 Left, Group 1 Right, Group 2 Left, Group 2 Right, etc.",
                Location = new Point(10, 25),
                Size = new Size(700, 50),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            autoFindGroupBox.Controls.Add(descLabel);

            // Executables label
            autoFindExecutablesLabel = new Label
            {
                Text = "Executables (semicolon-separated):",
                Location = new Point(10, 85),
                Size = new Size(300, 20)
            };
            autoFindGroupBox.Controls.Add(autoFindExecutablesLabel);

            // Executables text box
            autoFindExecutablesTextBox = new TextBox
            {
                Location = new Point(10, 105),
                Size = new Size(500, 23),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            autoFindGroupBox.Controls.Add(autoFindExecutablesTextBox);

            // Hotkey section label
            var hotkeySectionLabel = new Label
            {
                Text = "Hotkey Configuration:",
                Location = new Point(10, 140),
                Size = new Size(200, 20),
                Font = new Font(autoFindGroupBox.Font, FontStyle.Bold)
            };
            autoFindGroupBox.Controls.Add(hotkeySectionLabel);

            // Hotkey label
            var hotkeyLabel = new Label
            {
                Text = "Hotkey:",
                Location = new Point(10, 165),
                Size = new Size(60, 20)
            };
            autoFindGroupBox.Controls.Add(hotkeyLabel);

            // Hotkey picker
            autoFindKeyPicker = new KeyPicker
            {
                Location = new Point(80, 163),
                Size = new Size(120, 23)
            };
            autoFindGroupBox.Controls.Add(autoFindKeyPicker);

            // Modifier checkboxes
            autoFindAltCheckBox = new CheckBox
            {
                Text = "Alt",
                Location = new Point(210, 165),
                Size = new Size(50, 20)
            };
            autoFindGroupBox.Controls.Add(autoFindAltCheckBox);

            autoFindCtrlCheckBox = new CheckBox
            {
                Text = "Ctrl",
                Location = new Point(270, 165),
                Size = new Size(50, 20)
            };
            autoFindGroupBox.Controls.Add(autoFindCtrlCheckBox);

            autoFindShiftCheckBox = new CheckBox
            {
                Text = "Shift",
                Location = new Point(330, 165),
                Size = new Size(60, 20)
            };
            autoFindGroupBox.Controls.Add(autoFindShiftCheckBox);

            // Add group box to tab
            autoFindTab.Controls.Add(autoFindGroupBox);
        }

        private void CreateLayoutPriorityUI()
        {
            // Get the Hotkeys tab (tabPage3)
            var hotkeysTab = tabControl1.TabPages.Cast<TabPage>().FirstOrDefault(t => t.Text == "Hotkeys");
            if (hotkeysTab == null)
                return;

            // Find the tableLayoutPanel2 in the Hotkeys tab
            var tableLayoutPanel = hotkeysTab.Controls.OfType<TableLayoutPanel>().FirstOrDefault();
            if (tableLayoutPanel == null)
                return;

            // Check if layout priority UI already exists
            if (layoutPriorityGroupBox != null && tableLayoutPanel.Controls.Contains(layoutPriorityGroupBox))
                return;

            // Wrap TableLayoutPanel in a scrollable Panel to enable scrolling
            // Check if it's already wrapped
            Panel scrollPanel = hotkeysTab.Controls.OfType<Panel>().FirstOrDefault(p => p.Controls.Contains(tableLayoutPanel));
            
            if (scrollPanel == null)
            {
                // Create a scrollable panel
                scrollPanel = new Panel
                {
                    Dock = DockStyle.Fill,
                    AutoScroll = true,
                    Name = "hotkeysScrollPanel"
                };
                
                // Remove TableLayoutPanel from tab
                hotkeysTab.Controls.Remove(tableLayoutPanel);
                
                // Change TableLayoutPanel properties to allow growth
                tableLayoutPanel.Dock = DockStyle.Top;
                tableLayoutPanel.AutoSize = true;
                tableLayoutPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                
                // Add TableLayoutPanel to scroll panel
                scrollPanel.Controls.Add(tableLayoutPanel);
                
                // Add scroll panel to tab
                hotkeysTab.Controls.Add(scrollPanel);
                scrollPanel.BringToFront();
            }
            else
            {
                // Ensure the scroll panel has AutoScroll enabled
                scrollPanel.AutoScroll = true;
            }

            // Create group box for layout priority toggle
            layoutPriorityGroupBox = new GroupBox
            {
                Text = "Layout Priority Toggle Hotkey:",
                Dock = DockStyle.Top,
                Padding = new Padding(4)
            };

            // Description label
            layoutPriorityLabel = new Label
            {
                Text = "Toggles between two window ordering modes for layout placement:\n" +
                       "• Pairs First: Group 1 Pair 1 Left, Group 1 Pair 1 Right, Group 1 Pair 2 Left, Group 1 Pair 2 Right\n" +
                       "• Lefts First: Group 1 Pair 1 Left, Group 1 Pair 2 Left, Group 1 Pair 1 Right, Group 1 Pair 2 Right\n" +
                       "When toggled, automatically reapplies the last used layout preset.",
                Location = new Point(8, 20),
                Size = new Size(712, 60),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            layoutPriorityGroupBox.Controls.Add(layoutPriorityLabel);

            // Hotkey label
            var hotkeyLabel = new Label
            {
                Text = "Hotkey:",
                Location = new Point(8, 85),
                Size = new Size(60, 20)
            };
            layoutPriorityGroupBox.Controls.Add(hotkeyLabel);

            // Hotkey picker
            layoutPriorityKeyPicker = new KeyPicker
            {
                Location = new Point(70, 83),
                Size = new Size(120, 23)
            };
            layoutPriorityGroupBox.Controls.Add(layoutPriorityKeyPicker);

            // Modifier checkboxes
            layoutPriorityAltCheckBox = new CheckBox
            {
                Text = "Alt",
                Location = new Point(200, 85),
                Size = new Size(50, 20)
            };
            layoutPriorityGroupBox.Controls.Add(layoutPriorityAltCheckBox);

            layoutPriorityCtrlCheckBox = new CheckBox
            {
                Text = "Ctrl",
                Location = new Point(260, 85),
                Size = new Size(50, 20)
            };
            layoutPriorityGroupBox.Controls.Add(layoutPriorityCtrlCheckBox);

            layoutPriorityShiftCheckBox = new CheckBox
            {
                Text = "Shift",
                Location = new Point(320, 85),
                Size = new Size(60, 20)
            };
            layoutPriorityGroupBox.Controls.Add(layoutPriorityShiftCheckBox);

            // Set the group box height
            layoutPriorityGroupBox.Height = 120;

            // Increase row count if needed and add to tableLayoutPanel at row 4 (index 3)
            // Row 0: Mode/Activate Hotkey, Row 1: Multi-Click Hotkey, Row 2: Zero Power Throw Hotkey
            if (tableLayoutPanel.RowCount < 5)
            {
                // Change the last row (index 3) from Absolute to AutoSize if it exists
                if (tableLayoutPanel.RowCount == 4 && tableLayoutPanel.RowStyles.Count > 3)
                {
                    tableLayoutPanel.RowStyles[3] = new RowStyle(SizeType.AutoSize);
                }
                tableLayoutPanel.RowCount = 5;
                // Add a new row style for the layout priority group box
                tableLayoutPanel.RowStyles.Add(new RowStyle());
            }
            tableLayoutPanel.Controls.Add(layoutPriorityGroupBox, 0, 3);
        }

        private void LoadLayoutPrioritySettings()
        {
            if (layoutPriorityKeyPicker == null)
                return;

            layoutPriorityKeyPicker.ChosenKey = (Keys)Properties.Settings.Default.layoutPriorityToggleKeyCode;
            layoutPriorityAltCheckBox.Checked = ((Win32.KeyModifiers)Properties.Settings.Default.layoutPriorityToggleKeyModifiers & Win32.KeyModifiers.Alt) != 0;
            layoutPriorityCtrlCheckBox.Checked = ((Win32.KeyModifiers)Properties.Settings.Default.layoutPriorityToggleKeyModifiers & Win32.KeyModifiers.Control) != 0;
            layoutPriorityShiftCheckBox.Checked = ((Win32.KeyModifiers)Properties.Settings.Default.layoutPriorityToggleKeyModifiers & Win32.KeyModifiers.Shift) != 0;
        }

        private void SaveLayoutPrioritySettings()
        {
            if (layoutPriorityKeyPicker == null)
                return;

            Properties.Settings.Default.layoutPriorityToggleKeyCode = (int)layoutPriorityKeyPicker.ChosenKey;

            Win32.KeyModifiers modifiers = Win32.KeyModifiers.None;
            if (layoutPriorityAltCheckBox.Checked)
                modifiers |= Win32.KeyModifiers.Alt;
            if (layoutPriorityCtrlCheckBox.Checked)
                modifiers |= Win32.KeyModifiers.Control;
            if (layoutPriorityShiftCheckBox.Checked)
                modifiers |= Win32.KeyModifiers.Shift;

            Properties.Settings.Default.layoutPriorityToggleKeyModifiers = (int)modifiers;
        }

        private void CreateSwitchingModeTab()
        {
            // Create a new tab page for Switching Mode
            var switchingModeTab = new TabPage("Switching Mode");
            switchingModeTab.AutoScroll = true;
            switchingModeTab.Padding = new Padding(10);
            
            // Add the tab to the tab control
            tabControl1.TabPages.Add(switchingModeTab);

            // Create main group box
            switchingModeGroupBox = new GroupBox
            {
                Text = "Switching Mode Configuration",
                Location = new Point(10, 10),
                Size = new Size(720, 280),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            // Enabled checkbox
            switchingModeEnabledCheckBox = new CheckBox
            {
                Text = "Enable Switching Mode",
                Location = new Point(10, 20),
                Size = new Size(200, 20),
                Checked = true
            };
            switchingModeGroupBox.Controls.Add(switchingModeEnabledCheckBox);

            // Description label
            switchingModeDescriptionLabel = new Label
            {
                Text = "Switching Mode allows you to reorganize windows by swapping their controller assignments.\n\n" +
                       "• Hold Alt to enter Switching Mode (all windows show red borders with numbers)\n" +
                       "• Use the keybinds below to select/switch windows or mark them for removal\n" +
                       "• Release Alt to exit Switching Mode and apply changes",
                Location = new Point(10, 45),
                Size = new Size(700, 80),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            switchingModeGroupBox.Controls.Add(switchingModeDescriptionLabel);

            // Switch keybind section
            var switchLabel = new Label
            {
                Text = "Switch/Select Keybind:",
                Location = new Point(10, 135),
                Size = new Size(200, 20),
                Font = new Font(switchingModeGroupBox.Font, FontStyle.Bold)
            };
            switchingModeGroupBox.Controls.Add(switchLabel);

            var switchDescLabel = new Label
            {
                Text = "Press this key (or click) on a window to select it. Press again on another window to swap them.",
                Location = new Point(10, 155),
                Size = new Size(700, 20)
            };
            switchingModeGroupBox.Controls.Add(switchDescLabel);

            var switchKeyLabel = new Label
            {
                Text = "Key:",
                Location = new Point(10, 180),
                Size = new Size(60, 20)
            };
            switchingModeGroupBox.Controls.Add(switchKeyLabel);

            // ComboBox for selecting mouse button or keyboard key
            switchingModeSwitchComboBox = new ComboBox
            {
                Location = new Point(80, 178),
                Size = new Size(150, 23),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            switchingModeSwitchComboBox.Items.AddRange(new object[] {
                "Left Mouse Button",
                "Right Mouse Button",
                "Middle Mouse Button",
                "Keyboard Key..."
            });
            switchingModeSwitchComboBox.SelectedIndexChanged += SwitchingModeSwitchComboBox_SelectedIndexChanged;
            switchingModeGroupBox.Controls.Add(switchingModeSwitchComboBox);

            // KeyPicker for keyboard key selection (initially hidden)
            switchingModeSwitchKeyPicker = new KeyPicker
            {
                Location = new Point(240, 178),
                Size = new Size(120, 23),
                Visible = false
            };
            switchingModeGroupBox.Controls.Add(switchingModeSwitchKeyPicker);

            // Remove keybind section
            var removeLabel = new Label
            {
                Text = "Remove Keybind:",
                Location = new Point(10, 210),
                Size = new Size(200, 20),
                Font = new Font(switchingModeGroupBox.Font, FontStyle.Bold)
            };
            switchingModeGroupBox.Controls.Add(removeLabel);

            var removeDescLabel = new Label
            {
                Text = "Press this key on a window to mark it for removal (black highlight). Release Alt to remove all marked windows.",
                Location = new Point(10, 230),
                Size = new Size(700, 20)
            };
            switchingModeGroupBox.Controls.Add(removeDescLabel);

            var removeKeyLabel = new Label
            {
                Text = "Key:",
                Location = new Point(10, 255),
                Size = new Size(60, 20)
            };
            switchingModeGroupBox.Controls.Add(removeKeyLabel);

            switchingModeRemoveKeyPicker = new KeyPicker
            {
                Location = new Point(80, 253),
                Size = new Size(120, 23)
            };
            switchingModeGroupBox.Controls.Add(switchingModeRemoveKeyPicker);

            // Add group box to tab
            switchingModeTab.Controls.Add(switchingModeGroupBox);
        }

        private void SwitchingModeSwitchComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (switchingModeSwitchComboBox == null)
                return;

            // Show/hide KeyPicker based on selection
            // Index 0-2: Mouse buttons, Index 3: Keyboard Key
            switchingModeSwitchKeyPicker.Visible = switchingModeSwitchComboBox.SelectedIndex == 3;
        }

        private void LoadSwitchingModeSettings()
        {
            if (switchingModeSwitchComboBox == null)
                return;

            // Load enabled checkbox
            switchingModeEnabledCheckBox.Checked = Properties.Settings.Default.switchingModeEnabled;

            int switchKeyCode = Properties.Settings.Default.switchingModeSwitchKeyCode;
            
            // Map key codes to ComboBox indices:
            // 1 = Left Mouse Button (LButton)
            // 2 = Right Mouse Button (RButton)
            // 4 = Middle Mouse Button (MButton)
            // Other = Keyboard Key
            
            if (switchKeyCode == 1)
            {
                switchingModeSwitchComboBox.SelectedIndex = 0; // Left Mouse Button
            }
            else if (switchKeyCode == 2)
            {
                switchingModeSwitchComboBox.SelectedIndex = 1; // Right Mouse Button
            }
            else if (switchKeyCode == 4)
            {
                switchingModeSwitchComboBox.SelectedIndex = 2; // Middle Mouse Button
            }
            else
            {
                switchingModeSwitchComboBox.SelectedIndex = 3; // Keyboard Key
                switchingModeSwitchKeyPicker.ChosenKey = (Keys)switchKeyCode;
                switchingModeSwitchKeyPicker.Visible = true;
            }
            
            // Load remove keybind (default: X key = 88)
            switchingModeRemoveKeyPicker.ChosenKey = (Keys)Properties.Settings.Default.switchingModeRemoveKeyCode;
        }

        private void SaveSwitchingModeSettings()
        {
            if (switchingModeSwitchComboBox == null)
                return;

            // Save enabled checkbox
            Properties.Settings.Default.switchingModeEnabled = switchingModeEnabledCheckBox.Checked;

            // Map ComboBox selection to key code
            int switchKeyCode;
            switch (switchingModeSwitchComboBox.SelectedIndex)
            {
                case 0: // Left Mouse Button
                    switchKeyCode = 1;
                    break;
                case 1: // Right Mouse Button
                    switchKeyCode = 2;
                    break;
                case 2: // Middle Mouse Button
                    switchKeyCode = 4;
                    break;
                case 3: // Keyboard Key
                default:
                    switchKeyCode = (int)switchingModeSwitchKeyPicker.ChosenKey;
                    break;
            }

            Properties.Settings.Default.switchingModeSwitchKeyCode = switchKeyCode;
            Properties.Settings.Default.switchingModeRemoveKeyCode = (int)switchingModeRemoveKeyPicker.ChosenKey;
        }

        private void LoadAutoFindSettings()
        {
            if (autoFindExecutablesTextBox == null)
                return;

            autoFindExecutablesTextBox.Text = Properties.Settings.Default.autoFindExecutables;
            autoFindKeyPicker.ChosenKey = (Keys)Properties.Settings.Default.autoFindWindowsKeyCode;
            autoFindAltCheckBox.Checked = ((Win32.KeyModifiers)Properties.Settings.Default.autoFindWindowsKeyModifiers & Win32.KeyModifiers.Alt) != 0;
            autoFindCtrlCheckBox.Checked = ((Win32.KeyModifiers)Properties.Settings.Default.autoFindWindowsKeyModifiers & Win32.KeyModifiers.Control) != 0;
            autoFindShiftCheckBox.Checked = ((Win32.KeyModifiers)Properties.Settings.Default.autoFindWindowsKeyModifiers & Win32.KeyModifiers.Shift) != 0;
        }

        private void SaveAutoFindSettings()
        {
            if (autoFindExecutablesTextBox == null)
                return;

            Properties.Settings.Default.autoFindExecutables = autoFindExecutablesTextBox.Text;
            Properties.Settings.Default.autoFindWindowsKeyCode = (int)autoFindKeyPicker.ChosenKey;

            Win32.KeyModifiers modifiers = Win32.KeyModifiers.None;
            if (autoFindAltCheckBox.Checked)
                modifiers |= Win32.KeyModifiers.Alt;
            if (autoFindCtrlCheckBox.Checked)
                modifiers |= Win32.KeyModifiers.Control;
            if (autoFindShiftCheckBox.Checked)
                modifiers |= Win32.KeyModifiers.Shift;

            Properties.Settings.Default.autoFindWindowsKeyModifiers = (int)modifiers;
        }

        private void LoadLayoutPreset(int presetNumber)
        {
            var preset = LayoutPreset.LoadFromSettings(presetNumber);
            var controls = presetControls[presetNumber - 1];

            controls.EnabledCheckBox.Checked = preset.Enabled;
            controls.ColumnsNumeric.Value = preset.Columns;
            controls.RowsNumeric.Value = preset.Rows;
            
            // Load all regions
            controls.RegionControlsList.Clear();
            controls.RegionsPanel.Controls.Clear();
            
            if (preset.Regions != null && preset.Regions.Count > 0)
            {
                foreach (var region in preset.Regions)
                {
                    AddRegionControls(controls, region, region.Mode, region.DisplayIndex);
                }
            }
            else
            {
                AddRegionControls(controls, new LayoutRegion());
            }
            
            // Update panel and groupbox sizes after loading all regions
            UpdateRegionPanelSize(controls);
            
            controls.HotkeyPicker.ChosenKey = (Keys)preset.HotkeyCode;
            controls.AltCheckBox.Checked = (preset.HotkeyModifiers & Win32.KeyModifiers.Alt) != 0;
            controls.CtrlCheckBox.Checked = (preset.HotkeyModifiers & Win32.KeyModifiers.Control) != 0;
            controls.ShiftCheckBox.Checked = (preset.HotkeyModifiers & Win32.KeyModifiers.Shift) != 0;
        }

        private void okBtn_Click(object sender, EventArgs e)
        {
            Properties.SerializedSettings.Default.Bindings = controlsPicker.KeyMappings;

            // Save layout presets (save all presets in the list)
            for (int i = 0; i < presetControls.Count; i++)
            {
                SaveLayoutPreset(i + 1);
            }
            
            // Clear any remaining presets (2-4) if they exist but aren't in the list
            for (int i = presetControls.Count + 1; i <= 4; i++)
            {
                // Reset unused presets to default disabled state
                var emptyPreset = new LayoutPreset
                {
                    Enabled = false,
                    Columns = 4,
                    Rows = 2,
                    Regions = new List<LayoutRegion> { new LayoutRegion() },
                    HotkeyCode = 0,
                    HotkeyModifiers = Win32.KeyModifiers.None
                };
                emptyPreset.SaveToSettings(i);
            }

            // Save auto-find settings
            SaveAutoFindSettings();

            // Save layout priority settings
            SaveLayoutPrioritySettings();
            
            // Save switching mode settings
            SaveSwitchingModeSettings();
            
            Properties.Settings.Default.Save();
            DialogResult = DialogResult.OK;
            this.Close();
        }

        private void SaveLayoutPreset(int presetNumber)
        {
            var controls = presetControls[presetNumber - 1];
            
            // Create regions from all region controls
            var regions = new List<LayoutRegion>();
            foreach (var regionControls in controls.RegionControlsList)
            {
                var region = new LayoutRegion(
                    (int)regionControls.XNumeric.Value,
                    (int)regionControls.YNumeric.Value,
                    (int)regionControls.WidthNumeric.Value,
                    (int)regionControls.HeightNumeric.Value
                );
                
                // Save mode and display index
                region.Mode = regionControls.IsManualMode ? LayoutRegionMode.Manual : LayoutRegionMode.Display;
                region.DisplayIndex = regionControls.DisplayComboBox != null && regionControls.DisplayComboBox.SelectedIndex >= 0
                    ? regionControls.DisplayComboBox.SelectedIndex
                    : -1;
                
                regions.Add(region);
            }
            
            var preset = new LayoutPreset
            {
                Enabled = controls.EnabledCheckBox.Checked,
                Columns = (int)controls.ColumnsNumeric.Value,
                Rows = (int)controls.RowsNumeric.Value,
                Regions = regions,
                HotkeyCode = (int)controls.HotkeyPicker.ChosenKey,
                HotkeyModifiers = Win32.KeyModifiers.None
            };

            if (controls.AltCheckBox.Checked)
                preset.HotkeyModifiers |= Win32.KeyModifiers.Alt;
            if (controls.CtrlCheckBox.Checked)
                preset.HotkeyModifiers |= Win32.KeyModifiers.Control;
            if (controls.ShiftCheckBox.Checked)
                preset.HotkeyModifiers |= Win32.KeyModifiers.Shift;

            preset.SaveToSettings(presetNumber);
        }

        private void cancelBtn_Click(object sender, EventArgs e)
        {
            Properties.Settings.Default.Reload();
            DialogResult = DialogResult.Cancel;
        }

        private void aboutBtn_Click(object sender, EventArgs e)
        {
            new AboutWnd().ShowDialog(this);
        }

        private void checkUpdateBtn_Click(object sender, EventArgs e)
        {
            if (ApplicationDeployment.IsNetworkDeployed)
            {
                checkUpdates_ClickOnce();
            }
            else
            {
                checkUpdates_Standalone();
            }
        }
        
        private void addBindingBtn_Click(object sender, EventArgs e)
        {
            AddKeyMappingDlg addKeyMappingDlg = new AddKeyMappingDlg();

            while (addKeyMappingDlg.ShowDialog() == DialogResult.OK)
            {
                var keyBindings = controlsPicker.KeyMappings;

                if (string.IsNullOrEmpty(addKeyMappingDlg.BindingName.Trim()))
                {
                    MessageBox.Show("Please enter a name for the binding.");
                }
                /*else if (addKeyMappingDlg.LeftToonKey != Keys.None && keyBindings.Any(t => t.LeftToonKey == addKeyMappingDlg.LeftToonKey))
                {
                    MessageBox.Show("Sorry, the key you picked for the left toon is already being used for another binding on the left toon.");
                }
                else if (addKeyMappingDlg.RightToonKey != Keys.None && keyBindings.Any(t => t.RightToonKey == addKeyMappingDlg.RightToonKey))
                {
                    MessageBox.Show("Sorry, the key you picked for the right toon is already being used for another binding on the right toon.");
                }*/
                else
                {
                    if (addKeyMappingDlg.LeftToonKey >= Keys.D0 && addKeyMappingDlg.LeftToonKey <= Keys.D9
                        || addKeyMappingDlg.LeftToonKey >= Keys.NumPad0 && addKeyMappingDlg.LeftToonKey <= Keys.NumPad9
                        || addKeyMappingDlg.RightToonKey >= Keys.D0 && addKeyMappingDlg.RightToonKey <= Keys.D9
                        || addKeyMappingDlg.RightToonKey >= Keys.NumPad0 && addKeyMappingDlg.RightToonKey <= Keys.NumPad9)
                    {
                        MessageBox.Show("Note: the number keys (0-9) and number pad keys are reserved for switching groups if there is more than 1 group.");
                    }

                    controlsPicker.AddMapping(new KeyMapping(addKeyMappingDlg.BindingName, addKeyMappingDlg.BindingKey, addKeyMappingDlg.LeftToonKey, addKeyMappingDlg.RightToonKey, false));
                    break;
                }
            }
        }

        private void CreateLayoutPresetsUI()
        {
            // Create a new tab page for Layout Presets
            layoutTabPage = new TabPage("Layout Presets");
            layoutTabPage.AutoScroll = true;
            
            // Add the tab to the existing tabControl1
            tabControl1.TabPages.Add(layoutTabPage);

            // Check which presets exist in settings and create UI for all of them
            // A preset "exists" if it has been saved (has non-default values or is enabled)
            int presetCount = 1; // Always have at least 1 preset
            for (int i = 2; i <= 4; i++)
            {
                var preset = LayoutPreset.LoadFromSettings(i);
                // Consider a preset to exist if it's enabled, has a hotkey, or has non-default regions
                if (preset.Enabled || preset.HotkeyCode != 0 || 
                    (preset.Regions != null && preset.Regions.Count > 0 && 
                     !(preset.Regions.Count == 1 && preset.Regions[0].X == 0 && preset.Regions[0].Y == 0 && 
                       preset.Regions[0].Width == 1920 && preset.Regions[0].Height == 1080)))
                {
                    presetCount = i;
                }
            }

            // Create UI for all existing presets
            int yPosition = 10;
            for (int i = 1; i <= presetCount; i++)
            {
                var presetControls = CreatePresetControls(i, yPosition);
                this.presetControls.Add(presetControls);
                layoutTabPage.Controls.Add(presetControls.GroupBox);
                yPosition += presetControls.GroupBox.Height + 10;
            }

            // Add "Add Preset" button
            addPresetButton = new Button
            {
                Text = "Add Preset",
                Location = new Point(10, yPosition),
                Size = new Size(120, 30),
                Anchor = AnchorStyles.Top | AnchorStyles.Left
            };
            addPresetButton.Click += AddPresetButton_Click;
            layoutTabPage.Controls.Add(addPresetButton);
            
            // Disable "Add Preset" button if we've reached the limit
            if (presetControls.Count >= 4)
            {
                addPresetButton.Enabled = false;
            }
            
            // Update remove button visibility (should be hidden with only 1 preset)
            UpdatePresetRemoveButtons();
        }

        private void AddPresetButton_Click(object sender, EventArgs e)
        {
            // Maximum of 4 presets (due to LayoutPreset class limitation)
            if (presetControls.Count >= 4)
            {
                MessageBox.Show("Maximum of 4 presets allowed.", "Limit Reached", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            int newPresetNumber = presetControls.Count + 1;
            int yPosition = 10;
            
            // Calculate Y position based on existing presets
            foreach (var preset in presetControls)
            {
                yPosition += preset.GroupBox.Height + 10;
            }

            var newPresetControls = CreatePresetControls(newPresetNumber, yPosition);
            presetControls.Add(newPresetControls);
            layoutTabPage.Controls.Add(newPresetControls.GroupBox);
            
            // Reposition "Add Preset" button
            yPosition += newPresetControls.GroupBox.Height + 10;
            addPresetButton.Location = new Point(10, yPosition);
            
            // Disable "Add Preset" button if we've reached the limit
            if (presetControls.Count >= 4)
            {
                addPresetButton.Enabled = false;
            }
            
            // Update remove button visibility
            UpdatePresetRemoveButtons();
        }

        private LayoutPresetControls CreatePresetControls(int presetNumber, int yPos)
        {
            var controls = new LayoutPresetControls { PresetNumber = presetNumber };
            controls.RegionControlsList = new List<RegionControls>();

            // Create group box
            controls.GroupBox = new GroupBox
            {
                Text = $"Preset {presetNumber}",
                Location = new Point(10, yPos),
                Size = new Size(730, 230),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            // Enabled checkbox
            controls.EnabledCheckBox = new CheckBox
            {
                Text = "Enabled",
                Location = new Point(10, 20),
                Size = new Size(100, 20)
            };
            controls.GroupBox.Controls.Add(controls.EnabledCheckBox);

            // Add "Remove Preset" button (only show if more than 1 preset)
            // Position it next to the Enabled checkbox
            Button removePresetButton = new Button
            {
                Text = "Remove",
                Location = new Point(120, 20),
                Size = new Size(70, 23),
                Visible = false // Initially hidden, will be shown by UpdatePresetRemoveButtons
            };
            removePresetButton.Click += (s, e) => RemovePreset(controls);
            controls.GroupBox.Controls.Add(removePresetButton);

            // Grid layout section
            Label lblGrid = new Label { Text = "Grid Layout:", Location = new Point(10, 50), Size = new Size(90, 20) };
            controls.GroupBox.Controls.Add(lblGrid);

            controls.ColumnsNumeric = new NumericUpDown
            {
                Location = new Point(100, 48),
                Size = new Size(60, 20),
                Minimum = 1,
                Maximum = 20,
                Value = 4
            };
            controls.GroupBox.Controls.Add(controls.ColumnsNumeric);

            Label lblCols = new Label { Text = "cols x", Location = new Point(165, 50), Size = new Size(40, 20) };
            controls.GroupBox.Controls.Add(lblCols);

            controls.RowsNumeric = new NumericUpDown
            {
                Location = new Point(210, 48),
                Size = new Size(60, 20),
                Minimum = 1,
                Maximum = 20,
                Value = 2
            };
            controls.GroupBox.Controls.Add(controls.RowsNumeric);

            Label lblRows = new Label { Text = "rows", Location = new Point(275, 50), Size = new Size(35, 20) };
            controls.GroupBox.Controls.Add(lblRows);

            // Regions section
            Label lblRegions = new Label { Text = "Regions:", Location = new Point(10, 80), Size = new Size(90, 20) };
            controls.GroupBox.Controls.Add(lblRegions);

            // Panel to hold region controls (auto-sizing, no scroll)
            controls.RegionsPanel = new Panel
            {
                Location = new Point(10, 105),
                Size = new Size(710, 60),
                AutoScroll = false,
                BorderStyle = BorderStyle.None
            };
            controls.GroupBox.Controls.Add(controls.RegionsPanel);

            // Add Region button
            controls.AddRegionButton = new Button
            {
                Text = "Add Region",
                Location = new Point(100, 80),
                Size = new Size(90, 23)
            };
            controls.AddRegionButton.Click += (s, e) => AddRegionControls(controls, new LayoutRegion());
            controls.GroupBox.Controls.Add(controls.AddRegionButton);

            // Hotkey section
            Label lblHotkey = new Label { Text = "Hotkey:", Location = new Point(10, 173), Size = new Size(60, 20) };
            controls.GroupBox.Controls.Add(lblHotkey);

            controls.HotkeyPicker = new KeyPicker
            {
                Location = new Point(70, 171),
                Size = new Size(120, 23)
            };
            controls.GroupBox.Controls.Add(controls.HotkeyPicker);

            controls.AltCheckBox = new CheckBox
            {
                Text = "Alt",
                Location = new Point(200, 173),
                Size = new Size(50, 20)
            };
            controls.GroupBox.Controls.Add(controls.AltCheckBox);

            controls.CtrlCheckBox = new CheckBox
            {
                Text = "Ctrl",
                Location = new Point(260, 173),
                Size = new Size(50, 20)
            };
            controls.GroupBox.Controls.Add(controls.CtrlCheckBox);

            controls.ShiftCheckBox = new CheckBox
            {
                Text = "Shift",
                Location = new Point(320, 173),
                Size = new Size(60, 20)
            };
            controls.GroupBox.Controls.Add(controls.ShiftCheckBox);

            return controls;
        }

        private void RemovePreset(LayoutPresetControls presetToRemove)
        {
            // Keep at least 1 preset
            if (presetControls.Count <= 1)
            {
                MessageBox.Show("At least one preset must remain.", "Cannot Remove", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Remove from list and UI
            presetControls.Remove(presetToRemove);
            layoutTabPage.Controls.Remove(presetToRemove.GroupBox);

            // Reposition remaining presets and update their numbers
            int yPosition = 10;
            for (int i = 0; i < presetControls.Count; i++)
            {
                var preset = presetControls[i];
                preset.PresetNumber = i + 1;
                preset.GroupBox.Text = $"Preset {preset.PresetNumber}";
                preset.GroupBox.Location = new Point(10, yPosition);
                yPosition += preset.GroupBox.Height + 10;
            }

            // Reposition "Add Preset" button
            addPresetButton.Location = new Point(10, yPosition);
            addPresetButton.Enabled = true; // Re-enable if we removed one
            
            // Update remove button visibility for all remaining presets
            UpdatePresetRemoveButtons();
        }

        private void UpdatePresetRemoveButtons()
        {
            // Show remove buttons only if there's more than 1 preset
            bool showRemove = presetControls.Count > 1;
            foreach (var preset in presetControls)
            {
                // Find the remove button in the GroupBox
                foreach (Control control in preset.GroupBox.Controls)
                {
                    if (control is Button button && button.Text == "Remove")
                    {
                        button.Visible = showRemove;
                        break;
                    }
                }
            }
        }

        private void AddRegionControls(LayoutPresetControls presetControls, LayoutRegion region, LayoutRegionMode? mode = null, int displayIndex = -1)
        {
            var regionControls = new RegionControls();
            int yOffset = presetControls.RegionControlsList.Count * 60; // Increased height for two rows

            regionControls.Panel = new Panel
            {
                Location = new Point(0, yOffset),
                Size = new Size(680, 58),
                BorderStyle = BorderStyle.None
            };

            // First row: Mode selection and display selection
            Label lblNum = new Label { Text = $"#{presetControls.RegionControlsList.Count + 1}:", Location = new Point(5, 5), Size = new Size(25, 20) };
            regionControls.Panel.Controls.Add(lblNum);

            // Determine initial mode (use provided mode, or default to Manual)
            bool isManualMode = mode == null || mode == LayoutRegionMode.Manual;

            // Manual/Display mode radio buttons
            regionControls.ManualRadioButton = new RadioButton
            {
                Text = "Manual",
                Location = new Point(35, 5),
                Size = new Size(60, 20),
                Checked = isManualMode
            };
            regionControls.ManualRadioButton.CheckedChanged += (s, e) => UpdateRegionMode(regionControls);
            regionControls.Panel.Controls.Add(regionControls.ManualRadioButton);

            regionControls.DisplayRadioButton = new RadioButton
            {
                Text = "Select Display",
                Location = new Point(100, 5),
                Size = new Size(100, 20),
                Checked = !isManualMode
            };
            regionControls.DisplayRadioButton.CheckedChanged += (s, e) => UpdateRegionMode(regionControls);
            regionControls.Panel.Controls.Add(regionControls.DisplayRadioButton);

            // Display selection ComboBox
            regionControls.DisplayComboBox = new ComboBox
            {
                Location = new Point(205, 3),
                Size = new Size(200, 21),
                DropDownStyle = ComboBoxStyle.DropDownList,
                Enabled = false
            };
            
            // Populate displays
            var screens = Screen.AllScreens;
            for (int i = 0; i < screens.Length; i++)
            {
                var screen = screens[i];
                Rectangle workingArea = screen.WorkingArea;
                string displayName = $"Display {i + 1} ({workingArea.Width}x{workingArea.Height})";
                if (screen.Primary)
                    displayName += " [Primary]";
                regionControls.DisplayComboBox.Items.Add(displayName);
            }
            
            regionControls.DisplayComboBox.SelectedIndexChanged += (s, e) =>
            {
                if (regionControls.DisplayComboBox.SelectedIndex >= 0 && !regionControls.IsManualMode)
                {
                    var selectedScreen = screens[regionControls.DisplayComboBox.SelectedIndex];
                    regionControls.UpdateFromDisplay(selectedScreen);
                }
            };
            regionControls.Panel.Controls.Add(regionControls.DisplayComboBox);

            // Remove button (only show if more than 1 region)
            regionControls.RemoveButton = new Button
            {
                Text = "Remove",
                Location = new Point(450, 2),
                Size = new Size(70, 23)
            };
            regionControls.RemoveButton.Click += (s, e) => RemoveRegionControls(presetControls, regionControls);
            regionControls.Panel.Controls.Add(regionControls.RemoveButton);

            // Second row: Manual input fields
            Label lblX = new Label { Text = "X:", Location = new Point(35, 32), Size = new Size(20, 20) };
            regionControls.Panel.Controls.Add(lblX);

            regionControls.XNumeric = new NumericUpDown
            {
                Location = new Point(55, 30),
                Size = new Size(70, 20),
                Minimum = -10000,
                Maximum = 10000,
                Value = region.X
            };
            regionControls.Panel.Controls.Add(regionControls.XNumeric);

            Label lblY = new Label { Text = "Y:", Location = new Point(130, 32), Size = new Size(20, 20) };
            regionControls.Panel.Controls.Add(lblY);

            regionControls.YNumeric = new NumericUpDown
            {
                Location = new Point(150, 30),
                Size = new Size(70, 20),
                Minimum = -10000,
                Maximum = 10000,
                Value = region.Y
            };
            regionControls.Panel.Controls.Add(regionControls.YNumeric);

            Label lblW = new Label { Text = "W:", Location = new Point(225, 32), Size = new Size(25, 20) };
            regionControls.Panel.Controls.Add(lblW);

            regionControls.WidthNumeric = new NumericUpDown
            {
                Location = new Point(250, 30),
                Size = new Size(80, 20),
                Minimum = 100,
                Maximum = 10000,
                Value = region.Width
            };
            regionControls.Panel.Controls.Add(regionControls.WidthNumeric);

            Label lblH = new Label { Text = "H:", Location = new Point(335, 32), Size = new Size(25, 20) };
            regionControls.Panel.Controls.Add(lblH);

            regionControls.HeightNumeric = new NumericUpDown
            {
                Location = new Point(360, 30),
                Size = new Size(80, 20),
                Minimum = 100,
                Maximum = 10000,
                Value = region.Height
            };
            regionControls.Panel.Controls.Add(regionControls.HeightNumeric);

            presetControls.RegionControlsList.Add(regionControls);
            presetControls.RegionsPanel.Controls.Add(regionControls.Panel);

            // Update mode to set initial enabled/disabled state
            UpdateRegionMode(regionControls);
            
            // Set selected display after UpdateRegionMode (so combo box is enabled if Display mode)
            // Only set if we have a valid saved display index
            if (displayIndex >= 0 && displayIndex < screens.Length)
            {
                regionControls.DisplayComboBox.SelectedIndex = displayIndex;
            }
            
            UpdateRegionButtons(presetControls);
            
            // Update panel and groupbox sizes to fit all regions
            UpdateRegionPanelSize(presetControls);
        }

        private void UpdateRegionMode(RegionControls regionControls)
        {
            bool isManual = regionControls.IsManualMode;
            if (regionControls.DisplayComboBox != null)
            {
                regionControls.DisplayComboBox.Enabled = !isManual;
            }
            
            // Enable/disable numeric controls based on mode
            if (regionControls.XNumeric != null)
                regionControls.XNumeric.Enabled = isManual;
            if (regionControls.YNumeric != null)
                regionControls.YNumeric.Enabled = isManual;
            if (regionControls.WidthNumeric != null)
                regionControls.WidthNumeric.Enabled = isManual;
            if (regionControls.HeightNumeric != null)
                regionControls.HeightNumeric.Enabled = isManual;
            
            // If switching to display mode and no display is selected, select the first one
            if (!isManual && regionControls.DisplayComboBox != null && 
                regionControls.DisplayComboBox.SelectedIndex < 0 && 
                regionControls.DisplayComboBox.Items.Count > 0)
            {
                regionControls.DisplayComboBox.SelectedIndex = 0;
            }
        }

        private void RemoveRegionControls(LayoutPresetControls presetControls, RegionControls regionToRemove)
        {
            if (presetControls.RegionControlsList.Count <= 1)
                return; // Keep at least one region

            presetControls.RegionsPanel.Controls.Remove(regionToRemove.Panel);
            presetControls.RegionControlsList.Remove(regionToRemove);

            // Reposition remaining regions
            for (int i = 0; i < presetControls.RegionControlsList.Count; i++)
            {
                var regionControls = presetControls.RegionControlsList[i];
                regionControls.Panel.Location = new Point(0, i * 60); // Updated height
                
                // Update region number label
                ((Label)regionControls.Panel.Controls[0]).Text = $"#{i + 1}:";
            }

            UpdateRegionButtons(presetControls);
            
            // Update panel and groupbox sizes to fit all regions
            UpdateRegionPanelSize(presetControls);
        }

        private void UpdateRegionPanelSize(LayoutPresetControls presetControls)
        {
            // Calculate required height for regions panel (60 pixels per region)
            int regionHeight = 60;
            int regionsPanelHeight = presetControls.RegionControlsList.Count * regionHeight;
            
            // Update RegionsPanel size
            presetControls.RegionsPanel.Size = new Size(presetControls.RegionsPanel.Width, regionsPanelHeight);
            
            // Hotkey section starts at y=173 originally, so we need to move it down if regions panel grows
            int hotkeySectionY = presetControls.RegionsPanel.Location.Y + regionsPanelHeight + 8; // 8px gap
            
            // Update positions of hotkey section controls
            foreach (Control control in presetControls.GroupBox.Controls)
            {
                if (control == presetControls.HotkeyPicker || 
                    control == presetControls.AltCheckBox || 
                    control == presetControls.CtrlCheckBox || 
                    control == presetControls.ShiftCheckBox)
                {
                    control.Location = new Point(control.Location.X, hotkeySectionY);
                }
                else if (control is Label && control.Text == "Hotkey:")
                {
                    control.Location = new Point(control.Location.X, hotkeySectionY + 2);
                }
            }
            
            // Update GroupBox height to accommodate all content
            // Base height: hotkey section (y position) + hotkey section height (~30) + padding (~10)
            int groupBoxHeight = hotkeySectionY + 40;
            presetControls.GroupBox.Size = new Size(presetControls.GroupBox.Width, groupBoxHeight);
            
            // Update positions of subsequent presets
            int currentY = presetControls.GroupBox.Location.Y + groupBoxHeight + 10;
            int presetIndex = presetControls.PresetNumber - 1;
            for (int i = presetIndex + 1; i < this.presetControls.Count; i++)
            {
                var nextPreset = this.presetControls[i];
                if (nextPreset != null && nextPreset.GroupBox != null)
                {
                    nextPreset.GroupBox.Location = new Point(nextPreset.GroupBox.Location.X, currentY);
                    currentY += nextPreset.GroupBox.Height + 10;
                }
            }
            
            // Update "Add Preset" button position
            if (addPresetButton != null)
            {
                int maxY = 0;
                foreach (var preset in this.presetControls)
                {
                    if (preset.GroupBox != null)
                    {
                        int bottom = preset.GroupBox.Location.Y + preset.GroupBox.Height;
                        if (bottom > maxY) maxY = bottom;
                    }
                }
                addPresetButton.Location = new Point(addPresetButton.Location.X, maxY + 10);
            }
        }

        private void UpdateRegionButtons(LayoutPresetControls presetControls)
        {
            // Show/hide remove buttons based on region count
            bool showRemove = presetControls.RegionControlsList.Count > 1;
            foreach (var regionControls in presetControls.RegionControlsList)
            {
                regionControls.RemoveButton.Visible = showRemove;
            }
        }
    }
}
