using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using System.Runtime.Serialization.Formatters.Binary;
using System.Diagnostics;

namespace TTMulti.Forms
{
    /// <summary>
    /// The main window. This window captures all input sent to the window and child controls by 
    /// implementing IMessageFilter and overriding ProcessCmdKey(). All input is sent to the Multicontroller class.
    /// A low-level keyboard hook is also used to listen for the mode key when a Toontown window is active.
    /// </summary>
    internal partial class MulticontrollerWnd : Form, IMessageFilter
    {
        /// <summary>
        /// This flag is used to ignore input while a dialog is open.
        /// </summary>
        bool ignoreMessages = false;

        /// <summary>
        /// The thread used to work around activation issues.
        /// </summary>
        Thread activationThread = null;

        Multicontroller controller;

        bool hotkeyRegistered = false;
        bool userPromptedForAdminRights = false;
        internal MulticontrollerWnd()
        {
            InitializeComponent();
            this.Icon = Properties.Resources.icon;
        }

        /// <summary>
        /// Activates the window.
        /// Works around an issue where sometimes calling Activate() doesn't activate the window.
        /// If calling Activate() doesn't work, this makes the window topmost and fakes a mouse event.
        /// </summary>
        internal void TryActivate()
        {
            if (activationThread == null || activationThread.ThreadState != System.Threading.ThreadState.Running)
            {
                activationThread = new Thread(activationThreadFunc) { IsBackground = true };
                activationThread.Start();
            }
        }

        private void activationThreadFunc()
        {
            IntPtr hWnd = IntPtr.Zero;
            Invoke(new Action(() => hWnd = this.Handle));

            Stopwatch sw = Stopwatch.StartNew();

            do
            {
                // This check was put in to prevent exceptions when the window is closing.
                if (this.IsDisposed)
                {
                    return;
                }

                // First try calling Activate()
                if (sw.ElapsedMilliseconds < 100)
                {
                    Invoke(new Action(() => this.Activate()));
                }
                // If that doesn't work, try SetForegroundWindow and set TopMost
                else
                {
                    Invoke(new Action(() => this.TopMost = true));
                    Win32.SetForegroundWindow(hWnd);
                }

                Thread.Sleep(10);

                if (Win32.GetForegroundWindow() == hWnd)
                {
                    Invoke(new Action(() => this.TopMost = Properties.Settings.Default.onTopWhenInactive));
                    break;
                }
            } while (sw.Elapsed.TotalSeconds < 5);

            sw.Stop();
        }

        /// <summary>
        /// Updates the window selectors and group status.
        /// This should be called when the current group or window selection changes.
        /// </summary>
        internal void UpdateWindowStatus()
        {
            leftToonCrosshair.SelectedWindowHandle = controller.LeftControllers.First().WindowHandle;
            rightToonCrosshair.SelectedWindowHandle = controller.RightControllers.First().WindowHandle;

            leftStatusLbl.Text = "Group " + (controller.CurrentGroupIndex + 1) + " active.";
            rightStatusLbl.Text = controller.ControllerGroups.Count + " groups.";

            if (!statusStrip1.Visible && controller.ControllerGroups.Count > 1 && controller.CurrentMode != MulticontrollerMode.AllGroup)
            {
                statusStrip1.Visible = true;
                this.Padding = new Padding(this.Padding.Left, this.Padding.Top, this.Padding.Right, this.Padding.Bottom + statusStrip1.Height);
            }
            else if (statusStrip1.Visible && (controller.ControllerGroups.Count == 1 || controller.CurrentMode == MulticontrollerMode.AllGroup))
            {
                this.Padding = new Padding(this.Padding.Left, this.Padding.Top, this.Padding.Right, this.Padding.Bottom - statusStrip1.Height);
                statusStrip1.Visible = false;
            }
        }

