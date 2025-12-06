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

        // Helper class to hold controls for one region
        private class RegionControls
        {
            public Panel Panel;
            public NumericUpDown XNumeric;
            public NumericUpDown YNumeric;
            public NumericUpDown WidthNumeric;
            public NumericUpDown HeightNumeric;
            public Button RemoveButton;
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

        private void OptionsDlg_Load(object sender, EventArgs e)
        {
            controlsPicker.KeyMappings = Properties.SerializedSettings.Default.Bindings;

            // Load layout presets
            for (int i = 0; i < 4; i++)
            {
                LoadLayoutPreset(i + 1);
            }

            loaded = true;
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
                    AddRegionControls(controls, region);
                }
            }
            else
            {
                AddRegionControls(controls, new LayoutRegion());
            }
            
            controls.HotkeyPicker.ChosenKey = (Keys)preset.HotkeyCode;
            controls.AltCheckBox.Checked = (preset.HotkeyModifiers & Win32.KeyModifiers.Alt) != 0;
            controls.CtrlCheckBox.Checked = (preset.HotkeyModifiers & Win32.KeyModifiers.Control) != 0;
            controls.ShiftCheckBox.Checked = (preset.HotkeyModifiers & Win32.KeyModifiers.Shift) != 0;
        }

        private void okBtn_Click(object sender, EventArgs e)
        {
            Properties.SerializedSettings.Default.Bindings = controlsPicker.KeyMappings;

            // Save layout presets
            for (int i = 0; i < 4; i++)
            {
                SaveLayoutPreset(i + 1);
            }
            
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
                regions.Add(new LayoutRegion(
                    (int)regionControls.XNumeric.Value,
                    (int)regionControls.YNumeric.Value,
                    (int)regionControls.WidthNumeric.Value,
                    (int)regionControls.HeightNumeric.Value
                ));
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

            // Create 4 preset configuration group boxes
            int yPosition = 10;
            for (int i = 1; i <= 4; i++)
            {
                var presetControls = CreatePresetControls(i, yPosition);
                this.presetControls.Add(presetControls);
                layoutTabPage.Controls.Add(presetControls.GroupBox);
                yPosition += presetControls.GroupBox.Height + 10;
            }
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
                Size = new Size(730, 200),
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

            // Panel to hold region controls (scrollable)
            controls.RegionsPanel = new Panel
            {
                Location = new Point(10, 105),
                Size = new Size(710, 60),
                AutoScroll = true,
                BorderStyle = BorderStyle.FixedSingle
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

        private void AddRegionControls(LayoutPresetControls presetControls, LayoutRegion region)
        {
            var regionControls = new RegionControls();
            int yOffset = presetControls.RegionControlsList.Count * 30;

            regionControls.Panel = new Panel
            {
                Location = new Point(0, yOffset),
                Size = new Size(680, 28),
                BorderStyle = BorderStyle.None
            };

            Label lblNum = new Label { Text = $"#{presetControls.RegionControlsList.Count + 1}:", Location = new Point(5, 5), Size = new Size(25, 20) };
            regionControls.Panel.Controls.Add(lblNum);

            Label lblX = new Label { Text = "X:", Location = new Point(35, 5), Size = new Size(20, 20) };
            regionControls.Panel.Controls.Add(lblX);

            regionControls.XNumeric = new NumericUpDown
            {
                Location = new Point(55, 3),
                Size = new Size(70, 20),
                Minimum = -10000,
                Maximum = 10000,
                Value = region.X
            };
            regionControls.Panel.Controls.Add(regionControls.XNumeric);

            Label lblY = new Label { Text = "Y:", Location = new Point(130, 5), Size = new Size(20, 20) };
            regionControls.Panel.Controls.Add(lblY);

            regionControls.YNumeric = new NumericUpDown
            {
                Location = new Point(150, 3),
                Size = new Size(70, 20),
                Minimum = -10000,
                Maximum = 10000,
                Value = region.Y
            };
            regionControls.Panel.Controls.Add(regionControls.YNumeric);

            Label lblW = new Label { Text = "W:", Location = new Point(225, 5), Size = new Size(25, 20) };
            regionControls.Panel.Controls.Add(lblW);

            regionControls.WidthNumeric = new NumericUpDown
            {
                Location = new Point(250, 3),
                Size = new Size(80, 20),
                Minimum = 100,
                Maximum = 10000,
                Value = region.Width
            };
            regionControls.Panel.Controls.Add(regionControls.WidthNumeric);

            Label lblH = new Label { Text = "H:", Location = new Point(335, 5), Size = new Size(25, 20) };
            regionControls.Panel.Controls.Add(lblH);

            regionControls.HeightNumeric = new NumericUpDown
            {
                Location = new Point(360, 3),
                Size = new Size(80, 20),
                Minimum = 100,
                Maximum = 10000,
                Value = region.Height
            };
            regionControls.Panel.Controls.Add(regionControls.HeightNumeric);

            // Remove button (only show if more than 1 region)
            regionControls.RemoveButton = new Button
            {
                Text = "Remove",
                Location = new Point(450, 2),
                Size = new Size(70, 23)
            };
            regionControls.RemoveButton.Click += (s, e) => RemoveRegionControls(presetControls, regionControls);
            regionControls.Panel.Controls.Add(regionControls.RemoveButton);

            presetControls.RegionControlsList.Add(regionControls);
            presetControls.RegionsPanel.Controls.Add(regionControls.Panel);

            UpdateRegionButtons(presetControls);
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
                regionControls.Panel.Location = new Point(0, i * 30);
                
                // Update region number label
                ((Label)regionControls.Panel.Controls[0]).Text = $"#{i + 1}:";
            }

            UpdateRegionButtons(presetControls);
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