        /// <summary>
        /// Overrides keys that usually perform other functions like tab, arrow keys, etc. so that
        /// we can use them for control. After getting intercepted, they are caught by the message filter.
        /// </summary>
        /// <param name="msg"></param>
        /// <param name="keyData"></param>
        /// <returns>
        /// Returns true when the key should be intercepted so they don't perform their usual function.
        /// </returns>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Tab:
                case Keys.Up:
                case Keys.Down:
                case Keys.Left:
                case Keys.Right:
                case Keys.Alt:
                    return true;
                default:
                    break;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        /// <summary>
        /// IMessageFilter function implementation. This captures all keys sent to the window, including ones
        /// that are sent directly to child controls, and sends them to the multicontroller.
        /// </summary>
        /// <param name="m"></param>
        /// <returns>
        /// Returns true when the key should be stopped from getting to its destination.
        /// </returns>
        public bool PreFilterMessage(ref Message m)
        {
            if (ignoreMessages)
            {
                return false;
            }

            bool ret = false;

            var msg = (Win32.WM)m.Msg;

            switch (msg)
            {
                case Win32.WM.KEYDOWN:
                case Win32.WM.KEYUP:
                case Win32.WM.SYSKEYDOWN:
                case Win32.WM.SYSKEYUP:
                case Win32.WM.SYSCOMMAND:
                    ret = controller.ProcessInput(m.Msg, m.WParam, m.LParam);
                    break;
                case Win32.WM.HOTKEY:
                    // Check if this is a layout preset hotkey (IDs 3-6) or auto-find (ID 7)
                    int hotkeyId = m.WParam.ToInt32();
                    if (hotkeyId >= 3 && hotkeyId <= 6 || hotkeyId == 7)
                    {
                        // Let layout and auto-find hotkeys pass through to WndProc
                        ret = false;
                    }
                    else
                    {
                        // Process other hotkeys normally
                        ret = controller.ProcessInput(m.Msg, m.WParam, m.LParam);
                    }
                    break;
            }
            
            CheckControllerErrors();

            return ret;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == (int)Win32.WM.HOTKEY)
            {
                int hotkeyId = m.WParam.ToInt32();
                
                // Check if this is a layout preset hotkey (IDs 3-6)
                if (hotkeyId >= 3 && hotkeyId <= 6)
                {
                    int presetNumber = hotkeyId - 2; // 3->1, 4->2, 5->3, 6->4
                    var preset = LayoutPreset.LoadFromSettings(presetNumber);
                    controller.ApplyLayoutPreset(preset);
                }
                // Check if this is auto-find windows hotkey (ID 7)
                else if (hotkeyId == 7)
                {
                    controller.AutoFindAndAssignWindows();
                    // Re-register hotkeys after auto-find in case window lost focus
                    // This ensures layout hotkeys continue to work
                    if (controller.IsActive || controller.AllControllersWithWindows.Any(c => c.IsWindowActive))
                    {
                        if (controller.AllControllersWithWindows.Any(c => c.IsWindowActive))
                        {
                            RegisterHotkey();
                        }
                        RegisterLayoutHotkeys();
                        RegisterAutoFindHotkey();
                    }
                }
                else
                {
                    controller.ProcessInput(m.Msg, m.WParam, m.LParam);
                }
                
                CheckControllerErrors();
            }

            base.WndProc(ref m);
        }

        internal void CheckControllerErrors()
        {
            if (!userPromptedForAdminRights && controller.ErrorOccurredPostingMessage)
            {
                userPromptedForAdminRights = true;

                if (MessageBox.Show(
                    "There was an error controlling a Toontown window. You may need to run the multicontroller as administrator.\n\nDo you want to re-launch as administrator?",
                    "Error",
                    MessageBoxButtons.YesNo) == DialogResult.Yes)
                {
                    Properties.Settings.Default.runAsAdministrator = true;
                    Properties.Settings.Default.Save();

                    if (Program.TryRunAsAdmin())
                    {
                        Application.Exit();
                    }
                    else
                    {
                        MessageBox.Show("Failed to re-launch as administrator.", "Error");
                    }
                }
            }
        }

        internal void SaveWindowPosition()
        {
            Properties.Settings.Default.lastLocation = this.Location;
            Properties.Settings.Default.Save();
        }
        
        private void ReloadOptions()
        {
            this.TopMost = Properties.Settings.Default.onTopWhenInactive;
            panel1.Visible = !Properties.Settings.Default.compactUI;
            controller.UpdateOptions();
            
            // Unregister all hotkeys
            UnregisterHotkey();
            UnregisterLayoutHotkeys();
            UnregisterAutoFindHotkey();
            
            // Re-register hotkeys if multicontroller is active or windows are active
            if (controller.IsActive || controller.AllControllersWithWindows.Any(c => c.IsWindowActive))
            {
                if (controller.AllControllersWithWindows.Any(c => c.IsWindowActive))
                {
                    RegisterHotkey();
                }
                RegisterLayoutHotkeys();
                RegisterAutoFindHotkey();
            }
        }

        private bool RegisterHotkey()
        {
            if (!hotkeyRegistered)
            {
                hotkeyRegistered = Win32.RegisterHotKey(this.Handle, 0, Win32.KeyModifiers.None, (Keys)Properties.Settings.Default.modeKeyCode);
                
                // Register multiclick hotkey globally (ID 1)
                if (Properties.Settings.Default.replicateMouseKeyCode != 0)
                {
                    Win32.RegisterHotKey(this.Handle, 1, Win32.KeyModifiers.None, (Keys)Properties.Settings.Default.replicateMouseKeyCode);
                }
                
                // Register zero power throw hotkey globally (ID 2)
                if (Properties.Settings.Default.zeroPowerThrowKeyCode != 0)
                {
                    Win32.RegisterHotKey(this.Handle, 2, Win32.KeyModifiers.None, (Keys)Properties.Settings.Default.zeroPowerThrowKeyCode);
                }

                // Note: Auto-find hotkey (ID 7) is registered separately and always available
                // Note: Layout hotkeys (IDs 3-6) are registered separately via RegisterLayoutHotkeys()
            }

            return hotkeyRegistered;
        }

        private void UnregisterHotkey()
        {
            // Unregister mode switching hotkeys (IDs 0-2)
            Win32.UnregisterHotKey(this.Handle, 0);
            Win32.UnregisterHotKey(this.Handle, 1);
            Win32.UnregisterHotKey(this.Handle, 2);

            hotkeyRegistered = false;
        }

        private void RegisterLayoutHotkeys()
        {
            // Register layout preset hotkeys (IDs 3-6)
            for (int i = 1; i <= 4; i++)
            {
                var preset = LayoutPreset.LoadFromSettings(i);
                if (preset.Enabled && preset.HotkeyCode != 0)
                {
                    Win32.RegisterHotKey(this.Handle, 2 + i, preset.HotkeyModifiers, (Keys)preset.HotkeyCode);
                }
            }
        }

        private void UnregisterLayoutHotkeys()
        {
            // Unregister layout hotkeys (IDs 3-6)
            Win32.UnregisterHotKey(this.Handle, 3);
            Win32.UnregisterHotKey(this.Handle, 4);
            Win32.UnregisterHotKey(this.Handle, 5);
            Win32.UnregisterHotKey(this.Handle, 6);
        }

        private void RegisterAutoFindHotkey()
        {
            // Register auto-find windows hotkey (ID 7) - only when multicontroller is active
            if (Properties.Settings.Default.autoFindWindowsKeyCode != 0)
            {
                bool success = Win32.RegisterHotKey(this.Handle, 7, (Win32.KeyModifiers)Properties.Settings.Default.autoFindWindowsKeyModifiers, (Keys)Properties.Settings.Default.autoFindWindowsKeyCode);
                if (!success)
                {
                    // Hotkey registration failed - might be already registered or invalid combination
                    // Try unregistering first, then re-registering
                    Win32.UnregisterHotKey(this.Handle, 7);
                    Win32.RegisterHotKey(this.Handle, 7, (Win32.KeyModifiers)Properties.Settings.Default.autoFindWindowsKeyModifiers, (Keys)Properties.Settings.Default.autoFindWindowsKeyCode);
                }
            }
        }

        private void UnregisterAutoFindHotkey()
        {
            // Unregister auto-find hotkey (ID 7)
            Win32.UnregisterHotKey(this.Handle, 7);
        }

        private void MulticontrollerWnd_Load(object sender, EventArgs e)
        {
            controller = Multicontroller.Instance;

            controller.ModeChanged += Controller_ModeChanged;
            controller.GroupsChanged += Controller_GroupsChanged;
            controller.ActiveControllersChanged += Controller_ActiveControllersChanged;
            controller.ShouldActivate += Controller_ShouldActivate;
            controller.WindowActivated += Controller_WindowActivated;
            controller.AllWindowsInactive += Controller_AllWindowsInactive;

            controller.ControllerGroups[0].ControllerPairs[0].LeftController.WindowHandleChanged += LeftController_WindowHandleChanged;
            controller.ControllerGroups[0].ControllerPairs[0].RightController.WindowHandleChanged += RightController_WindowHandleChanged;

            // Removes the extra padding on the right side of the status strip.
            // Apparently this is "not relevant for this class" but still has an effect.
            statusStrip1.Padding = new Padding(statusStrip1.Padding.Left, statusStrip1.Padding.Top, statusStrip1.Padding.Left, statusStrip1.Padding.Bottom);

            // Set up the IMessageFilter so we receive all messages for child controls
            Application.AddMessageFilter(this);
            
            // Restore the saved position of the window, making sure that it's not offscreen
            if (Properties.Settings.Default.lastLocation != Point.Empty)
            {
                var location = Properties.Settings.Default.lastLocation;
                var isNotOffScreen = false;

                foreach (var screen in Screen.AllScreens)
                {
                    if (screen.Bounds.Contains(location))
                    {
                        isNotOffScreen = true;
                        break;
                    }
                }

                if (isNotOffScreen)
                {
                    this.Location = Properties.Settings.Default.lastLocation;
                }
            }

            ReloadOptions();

            // Multicontroller could have loaded groups
            UpdateWindowStatus();
        }
        
        private void MulticontrollerWnd_Shown(object sender, EventArgs e)
        {
            // When window is first shown, check if it's active and register hotkeys
            if (this.ContainsFocus || Win32.GetForegroundWindow() == this.Handle)
            {
                controller.IsActive = true;
                RegisterLayoutHotkeys();
                RegisterAutoFindHotkey();
            }
        }

        private void RightController_WindowHandleChanged(object sender, EventArgs e)
        {
            leftToonCrosshair.SelectedWindowHandle = controller.ControllerGroups[0].ControllerPairs[0].LeftController.WindowHandle;
        }

        private void LeftController_WindowHandleChanged(object sender, EventArgs e)
        {
            rightToonCrosshair.SelectedWindowHandle = controller.ControllerGroups[0].ControllerPairs[0].RightController.WindowHandle;
        }

        private void Controller_AllWindowsInactive(object sender, EventArgs e)
        {
            // Only unregister hotkeys if multicontroller window is also inactive
            // If multicontroller window is active, keep hotkeys registered
            if (!controller.IsActive)
            {
                UnregisterHotkey();
                // Unregister layout hotkeys when all windows are inactive
                UnregisterLayoutHotkeys();
                // Unregister auto-find hotkey when all windows are inactive
                UnregisterAutoFindHotkey();
            }
            else
            {
                // Multicontroller window is still active, ensure hotkeys are registered
                RegisterLayoutHotkeys();
                RegisterAutoFindHotkey();
            }
        }

        private void Controller_WindowActivated(object sender, EventArgs e)
        {
            RegisterHotkey();
            // Also register layout hotkeys when a Toontown window is active
            RegisterLayoutHotkeys();
            // Register auto-find hotkey when a Toontown window is active
            RegisterAutoFindHotkey();
        }

        private void MainWnd_FormClosing(object sender, FormClosingEventArgs e)
        {
            try
            {
                activationThread.Abort();
            }
            catch { }
            
            SaveWindowPosition();
        }

        private void Controller_GroupsChanged(object sender, EventArgs e)
        {
            this.UpdateWindowStatus();
            
            // Re-register hotkeys if multicontroller is active (groups may have been added/removed)
            if (controller.IsActive || controller.AllControllersWithWindows.Any(c => c.IsWindowActive))
            {
                if (controller.AllControllersWithWindows.Any(c => c.IsWindowActive))
                {
                    RegisterHotkey();
                }
                RegisterLayoutHotkeys();
                RegisterAutoFindHotkey();
            }
        }

        private void Controller_ActiveControllersChanged(object sender, EventArgs e)
        {
            UpdateWindowStatus();
        }

        private void Controller_ShouldActivate(object sender, EventArgs e)
        {
            this.TryActivate();
        }

        private void Controller_ModeChanged(object sender, EventArgs e)
        {
            switch (controller.CurrentMode)
            {
                case MulticontrollerMode.Group:
                    multiModeRadio.Checked = true;
                    break;
                case MulticontrollerMode.MirrorAll:
                    mirrorModeRadio.Checked = true;
                    break;
                default:
                    multiModeRadio.Checked = false;
                    mirrorModeRadio.Checked = false;
                    break;
            }

            UpdateWindowStatus();
        }

        private void optionsBtn_Click(object sender, EventArgs e)
        {
            OptionsDlg optionsDlg = new OptionsDlg();

            ignoreMessages = true;

            if (optionsDlg.ShowDialog(this) == DialogResult.OK)
            {
                ReloadOptions();
            }

            ignoreMessages = false;

            UpdateWindowStatus();
        }

        private void windowGroupsBtn_Click(object sender, EventArgs e)
        {
            controller.ShowAllBorders = true;
            ignoreMessages = true;
            new WindowGroupsForm().ShowDialog(this);
            ignoreMessages = false;
            controller.ShowAllBorders = false;

            UpdateWindowStatus();
        }

        private void leftToonCrosshair_WindowSelected(object sender, IntPtr handle)
        {
            controller.ControllerGroups[0].ControllerPairs[0].LeftController.WindowHandle = handle;
        }

        private void rightToonCrosshair_WindowSelected(object sender, IntPtr handle)
        {
            controller.ControllerGroups[0].ControllerPairs[0].RightController.WindowHandle = handle;
        }

        private void multiModeRadio_Click(object sender, EventArgs e)
        {
            controller.CurrentMode = MulticontrollerMode.Group;
        }

        private void mirrorModeRadio_Clicked(object sender, EventArgs e)
        {
            controller.CurrentMode = MulticontrollerMode.MirrorAll;
        }

        private void MulticontrollerWnd_Activated(object sender, EventArgs e)
        {
            controller.IsActive = true;
            // Register layout hotkeys when multicontroller window is active
            RegisterLayoutHotkeys();
            // Register auto-find hotkey when multicontroller window is active
            RegisterAutoFindHotkey();
        }

        private void MulticontrollerWnd_Deactivate(object sender, EventArgs e)
        {
            controller.IsActive = false;
            // Unregister layout hotkeys when multicontroller window is inactive
            UnregisterLayoutHotkeys();
            // Unregister auto-find hotkey when multicontroller window is inactive
            UnregisterAutoFindHotkey();
        }
    }
}
