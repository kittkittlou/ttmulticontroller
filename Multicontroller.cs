using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Drawing;
using System.Threading;
using System.Diagnostics;
using System.Runtime.InteropServices;
using TTMulti.Forms;

namespace TTMulti
{
    internal enum MulticontrollerMode
    {
        /// <summary>
        /// Control all pairs of toons in the current group with separate left and right controls (default mode)
        /// </summary>
        Group,

        /// <summary>
        /// Control both toons in the current pair with separate left and right controls
        /// </summary>
        Pair,

        /// <summary>
        /// Control all groups of toons with separate left and right controls
        /// </summary>
        AllGroup,

        /// <summary>
        /// Mirror all input to all groups of toons
        /// </summary>
        MirrorAll,

        /// <summary>
        /// Mirror all input to all pairs of the current group
        /// </summary>
        MirrorGroup,

    /// <summary>
    /// Mirror all input to one controller
    /// </summary>
    MirrorIndividual,
    
    /// <summary>
    /// Focused mode - all input goes to all windows except directional movement keys
    /// </summary>
    Focused
}

    class Multicontroller
    {
        private static Multicontroller _instance = null;

        internal static Multicontroller Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new Multicontroller();

                    int numberOfGroups = Properties.Settings.Default.numberOfGroups;
                    // Ensure at least one group is always created
                    if (numberOfGroups <= 0)
                    {
                        numberOfGroups = 1;
                        Properties.Settings.Default.numberOfGroups = 1;
                        Properties.Settings.Default.Save();
                    }

                    for (int i = 0; i < numberOfGroups; i++)
                    {
                        _instance.AddControllerGroup();
                    }
                }

                return _instance;
            }
        }

        /// <summary>
        /// The multicontroller was activated or deactivated
        /// </summary>
        public event EventHandler ActiveChanged;

        /// <summary>
        /// The mode of the multicontroller changed
        /// </summary>
        public event EventHandler ModeChanged;

        /// <summary>
        /// The controllers that are active changed
        /// </summary>
        public event EventHandler ActiveControllersChanged;

        /// <summary>
        /// A group was added or removed
        /// </summary>
        public event EventHandler GroupsChanged;

        /// <summary>
        /// A misc. setting of the multicontroller was changed
        /// </summary>
        public event EventHandler SettingChanged;

        /// <summary>
        /// The multicontroller should be actived (due to a hotkey)
        /// </summary>
        public event EventHandler ShouldActivate;

        /// <summary>
        /// A controlled window was activated
        /// </summary>
        public event EventHandler WindowActivated;

        /// <summary>
        /// All controlled windows are now inactive
        /// </summary>
        public event EventHandler AllWindowsInactive;
        
        internal List<ControllerGroup> ControllerGroups { get; } = new List<ControllerGroup>();

        internal IEnumerable<ToontownController> ActiveControllers
        {
            get
            {
                switch (CurrentMode)
                {
                    case MulticontrollerMode.Group:
                        if (ControllerGroups.Count > 0 && CurrentGroupIndex < ControllerGroups.Count)
                        {
                            return ControllerGroups[CurrentGroupIndex].AllControllers;
                        }
                        return new ToontownController[] { };
                    case MulticontrollerMode.AllGroup:
                    case MulticontrollerMode.MirrorAll:
                    case MulticontrollerMode.Focused:
                        return AllControllers;
                }

                return new ToontownController[] { };
            }
        }

        int currentGroupIndex = 0;

        /// <summary>
        /// The index of the group that is currently being controlled, if applicable in the current mode
        /// </summary>
        internal int CurrentGroupIndex
        {
            get
            {
                if (ControllerGroups.Count > 0 && currentGroupIndex >= ControllerGroups.Count)
                {
                    currentGroupIndex = 0;
                }

                return currentGroupIndex;
            }
            private set
            {
                if (currentGroupIndex != value)
                {
                    currentGroupIndex = value;

                    ActiveControllersChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        int _currentPairIndex = 0;

        /// <summary>
        /// The index of the current pair that is being controlled (in pair mode)
        /// </summary>
        internal int CurrentPairIndex
        {
            get
            {
                if (_currentPairIndex > AllControllerPairsWithWindows.Count())
                {
                    _currentPairIndex = 0;
                }

                return _currentPairIndex;
            }
            set
            {
                if (_currentPairIndex != value)
                {
                    _currentPairIndex = value;

                    ActiveControllersChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        int _currentIndividualControllerIndex = 0;

        internal int CurrentIndividualControllerIndex
        {
            get
            {
                if (AllControllersWithWindows.Count() > 0 && _currentIndividualControllerIndex >= AllControllersWithWindows.Count())
                {
                    _currentIndividualControllerIndex = 0;
                }

                return _currentIndividualControllerIndex;
            }
            private set
            {
                if (_currentIndividualControllerIndex != value)
                {
                    _currentIndividualControllerIndex = value;

                    ActiveControllersChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        /// <summary>
        /// Left controllers of the current group, or all groups if all groups are being controlled at once
        /// </summary>
        internal IEnumerable<ToontownController> LeftControllers
        {
            get
            {
                if (CurrentMode == MulticontrollerMode.AllGroup)
                {
                    return ControllerGroups.SelectMany(g => g.LeftControllers);
                }
                else
                {
                    if (ControllerGroups.Count > 0 && CurrentGroupIndex < ControllerGroups.Count)
                    {
                        return ControllerGroups[CurrentGroupIndex].LeftControllers;
                    }
                    return new ToontownController[] { };
                }
            }
        }

        /// <summary>
        /// Right controllers of the current group, or all groups if all groups are being controlled at once
        /// </summary>
        internal IEnumerable<ToontownController> RightControllers
        {
            get
            {
                if (CurrentMode == MulticontrollerMode.AllGroup)
                {
                    return ControllerGroups.SelectMany(g => g.RightControllers);
                }
                else if (CurrentMode == MulticontrollerMode.Pair)
                {
                    if (CurrentControllerPair != null)
                    {
                        return new[] { CurrentControllerPair?.RightController };
                    }
                    else
                    {
                        return new ToontownController[] { };
                    }
                }
                else
                {
                    if (ControllerGroups.Count > 0 && CurrentGroupIndex < ControllerGroups.Count)
                    {
                        return ControllerGroups[CurrentGroupIndex].RightControllers;
                    }
                    return new ToontownController[] { };
                }
            }
        }

        /// <summary>
        /// The current controller that is being controlled individually
        /// </summary>
        internal ToontownController CurrentIndividualController
        {
            get
            {
                if (CurrentIndividualControllerIndex < AllControllersWithWindows.Count())
                {
                    return AllControllersWithWindows.ElementAt(CurrentIndividualControllerIndex);
                }

                return null;
            }
        }

        internal IEnumerable<ToontownController> AllControllers
        {
            get
            {
                return ControllerGroups.SelectMany(g => g.ControllerPairs.SelectMany(p => new[] { p.LeftController, p.RightController }));
            }
        }

        internal IEnumerable<ToontownController> AllControllersWithWindows
        {
            get
            {
                return AllControllers.Where(c => c.HasWindow);
            }
        }

        internal IEnumerable<ControllerPair> AllControllerPairs
        {
            get
            {
                return ControllerGroups.SelectMany(g => g.ControllerPairs);
            }
        }

        internal IEnumerable<ControllerPair> AllControllerPairsWithWindows
        {
            get
            {
                return AllControllerPairs.Where(p => p.LeftController.HasWindow || p.RightController.HasWindow);
            }
        }

        internal ControllerPair CurrentControllerPair
        {
            get
            {
                if (AllControllerPairsWithWindows.Count() > 0)
                {
                    return AllControllerPairsWithWindows.ElementAt(CurrentPairIndex);
                }

                return null;
            }
        }

        /// <summary>
        /// Whether an error occurred when posting a message to a Toontown window.
        /// This usually indicated that we don't have enough privileges and need to run as administrator.
        /// </summary>
        public bool ErrorOccurredPostingMessage
        {
            get => ControllerGroups.Any(g => g.AllControllers.Any(c => c.ErrorOccurredPostingMessage));
        }

        private bool showAllBorders = false;
        public bool ShowAllBorders
        {
            get => showAllBorders;
            set
            {
                if (showAllBorders != value)
                {
                    showAllBorders = value;

                    SettingChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        private bool _isActive = false;
        internal bool IsActive
        {
            get { return _isActive; }
            set
            {
                if (_isActive != value)
                {
                    _isActive = value;

                    ActiveChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        MulticontrollerMode _currentMode = MulticontrollerMode.Group;

        internal MulticontrollerMode CurrentMode
        {
            get { return _currentMode; }
            set
            {
                if (_currentMode != value)
                {
                    _currentMode = value;
                    
                    ModeChanged?.Invoke(this, EventArgs.Empty);
                    ActiveControllersChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }
        
        Dictionary<Keys, List<Keys>> leftKeys = new Dictionary<Keys, List<Keys>>(),
            rightKeys = new Dictionary<Keys, List<Keys>>();
        
        bool zeroPowerThrowKeyPressed = false;
        bool multiClickKeyPressed = false;

        int lastMoveX, lastMoveY;

        // Window switching mode state
        private bool _switchingMode = false;
        private ToontownController _firstSelectedController = null;
        private ToontownController _secondSelectedController = null;
        private System.Windows.Forms.Timer _switchingModeTimer = null;
        private HashSet<ToontownController> _switchedControllers = new HashSet<ToontownController>();
        private HashSet<ToontownController> _markedForRemoval = new HashSet<ToontownController>();
        
        // Global mouse hook for blocking clicks in switching mode
        private static IntPtr _mouseHookHandle = IntPtr.Zero;
        private static Multicontroller _hookInstance = null;
        private static Win32.HookProc _mouseHookProc = null;

        /// <summary>
        /// Whether switching mode is currently active
        /// </summary>
        internal bool IsSwitchingMode => _switchingMode;

        internal Multicontroller()
        {
            UpdateOptions();
            
            // Initialize switching mode timer for mouse tracking
            _switchingModeTimer = new System.Windows.Forms.Timer();
            _switchingModeTimer.Interval = 50; // Check every 50ms
            _switchingModeTimer.Tick += SwitchingModeTimer_Tick;
        }

        internal void UpdateOptions()
        {
            leftKeys.Clear();
            rightKeys.Clear();

            var keyBindings = Properties.SerializedSettings.Default.Bindings;

            for (int i = 0; i < keyBindings.Count; i++)
            {
                if (!leftKeys.ContainsKey(keyBindings[i].LeftToonKey))
                {
                    leftKeys.Add(keyBindings[i].LeftToonKey, new List<Keys>());
                }

                if (!rightKeys.ContainsKey(keyBindings[i].RightToonKey))
                {
                    rightKeys.Add(keyBindings[i].RightToonKey, new List<Keys>());
                }

                if (keyBindings[i].Key != Keys.None && keyBindings[i].LeftToonKey != Keys.None)
                {
                    leftKeys[keyBindings[i].LeftToonKey].Add(keyBindings[i].Key);
                }

                if (keyBindings[i].Key != Keys.None && keyBindings[i].RightToonKey != Keys.None)
                {
                    rightKeys[keyBindings[i].RightToonKey].Add(keyBindings[i].Key);
                }
            }
        }

        private void SwitchingModeTimer_Tick(object sender, EventArgs e)
        {
            if (!_switchingMode)
            {
                _switchingModeTimer.Stop();
                return;
            }

            // Update switching mode display for all controllers
            UpdateSwitchingModeDisplay();
        }

        private void UpdateSwitchingModeDisplay()
        {
            // Calculate switching numbers based on group and type (Left/Right)
            // All controllers with the same group and type get the same number, regardless of pair
            // Numbering: Group 1 Left = 1, Group 1 Right = 2, Group 2 Left = 3, Group 2 Right = 4, etc.
            
            // Apply switching numbers to all controllers with windows
            foreach (var controller in AllControllersWithWindows)
            {
                var borderWnd = GetBorderWindow(controller);
                if (borderWnd != null)
                {
                    borderWnd.SwitchingMode = true;
                    
                    // Calculate switching number: (GroupNumber - 1) * 2 + (Left = 1, Right = 2)
                    int switchingNumber = (controller.GroupNumber - 1) * 2 + (controller.Type == ControllerType.Left ? 1 : 2);
                    borderWnd.SwitchingNumber = switchingNumber;
                    
                    // Selected windows (first or second selected) are Yellow
                    bool isSelected = (controller == _firstSelectedController || controller == _secondSelectedController);
                    // Switched windows (in _switchedControllers but not currently selected) are Orange
                    bool isSwitched = _switchedControllers.Contains(controller) && !isSelected;
                    // Marked for removal windows are Black
                    bool isMarkedForRemoval = _markedForRemoval.Contains(controller);
                    
                    borderWnd.SwitchingSelected = isSelected;
                    borderWnd.SwitchingSwitched = isSwitched;
                    borderWnd.SwitchingMarkedForRemoval = isMarkedForRemoval;
                }
            }
        }

        private void ExitSwitchingMode()
        {
            _switchingMode = false;
            _switchingModeTimer.Stop();
            _firstSelectedController = null;
            _secondSelectedController = null;
            
            // Uninstall global mouse hook
            UninstallMouseHook();

            // Disconnect all controllers marked for removal
            foreach (var controller in _markedForRemoval.ToList())
            {
                if (controller != null && controller.HasWindow)
                {
                    controller.WindowHandle = IntPtr.Zero;
                }
            }
            _markedForRemoval.Clear();

            // Reset all border windows, but keep SwitchingSelected true for switched controllers
            foreach (var controller in AllControllersWithWindows)
            {
                var borderWnd = GetBorderWindow(controller);
                if (borderWnd != null)
                {
                    borderWnd.SwitchingMode = false;
                    borderWnd.SwitchingNumber = 0;
                    borderWnd.SwitchingSwitched = false;
                    borderWnd.SwitchingMarkedForRemoval = false;
                    // Keep SwitchingSelected true for controllers that were switched
                    if (!_switchedControllers.Contains(controller))
                    {
                        borderWnd.SwitchingSelected = false;
                    }
                }
            }
            
            // Apply the last used layout preset when exiting switching mode
            int lastUsedPreset = Properties.Settings.Default.lastUsedLayoutPreset;
            if (lastUsedPreset < 1 || lastUsedPreset > 4)
            {
                lastUsedPreset = 1;
            }
            
            var preset = LayoutPreset.LoadFromSettings(lastUsedPreset);
            if (preset.Enabled)
            {
                ApplyLayoutPreset(preset, lastUsedPreset);
            }
            
            // Trigger refresh so border windows are hidden for inactive controllers (if not showing all borders)
            SettingChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Clear the list of switched controllers and reset their highlighting
        /// </summary>
        internal void ClearSwitchedControllers()
        {
            foreach (var controller in _switchedControllers)
            {
                var borderWnd = GetBorderWindow(controller);
                if (borderWnd != null)
                {
                    borderWnd.SwitchingSelected = false;
                    borderWnd.SwitchingSwitched = false;
                }
            }
            _switchedControllers.Clear();
        }

        private BorderWnd GetBorderWindow(ToontownController controller)
        {
            // Use reflection to access the private _borderWnd field
            var field = typeof(ToontownController).GetField("_borderWnd", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            return field?.GetValue(controller) as BorderWnd;
        }

        private ToontownController GetControllerUnderCursor()
        {
            Point cursorPos;
            if (!Win32.GetCursorPos(out cursorPos))
                return null;

            // Find which controller's window the cursor is over
            foreach (var controller in AllControllersWithWindows)
            {
                Point clientAreaLocation = Win32.GetWindowClientAreaLocation(controller.WindowHandle);
                Size clientAreaSize = controller.WindowSize;

                if (cursorPos.X >= clientAreaLocation.X && cursorPos.X < clientAreaLocation.X + clientAreaSize.Width &&
                    cursorPos.Y >= clientAreaLocation.Y && cursorPos.Y < clientAreaLocation.Y + clientAreaSize.Height)
                {
                    return controller;
                }
            }

            return null;
        }

        /// <summary>
        /// Get the window handle under the cursor that isn't already assigned to a controller
        /// </summary>
        private IntPtr GetUnassignedWindowHandleUnderCursor()
        {
            Point cursorPos;
            if (!Win32.GetCursorPos(out cursorPos))
                return IntPtr.Zero;

            // Get the window at the cursor position
            IntPtr hWnd = Win32.WindowFromPoint(cursorPos);
            if (hWnd == IntPtr.Zero)
                return IntPtr.Zero;

            // Get the root window (top-level window)
            IntPtr rootWnd = Win32.GetAncestor(hWnd, Win32.GetAncestorFlags.GetRoot);
            if (rootWnd == IntPtr.Zero)
                return IntPtr.Zero;

            // Check if this window is already assigned to a controller
            var currentlyAssignedHandles = new HashSet<IntPtr>(
                AllControllersWithWindows.Select(c => c.WindowHandle).Where(h => h != IntPtr.Zero)
            );

            if (currentlyAssignedHandles.Contains(rootWnd))
                return IntPtr.Zero;

            // Verify the window is visible and valid
            if (!Win32.IsWindowVisible(rootWnd) || !Win32.IsWindow(rootWnd))
                return IntPtr.Zero;

            return rootWnd;
        }

        private void SwitchWindows(ToontownController controller1, ToontownController controller2)
        {
            if (controller1 == null || controller2 == null || controller1 == controller2)
                return;

            // Get current window handles
            IntPtr handle1 = controller1.WindowHandle;
            IntPtr handle2 = controller2.WindowHandle;

            if (handle1 == IntPtr.Zero || handle2 == IntPtr.Zero)
                return;

            // Only swap window handle assignments (group/pair assignments)
            // Don't move or resize windows - let user apply layout presets manually if needed
            controller1.WindowHandle = handle2;
            controller2.WindowHandle = handle1;

            // Mark these controllers as switched so they stay yellow until layout/resize
            _switchedControllers.Add(controller1);
            _switchedControllers.Add(controller2);

            // Update border positions after switching
            System.Windows.Forms.Application.DoEvents();
            System.Threading.Thread.Sleep(10);
            controller1.UpdateBorderPosition();
            controller2.UpdateBorderPosition();
        }

        internal ControllerGroup AddControllerGroup()
        {
            ControllerGroup group = new ControllerGroup(ControllerGroups.Count + 1);

            group.ControllerWindowActivated += Controller_WindowActivated;
            group.ControllerWindowDeactivated += Controller_WindowDeactivated;
            group.ControllerWindowHandleChanged += Controller_WindowHandleChanged;
            group.ControllerShouldActivate += Controller_ShouldActivate;
            group.MouseEvent += Controller_MouseEvent;

            ControllerGroups.Add(group);
            
            // Update and save numberOfGroups setting to persist groups between sessions
            Properties.Settings.Default.numberOfGroups = ControllerGroups.Count;
            Properties.Settings.Default.Save();
            
            GroupsChanged?.Invoke(this, EventArgs.Empty);

            return group;
        }

        private void Controller_ShouldActivate(object sender, EventArgs e)
        {
            ToontownController controller = sender as ToontownController;

            if (!ActiveControllers.Contains(controller))
            {
                switch (CurrentMode)
                {
                    case MulticontrollerMode.Group:
                        ControllerGroup group = ControllerGroups.First(g => g.AllControllers.Contains(controller));

                        CurrentGroupIndex = ControllerGroups.IndexOf(group);
                        break;
                }
            }
        }

        private void Controller_MouseEvent(object sender, Message m)
        {
            ProcessInput(m.Msg, m.WParam, m.LParam, sender as ToontownController);
        }

        internal void RemoveControllerGroup(int index)
        {
            ControllerGroup controllerGroup = ControllerGroups[index];
            controllerGroup.Dispose();
            ControllerGroups.Remove(controllerGroup);
            
            // Update and save numberOfGroups setting to persist groups between sessions
            // Ensure at least one group remains
            if (ControllerGroups.Count > 0)
            {
                Properties.Settings.Default.numberOfGroups = ControllerGroups.Count;
                Properties.Settings.Default.Save();
            }
            
            GroupsChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// The main input processor. All input to the multicontroller window ends up here.
        /// </summary>
        /// <returns>Whether the input is discarded (doesn't reach its intended destination)</returns>
        internal bool ProcessInput(int msgCode, IntPtr wParam, IntPtr lParam, ToontownController sourceController = null) 
        {
            Win32.WM msg = (Win32.WM)msgCode;
            bool isKeyboardInput = false;
            bool isMouseInput = false;
            Keys keysPressed = Keys.None;

            switch (msg)
            {
                case Win32.WM.KEYDOWN:
                case Win32.WM.KEYUP:
                case Win32.WM.SYSKEYDOWN:
                case Win32.WM.SYSKEYUP:
                    isKeyboardInput = true;
                    keysPressed = (Keys)wParam;
                    break;
                case Win32.WM.HOTKEY:
                    isKeyboardInput = true;
                    keysPressed = (Keys)(lParam.ToInt32() >> 16);
                    break;
                case Win32.WM.MOUSEMOVE:
                case Win32.WM.LBUTTONDOWN:
                case Win32.WM.LBUTTONUP:
                case Win32.WM.RBUTTONDOWN:
                case Win32.WM.RBUTTONUP:
                case Win32.WM.MBUTTONDOWN:
                case Win32.WM.MBUTTONUP:
                case Win32.WM.MOUSEHOVER:
                case Win32.WM.MOUSEWHEEL:
                case Win32.WM.MOUSELEAVE:
                    isMouseInput = true;
                    break;
            }

            if (isMouseInput)
            {
                return ProcessMouseInput(msg, wParam, lParam, sourceController);
            }
            else if (isKeyboardInput)
            {
                return ProcessMetaKeyboardInput(msg, keysPressed)
                    || ProcessKeyboardInput(msg, wParam, lParam);
            }

            return false;
        }

        /// <summary>
        /// Process keyboard input for meta actions (hotkeys, changing groups, etc.)
        /// </summary>
        /// <returns>True the input was handled as a meta input</returns>
        private bool ProcessMetaKeyboardInput(Win32.WM msg, Keys keysPressed)
        {
            if (keysPressed == (Keys)Properties.Settings.Default.modeKeyCode)
            {
                if (msg == Win32.WM.HOTKEY || msg == Win32.WM.KEYDOWN)
                {
                    // Check if any modifiers are currently pressed - if so, don't switch modes, let it pass through to games
                    Keys currentModifiers = System.Windows.Forms.Control.ModifierKeys;
                    bool hasModifiers = (currentModifiers & (Keys.Shift | Keys.Control | Keys.Alt)) != Keys.None;
                    
                    if (hasModifiers)
                    {
                        // Modifiers are pressed - don't switch modes, return false to let it pass through to ProcessKeyboardInput
                        return false;
                    }
                    
                    if (IsActive)
                    {
                        List<MulticontrollerMode> availableModesToCycle = new List<MulticontrollerMode>();

                        if (Properties.Settings.Default.groupModeCycleWithModeHotkey)
                        {
                            availableModesToCycle.Add(MulticontrollerMode.Group);
                        }

                        if (Properties.Settings.Default.mirrorModeCycleWithModeHotkey)
                        {
                            availableModesToCycle.Add(MulticontrollerMode.MirrorAll);
                        }

                        if (Properties.Settings.Default.allGroupModeCycleWithModeHotkey)
                        {
                            availableModesToCycle.Add(MulticontrollerMode.AllGroup);
                        }

                        int currentModeIndex = availableModesToCycle.IndexOf(CurrentMode);

                        if (currentModeIndex >= 0)
                        {
                            currentModeIndex = (currentModeIndex + 1) % availableModesToCycle.Count;

                            CurrentMode = availableModesToCycle[currentModeIndex];
                        }
                        else if (availableModesToCycle.Count > 0)
                        {
                            CurrentMode = availableModesToCycle[0];
                        }
                    }
                    else
                    {
                        ShouldActivate?.Invoke(this, EventArgs.Empty);
                    }
                }
            }
            else if (keysPressed == (Keys)Properties.Settings.Default.groupModeKeyCode)
            {
                CurrentMode = MulticontrollerMode.Group;
            }
            else if (keysPressed == (Keys)Properties.Settings.Default.mirrorModeKeyCode)
            {
                CurrentMode = MulticontrollerMode.MirrorAll;
            }
            else if (keysPressed == (Keys)Properties.Settings.Default.controlAllGroupsKeyCode)
            {
                CurrentMode = MulticontrollerMode.AllGroup;
            }
            else if (keysPressed == (Keys)Properties.Settings.Default.replicateMouseKeyCode
                && Properties.Settings.Default.replicateMouseKeyCode != 0)
            {
                // Instant Multi-Click: Send a click to all windows at current cursor position
                if (msg == Win32.WM.KEYDOWN || msg == Win32.WM.HOTKEY)
                {
                    // Prevent key repeat - only trigger on initial press
                    if (multiClickKeyPressed)
                    {
                        return true;
                    }
                    multiClickKeyPressed = true;
                    
                    // Get the current global cursor position FIRST
                    Point cursorPos = System.Windows.Forms.Control.MousePosition;
                    
                    // Find which window the cursor is over to get relative coordinates
                    int relativeX = 0;
                    int relativeY = 0;
                    bool foundCursorWindow = false;
                    
                    foreach (ToontownController controller in AllControllersWithWindows)
                    {
                        // Get client area location (screen coordinates of the game area, excluding title bar/borders)
                        Point clientAreaLocation = Win32.GetWindowClientAreaLocation(controller.WindowHandle);
                        Size clientAreaSize = controller.WindowSize;
                        
                        // Check if cursor is within this window's client area bounds
                        if (cursorPos.X >= clientAreaLocation.X && cursorPos.X < clientAreaLocation.X + clientAreaSize.Width &&
                            cursorPos.Y >= clientAreaLocation.Y && cursorPos.Y < clientAreaLocation.Y + clientAreaSize.Height)
                        {
                            relativeX = cursorPos.X - clientAreaLocation.X;
                            relativeY = cursorPos.Y - clientAreaLocation.Y;
                            foundCursorWindow = true;
                            break;
                        }
                    }
                    
                    // If cursor is not over any window, don't do anything
                    if (!foundCursorWindow)
                    {
                        // Just activate if not active, but don't send any clicks
                        if (!IsActive)
                        {
                            ShouldActivate?.Invoke(this, EventArgs.Empty);
                            CurrentMode = MulticontrollerMode.MirrorAll;
                        }
                        return true;
                    }
                    
                    // If multicontroller is not active, activate it and switch to mirror mode
                    if (!IsActive)
                    {
                        ShouldActivate?.Invoke(this, EventArgs.Empty);
                        CurrentMode = MulticontrollerMode.MirrorAll;
                        
                        // Small delay to ensure activation completes before sending clicks
                        System.Threading.Thread.Sleep(50);
                    }
                    
                    // Send click to active controllers only (respects group selection)
                    IEnumerable<ToontownController> affectedControllers = ActiveControllers;
                    
                    foreach (ToontownController controller in affectedControllers)
                    {
                        if (controller.HasWindow)
                        {
                            // Use the same relative position for all windows
                            // Create lParam with x,y coordinates (x in low word, y in high word)
                            IntPtr clickLParam = (IntPtr)((relativeY << 16) | (relativeX & 0xFFFF));
                            
                            // Send left button down and up (instant click)
                            controller.PostMessage(Win32.WM.LBUTTONDOWN, (IntPtr)Win32.MK_LBUTTON, clickLParam);
                            controller.PostMessage(Win32.WM.LBUTTONUP, IntPtr.Zero, clickLParam);
                        }
                    }
                }
                else if (msg == Win32.WM.KEYUP)
                {
                    // Reset flag when key is released
                    multiClickKeyPressed = false;
                }
            }
            else if (keysPressed == (Keys)Properties.Settings.Default.controlAllGroupsKeyCode)
            {
                if (msg == Win32.WM.KEYDOWN && CurrentMode != MulticontrollerMode.AllGroup)
                {
                    CurrentMode = MulticontrollerMode.AllGroup;
                }
            }
            else if (keysPressed == Keys.Menu) // Alt key
            {
                // Handle Alt key for switching mode (only if enabled)
                if (!Properties.Settings.Default.switchingModeEnabled)
                {
                    return false; // Don't handle Alt if switching mode is disabled
                }
                
                if (msg == Win32.WM.SYSKEYDOWN || msg == Win32.WM.KEYDOWN)
                {
                    if (!_switchingMode)
                    {
                        // Enter switching mode (allow even when not active or no windows are connected)
                        // Activate multicontroller if not already active
                        if (!IsActive)
                        {
                            ShouldActivate?.Invoke(this, EventArgs.Empty);
                        }
                        
                        _switchingMode = true;
                        _firstSelectedController = null;
                        _secondSelectedController = null;
                        _markedForRemoval.Clear(); // Clear removal marks when entering switching mode
                        _switchingModeTimer.Start();
                        
                        // Install global mouse hook to block clicks during switching mode
                        InstallMouseHook();
                        
                        // Trigger refresh so all border windows are shown
                        SettingChanged?.Invoke(this, EventArgs.Empty);
                        UpdateSwitchingModeDisplay();
                        return true;
                    }
                }
                else if (msg == Win32.WM.SYSKEYUP || msg == Win32.WM.KEYUP)
                {
                    if (_switchingMode)
                    {
                        // Exit switching mode
                        ExitSwitchingMode();
                        return true;
                    }
                }
            }
            else if (_switchingMode && keysPressed == (Keys)Properties.Settings.Default.switchingModeRemoveKeyCode)
            {
                // Handle remove key in switching mode - toggle removal mark on the controller under cursor
                if (msg == Win32.WM.KEYDOWN || msg == Win32.WM.SYSKEYDOWN)
                {
                    var controllerUnderCursor = GetControllerUnderCursor();
                    if (controllerUnderCursor != null && controllerUnderCursor.HasWindow)
                    {
                        // Toggle removal mark
                        if (_markedForRemoval.Contains(controllerUnderCursor))
                        {
                            // Remove from removal list (unmark)
                            _markedForRemoval.Remove(controllerUnderCursor);
                        }
                        else
                        {
                            // Add to removal list (mark for removal)
                            _markedForRemoval.Add(controllerUnderCursor);
                            
                            // Clear selection if this controller was selected
                            if (_firstSelectedController == controllerUnderCursor)
                            {
                                _firstSelectedController = null;
                            }
                            if (_secondSelectedController == controllerUnderCursor)
                            {
                                _secondSelectedController = null;
                            }
                        }
                        
                        UpdateSwitchingModeDisplay();
                    }
                    return true;
                }
            }
            else if (_switchingMode && keysPressed == (Keys)Properties.Settings.Default.switchingModeSwitchKeyCode)
            {
                // Handle switch/select key in switching mode
                if (msg == Win32.WM.KEYDOWN || msg == Win32.WM.SYSKEYDOWN)
                {
                    var controllerUnderCursor = GetControllerUnderCursor();
                    if (controllerUnderCursor != null && controllerUnderCursor.HasWindow)
                    {
                        // If clicking on a window marked for removal, unmark it first
                        if (_markedForRemoval.Contains(controllerUnderCursor))
                        {
                            _markedForRemoval.Remove(controllerUnderCursor);
                        }
                        
                        if (_firstSelectedController == null)
                        {
                            // Select first window
                            _firstSelectedController = controllerUnderCursor;
                            UpdateSwitchingModeDisplay();
                        }
                        else if (_secondSelectedController == null && controllerUnderCursor != _firstSelectedController)
                        {
                            // Select second window and switch
                            _secondSelectedController = controllerUnderCursor;
                            SwitchWindows(_firstSelectedController, _secondSelectedController);
                            
                            // Reset selection state but keep switching mode active (Alt is still held)
                            _firstSelectedController = null;
                            _secondSelectedController = null;
                            UpdateSwitchingModeDisplay();
                        }
                        else if (controllerUnderCursor == _firstSelectedController)
                        {
                            // Pressing the same window again deselects it
                            _firstSelectedController = null;
                            UpdateSwitchingModeDisplay();
                        }
                        else
                        {
                            // Just update display if we unmarked a removal
                            UpdateSwitchingModeDisplay();
                        }
                    }
                    return true;
                }
            }
            else if (_switchingMode && (keysPressed >= Keys.D1 && keysPressed <= Keys.D9
                || keysPressed >= Keys.NumPad1 && keysPressed <= Keys.NumPad9))
            {
                // Handle number keys in switching mode to assign windows to specific numbers
                if (msg == Win32.WM.KEYDOWN || msg == Win32.WM.SYSKEYDOWN)
                {
                    IntPtr windowHandle = IntPtr.Zero;
                    
                    // First try to get an already-assigned controller under cursor
                    var controllerUnderCursor = GetControllerUnderCursor();
                    if (controllerUnderCursor != null && controllerUnderCursor.HasWindow)
                    {
                        windowHandle = controllerUnderCursor.WindowHandle;
                    }
                    else
                    {
                        // If no assigned controller, try to find an unassigned window under cursor
                        windowHandle = GetUnassignedWindowHandleUnderCursor();
                    }

                    if (windowHandle != IntPtr.Zero)
                    {
                        // First, remove this window from all existing controllers
                        foreach (var controller in AllControllers)
                        {
                            if (controller.WindowHandle == windowHandle)
                            {
                                controller.WindowHandle = IntPtr.Zero;
                            }
                        }

                        // Convert key to number (1-9)
                        int number;
                        if (keysPressed >= Keys.D1 && keysPressed <= Keys.D9)
                        {
                            number = keysPressed - Keys.D0;
                        }
                        else
                        {
                            number = keysPressed - Keys.NumPad0;
                        }

                        // Calculate group and type from number
                        // Number 1 = Group 1 Left, Number 2 = Group 1 Right, Number 3 = Group 2 Left, etc.
                        int groupNumber = ((number - 1) / 2) + 1;
                        ControllerType targetType = (number % 2 == 1) ? ControllerType.Left : ControllerType.Right;

                        // Find or create the group
                        ControllerGroup targetGroup = ControllerGroups.FirstOrDefault(g => g.GroupNumber == groupNumber);
                        if (targetGroup == null)
                        {
                            // Create new groups until we have the target group
                            while (ControllerGroups.Count < groupNumber)
                            {
                                AddControllerGroup();
                            }
                            targetGroup = ControllerGroups[groupNumber - 1];
                        }

                        // Find the first unused controller of the target type in this group
                        ToontownController targetController = null;
                        foreach (var pair in targetGroup.ControllerPairs.OrderBy(p => p.PairNumber))
                        {
                            var candidate = (targetType == ControllerType.Left) ? pair.LeftController : pair.RightController;
                            if (!candidate.HasWindow)
                            {
                                targetController = candidate;
                                break;
                            }
                        }

                        // If no unused controller found, create a new pair
                        if (targetController == null)
                        {
                            var newPair = targetGroup.AddPair();
                            targetController = (targetType == ControllerType.Left) ? newPair.LeftController : newPair.RightController;
                        }

                        // Assign the window to the target controller
                        if (targetController != null)
                        {
                            targetController.WindowHandle = windowHandle;
                            UpdateSwitchingModeDisplay();
                        }
                    }
                    return true;
                }
            }
            else if (CurrentMode == MulticontrollerMode.Group
                && !_switchingMode  // Don't handle group switching when in switching mode
                && ControllerGroups.Count > 1
                && (keysPressed >= Keys.D0 && keysPressed <= Keys.D9
                    || keysPressed >= Keys.NumPad0 && keysPressed <= Keys.NumPad9))
            {
                // Change groups while in group mode
                int index;

                if (keysPressed >= Keys.D0 && keysPressed <= Keys.D9)
                {
                    index = 9 - (Keys.D9 - keysPressed);
                }
                else
                {
                    index = 9 - (Keys.NumPad9 - keysPressed);
                }

                index = index == 0 ? 9 : index - 1;

                if (ControllerGroups.Count > index)
                {
                    CurrentGroupIndex = index;
                }
            }
            else if (keysPressed == (Keys)Properties.Settings.Default.zeroPowerThrowKeyCode 
                && Properties.Settings.Default.zeroPowerThrowKeyCode != 0)
            {
                // Handle Zero Power Throw Hotkey
                if (msg == Win32.WM.KEYDOWN || msg == Win32.WM.HOTKEY)
                {
                    // Prevent key repeat - only trigger on initial press
                    if (zeroPowerThrowKeyPressed)
                    {
                        return true;
                    }
                    zeroPowerThrowKeyPressed = true;
                    
                    // Find the Throw key from bindings
                    var keyBindings = Properties.SerializedSettings.Default.Bindings;
                    var throwBinding = keyBindings.FirstOrDefault(b => b.Title == "Throw");
                    
                    if (throwBinding != null)
                    {
                        if (IsActive)
                        {
                            // Multicontroller is active: Send to all active controllers
                            IEnumerable<ToontownController> affectedControllers = ActiveControllers;
                            
                            // Send instant tap of the throw key to all active controllers
                            affectedControllers.ToList().ForEach(c => {
                                Keys throwKey = Keys.None;
                                
                                // Determine which throw key to use based on controller type
                                if (c.Type == ControllerType.Left && throwBinding.LeftToonKey != Keys.None)
                                {
                                    throwKey = throwBinding.LeftToonKey;
                                }
                                else if (c.Type == ControllerType.Right && throwBinding.RightToonKey != Keys.None)
                                {
                                    throwKey = throwBinding.RightToonKey;
                                }
                                
                                if (throwKey != Keys.None)
                                {
                                    // Send both KEYDOWN and KEYUP immediately without delay for 0% power
                                    c.PostMessage(Win32.WM.KEYDOWN, (IntPtr)throwKey, IntPtr.Zero);
                                    c.PostMessage(Win32.WM.KEYUP, (IntPtr)throwKey, IntPtr.Zero);
                                }
                            });
                        }
                        else
                        {
                            // Multicontroller is NOT active: activate in MirrorAll mode and send throw to all windows
                            ShouldActivate?.Invoke(this, EventArgs.Empty);
                            CurrentMode = MulticontrollerMode.MirrorAll;

                            foreach (var controller in AllControllersWithWindows)
                            {
                                Keys throwKey = Keys.None;
                                
                                // Determine which throw key to use based on controller type
                                if (controller.Type == ControllerType.Left && throwBinding.LeftToonKey != Keys.None)
                                {
                                    throwKey = throwBinding.LeftToonKey;
                                }
                                else if (controller.Type == ControllerType.Right && throwBinding.RightToonKey != Keys.None)
                                {
                                    throwKey = throwBinding.RightToonKey;
                                }
                                
                                if (throwKey != Keys.None)
                                {
                                    // Send both KEYDOWN and KEYUP immediately without delay for 0% power
                                    controller.PostMessage(Win32.WM.KEYDOWN, (IntPtr)throwKey, IntPtr.Zero);
                                    controller.PostMessage(Win32.WM.KEYUP, (IntPtr)throwKey, IntPtr.Zero);
                                }
                            }
                        }
                    }
                }
                else if (msg == Win32.WM.KEYUP)
                {
                    // Reset flag when key is released
                    zeroPowerThrowKeyPressed = false;
                }
            }
            else
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Process mouse input
        /// </summary>
        /// <returns>True if the input was handled</returns>
        private bool ProcessMouseInput(Win32.WM msg, IntPtr wParam, IntPtr lParam, ToontownController sourceController)
        {
            // Handle mouse clicks in switching mode for window selection/switching
            // Only handle mouse clicks if the switch key is set to a mouse button
            int switchKeyCode = Properties.Settings.Default.switchingModeSwitchKeyCode;
            bool isMouseButtonSwitch = switchKeyCode == 1 || switchKeyCode == 2 || switchKeyCode == 4;
            bool isMatchingMouseButton = (msg == Win32.WM.LBUTTONDOWN && switchKeyCode == 1) ||
                                        (msg == Win32.WM.RBUTTONDOWN && switchKeyCode == 2) ||
                                        (msg == Win32.WM.MBUTTONDOWN && switchKeyCode == 4);
            
            if (_switchingMode && isMatchingMouseButton)
            {
                var controllerUnderCursor = GetControllerUnderCursor();
                if (controllerUnderCursor != null)
                {
                    // If clicking on a window marked for removal, unmark it first
                    if (_markedForRemoval.Contains(controllerUnderCursor))
                    {
                        _markedForRemoval.Remove(controllerUnderCursor);
                    }
                    
                    if (_firstSelectedController == null)
                    {
                        // Select first window
                        _firstSelectedController = controllerUnderCursor;
                        UpdateSwitchingModeDisplay();
                    }
                    else if (_secondSelectedController == null && controllerUnderCursor != _firstSelectedController)
                    {
                        // Select second window and switch
                        _secondSelectedController = controllerUnderCursor;
                        SwitchWindows(_firstSelectedController, _secondSelectedController);
                        
                        // Reset selection state but keep switching mode active (Alt is still held)
                        _firstSelectedController = null;
                        _secondSelectedController = null;
                        UpdateSwitchingModeDisplay();
                    }
                    else if (controllerUnderCursor == _firstSelectedController)
                    {
                        // Clicking the same window again deselects it
                        _firstSelectedController = null;
                        UpdateSwitchingModeDisplay();
                    }
                    else
                    {
                        // Just update display if we unmarked a removal
                        UpdateSwitchingModeDisplay();
                    }
                }
                // Consume the click so it doesn't get sent to the games
                return true;
            }
            
            // Block all mouse input in switching mode (don't send clicks to games)
            if (_switchingMode)
            {
                return true;
            }
            
            // Mouse input processing removed - multiclick is now instant via hotkey
            return false;
        }

        /// <summary>
        /// Process keyboard input
        /// </summary>
        /// <returns>True if the input was handled</returns>
        private bool ProcessKeyboardInput(Win32.WM msg, IntPtr wParam, IntPtr lParam)
        {
            // Block normal input processing when in switching mode
            if (_switchingMode)
            {
                return true; // Consume all input in switching mode
            }

            if (IsActive)
            {
                Keys keysPressed = (Keys)wParam;

                IEnumerable<ToontownController> affectedControllers = ActiveControllers;
                List<Keys> keysToPress = new List<Keys>();
                
                if (CurrentMode == MulticontrollerMode.Group 
                    || CurrentMode == MulticontrollerMode.AllGroup)
                {
                    if (leftKeys.ContainsKey(keysPressed) && !rightKeys.ContainsKey(keysPressed))
                    {
                        affectedControllers = affectedControllers.Where(c => c.Type == ControllerType.Left);

                        keysToPress.AddRange(leftKeys[keysPressed]);
                    }
                    else if (!leftKeys.ContainsKey(keysPressed) && rightKeys.ContainsKey(keysPressed))
                    {
                        affectedControllers = affectedControllers.Where(c => c.Type == ControllerType.Right);

                        keysToPress.AddRange(rightKeys[keysPressed]);
                    }
                    else if (leftKeys.ContainsKey(keysPressed) && rightKeys.ContainsKey(keysPressed))
                    {
                        keysToPress.AddRange(leftKeys[keysPressed]);
                        keysToPress.AddRange(rightKeys[keysPressed]);
                    }
                }
                
                if (CurrentMode == MulticontrollerMode.MirrorAll)
                {
                    affectedControllers.ToList().ForEach(c => c.PostMessage(msg, wParam, lParam));
                }
                else
                {
                    foreach (Keys actualKey in keysToPress)
                        affectedControllers.ToList().ForEach(c => c.PostMessage(msg, (IntPtr)actualKey, lParam));
                }

                return true;
            }

            return false;
        }

        private void Controller_WindowHandleChanged(object sender, EventArgs e)
        {
            if (!AllControllersWithWindows.Any(c => c.IsWindowActive))
            {
                AllWindowsInactive?.Invoke(this, EventArgs.Empty);
            }
        }

        private void Controller_WindowActivated(object sender, EventArgs e)
        {
            WindowActivated?.Invoke(this, EventArgs.Empty);
        }

        private void Controller_WindowDeactivated(object sender, EventArgs e)
        {
            if (!AllControllersWithWindows.Any(c => c.IsWindowActive))
            {
                AllWindowsInactive?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Automatically find and assign windows from recognized game executables
        /// </summary>
        public void AutoFindAndAssignWindows()
        {
            var executableNames = Properties.Settings.Default.autoFindExecutables
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(e => e.Trim())
                .Where(e => !string.IsNullOrEmpty(e))
                .ToList();

            if (executableNames.Count == 0)
                return;

            // Get currently assigned window handles to check if we're adding new ones
            var currentlyAssignedHandles = new HashSet<IntPtr>(
                AllControllersWithWindows.Select(c => c.WindowHandle).Where(h => h != IntPtr.Zero)
            );

            // Find all processes matching the executable names and get their main windows
            var foundWindows = new List<IntPtr>();
            var processNames = new HashSet<string>(executableNames, StringComparer.OrdinalIgnoreCase);
            
            // Also create a set without .exe extension for matching
            var processNamesNoExt = new HashSet<string>(
                executableNames.Select(e => System.IO.Path.GetFileNameWithoutExtension(e)), 
                StringComparer.OrdinalIgnoreCase
            );
            
            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    // Check if this process matches one of our executable names (with or without .exe)
                    bool matches = processNames.Contains(process.ProcessName) || 
                                   processNamesNoExt.Contains(process.ProcessName);
                    
                    if (matches)
                    {
                        // Get the main window handle for this process
                        IntPtr mainWindowHandle = process.MainWindowHandle;
                        
                        // If MainWindowHandle is zero, try to find the window using EnumWindows
                        if (mainWindowHandle == IntPtr.Zero)
                        {
                            // Find the first visible window for this process
                            Win32.EnumWindows((hWnd, lParam) =>
                            {
                                uint processId;
                                Win32.GetWindowThreadProcessId(hWnd, out processId);
                                if (processId == process.Id && Win32.IsWindowVisible(hWnd))
                                {
                                    mainWindowHandle = hWnd;
                                    return false; // Stop enumeration
                                }
                                return true; // Continue enumeration
                            }, IntPtr.Zero);
                        }
                        
                        // Only add if it's a valid window handle and the window is visible
                        if (mainWindowHandle != IntPtr.Zero && Win32.IsWindowVisible(mainWindowHandle))
                        {
                            // Verify the window is still valid
                            if (Win32.IsWindow(mainWindowHandle))
                            {
                                foundWindows.Add(mainWindowHandle);
                            }
                        }
                    }
                }
                catch
                {
                    // Process might have exited or we don't have access, ignore
                }
            }

            if (foundWindows.Count == 0)
                return;

            // Filter out windows that are already assigned
            var newWindows = foundWindows.Where(h => !currentlyAssignedHandles.Contains(h)).ToList();

            // If no new windows to add, do nothing
            if (newWindows.Count == 0)
                return;

            // Assign windows to controllers in order: Group 1 Left, Group 1 Right, Group 2 Left, Group 2 Right, etc.
            // Only use the first pair (PairNumber == 1) in each group
            int newWindowIndex = 0;
            
            // Iterate through all groups in order
            foreach (var group in ControllerGroups.OrderBy(g => g.GroupNumber))
            {
                // Only use the first pair in each group (PairNumber == 1)
                var firstPair = group.ControllerPairs.FirstOrDefault(p => p.PairNumber == 1);
                if (firstPair == null)
                {
                    // If no first pair exists, create one
                    firstPair = group.AddPair();
                }

                // Try to assign to Left controller first
                if (!firstPair.LeftController.HasWindow && newWindowIndex < newWindows.Count)
                {
                    firstPair.LeftController.WindowHandle = newWindows[newWindowIndex];
                    newWindowIndex++;
                }

                // Then try to assign to Right controller
                if (!firstPair.RightController.HasWindow && newWindowIndex < newWindows.Count)
                {
                    firstPair.RightController.WindowHandle = newWindows[newWindowIndex];
                    newWindowIndex++;
                }

                // If we've assigned all windows, stop
                if (newWindowIndex >= newWindows.Count)
                    break;
            }

            // If there are still new windows to assign, create new groups and assign them
            while (newWindowIndex < newWindows.Count)
            {
                // Create a new group
                var newGroup = AddControllerGroup();
                var firstPair = newGroup.ControllerPairs[0]; // New groups always have at least one pair

                // Assign to Left controller
                firstPair.LeftController.WindowHandle = newWindows[newWindowIndex];
                newWindowIndex++;

                // If there are more windows, assign to Right controller
                if (newWindowIndex < newWindows.Count)
                {
                    firstPair.RightController.WindowHandle = newWindows[newWindowIndex];
                    newWindowIndex++;
                }
            }

            // Force update all border positions after assignment
            // This ensures borders are correctly positioned even if windows haven't been moved yet
            // Process window messages to allow window assignment to complete
            System.Windows.Forms.Application.DoEvents();
            System.Threading.Thread.Sleep(10); // Small delay to ensure windows are ready
            System.Windows.Forms.Application.DoEvents();
            foreach (var controller in AllControllersWithWindows)
            {
                controller.UpdateBorderPosition();
            }

            // Automatically set to mirror mode and apply the last used layout preset (or preset 1 if none used)
            CurrentMode = MulticontrollerMode.MirrorAll;
            
            // Get the last used preset number, defaulting to 1 if none has been used
            int lastUsedPreset = Properties.Settings.Default.lastUsedLayoutPreset;
            if (lastUsedPreset < 1 || lastUsedPreset > 4)
            {
                lastUsedPreset = 1;
            }
            
            // Load and apply the last used preset
            var preset = LayoutPreset.LoadFromSettings(lastUsedPreset);

            
            if (preset.Enabled)
            {
                ApplyLayoutPreset(preset, lastUsedPreset);
            }
        }

        /// <summary>
        /// Apply a layout preset to all windows with handles
        /// </summary>
        public void ApplyLayoutPreset(LayoutPreset preset, int? presetNumber = null)
        {
            if (preset == null || !preset.Enabled)
                return;
            
            // Track the last used preset number
            if (presetNumber.HasValue && presetNumber.Value >= 1 && presetNumber.Value <= 4)
            {
                Properties.Settings.Default.lastUsedLayoutPreset = presetNumber.Value;
                Properties.Settings.Default.Save();
            }

            var controllersWithWindows = AllControllersWithWindows.ToList();
            if (controllersWithWindows.Count == 0)
                return;

            // Order controllers based on priority mode
            // Pairs first (default): Group -> Pair -> Type (Left before Right)
            // Lefts first: Group -> Type (Left before Right) -> Pair
            bool leftsFirst = Properties.Settings.Default.layoutPriorityLeftsFirst;
            if (leftsFirst)
            {
                // Lefts first: Group 1 Pair 1 Left, Group 1 Pair 2 Left, Group 1 Pair 1 Right, Group 1 Pair 2 Right
                controllersWithWindows = controllersWithWindows
                    .OrderBy(c => c.GroupNumber)
                    .ThenBy(c => c.Type) // Left (0) before Right (1)
                    .ThenBy(c => c.PairNumber)
                    .ToList();
            }
            else
            {
                // Pairs first (default): Group 1 Pair 1 Left, Group 1 Pair 1 Right, Group 1 Pair 2 Left, Group 1 Pair 2 Right
                controllersWithWindows = controllersWithWindows
                    .OrderBy(c => c.GroupNumber)
                    .ThenBy(c => c.PairNumber)
                    .ThenBy(c => c.Type) // Left (0) before Right (1)
                    .ToList();
            }

            // Calculate grid layout (window size and positions)
            var (windowSize, positions) = preset.CalculateGridLayout(controllersWithWindows.Count);

            if (windowSize.IsEmpty || positions.Length == 0)
                return;

            // Apply layout to all windows
            for (int i = 0; i < controllersWithWindows.Count && i < positions.Length; i++)
            {
                var controller = controllersWithWindows[i];
                Point position = positions[i];

                // Use SetWindowPos to move and resize the window
                Win32.SetWindowPos(
                    controller.WindowHandle,
                    IntPtr.Zero,
                    position.X,
                    position.Y,
                    windowSize.Width,
                    windowSize.Height,
                    Win32.SetWindowPosFlags.ShowWindow | Win32.SetWindowPosFlags.DoNotActivate
                );
            }

            // Clear switched controllers list since layout has been applied
            ClearSwitchedControllers();

            // Force update all border positions after moving windows
            // WindowWatcher will eventually update them, but this ensures immediate correctness
            // Process window messages to allow SetWindowPos to complete
            System.Windows.Forms.Application.DoEvents();
            System.Threading.Thread.Sleep(10); // Small delay to ensure windows have moved
            System.Windows.Forms.Application.DoEvents();
            foreach (var controller in controllersWithWindows)
            {
                controller.UpdateBorderPosition();
            }
        }

        /// <summary>
        /// Toggle layout priority mode and reapply the last used layout preset
        /// </summary>
        public void ToggleLayoutPriority()
        {
            // Toggle the priority mode
            Properties.Settings.Default.layoutPriorityLeftsFirst = !Properties.Settings.Default.layoutPriorityLeftsFirst;
            Properties.Settings.Default.Save();

            // Get the last used preset number, defaulting to 1 if none has been used
            int lastUsedPreset = Properties.Settings.Default.lastUsedLayoutPreset;
            if (lastUsedPreset < 1 || lastUsedPreset > 4)
            {
                lastUsedPreset = 1;
            }

            // Load and reapply the last used preset with the new priority order
            var preset = LayoutPreset.LoadFromSettings(lastUsedPreset);
            if (preset.Enabled)
            {
                ApplyLayoutPreset(preset, lastUsedPreset);
            }
        }
        
        /// <summary>
        /// Install global low-level mouse hook to block clicks during switching mode
        /// </summary>
        private void InstallMouseHook()
        {
            if (_mouseHookHandle != IntPtr.Zero)
                return; // Hook already installed
            
            _hookInstance = this;
            if (_mouseHookProc == null)
            {
                _mouseHookProc = MouseHookProc;
            }
            
            IntPtr hModule = Win32.GetModuleHandle(null);
            _mouseHookHandle = Win32.SetWindowsHookEx(
                Win32.WH_MOUSE_LL,
                _mouseHookProc,
                hModule,
                0
            );
        }
        
        /// <summary>
        /// Uninstall global low-level mouse hook
        /// </summary>
        private void UninstallMouseHook()
        {
            if (_mouseHookHandle != IntPtr.Zero)
            {
                Win32.UnhookWindowsHookEx(_mouseHookHandle);
                _mouseHookHandle = IntPtr.Zero;
            }
            _hookInstance = null;
        }
        
        /// <summary>
        /// Low-level mouse hook procedure - blocks clicks during switching mode
        /// </summary>
        private static IntPtr MouseHookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            // If nCode is less than zero, we must pass the message to CallNextHookEx
            if (nCode < 0)
            {
                return Win32.CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
            }
            
            // Check if switching mode is active
            if (_hookInstance != null && _hookInstance._switchingMode)
            {
                int msg = wParam.ToInt32();
                
                // Block mouse clicks (left, right, middle button down)
                int switchKeyCode = Properties.Settings.Default.switchingModeSwitchKeyCode;
                bool isMatchingMouseButton = (msg == (int)Win32.WM.LBUTTONDOWN && switchKeyCode == 1) ||
                                            (msg == (int)Win32.WM.RBUTTONDOWN && switchKeyCode == 2) ||
                                            (msg == (int)Win32.WM.MBUTTONDOWN && switchKeyCode == 4);
                
                if (msg == (int)Win32.WM.LBUTTONDOWN || 
                    msg == (int)Win32.WM.RBUTTONDOWN || 
                    msg == (int)Win32.WM.MBUTTONDOWN)
                {
                    // Process matching mouse button clicks for selection/switching
                    if (isMatchingMouseButton)
                    {
                        // Get mouse position from hook structure
                        Win32.MSLLHOOKSTRUCT hookStruct = (Win32.MSLLHOOKSTRUCT)System.Runtime.InteropServices.Marshal.PtrToStructure(
                            lParam, typeof(Win32.MSLLHOOKSTRUCT));
                        
                        // Manually process the click for selection/switching
                        // We need to call ProcessMouseInput, but we need to convert the hook message to a window message
                        // For now, we'll handle it directly here
                        var controllerUnderCursor = _hookInstance.GetControllerUnderCursor();
                        if (controllerUnderCursor != null)
                        {
                            // If clicking on a window marked for removal, unmark it first
                            if (_hookInstance._markedForRemoval.Contains(controllerUnderCursor))
                            {
                                _hookInstance._markedForRemoval.Remove(controllerUnderCursor);
                            }
                            
                            if (_hookInstance._firstSelectedController == null)
                            {
                                // Select first window
                                _hookInstance._firstSelectedController = controllerUnderCursor;
                                _hookInstance.UpdateSwitchingModeDisplay();
                            }
                            else if (_hookInstance._secondSelectedController == null && 
                                     controllerUnderCursor != _hookInstance._firstSelectedController)
                            {
                                // Select second window and switch
                                _hookInstance._secondSelectedController = controllerUnderCursor;
                                _hookInstance.SwitchWindows(_hookInstance._firstSelectedController, _hookInstance._secondSelectedController);
                                
                                // Reset selection state but keep switching mode active (Alt is still held)
                                _hookInstance._firstSelectedController = null;
                                _hookInstance._secondSelectedController = null;
                                _hookInstance.UpdateSwitchingModeDisplay();
                            }
                            else if (controllerUnderCursor == _hookInstance._firstSelectedController)
                            {
                                // Clicking the same window again deselects it
                                _hookInstance._firstSelectedController = null;
                                _hookInstance.UpdateSwitchingModeDisplay();
                            }
                            else
                            {
                                // Just update display if we unmarked a removal
                                _hookInstance.UpdateSwitchingModeDisplay();
                            }
                        }
                    }
                    
                    // Block the click from reaching the game window
                    return (IntPtr)1; // Return non-zero to block the message
                }
            }
            
            // Pass the message to the next hook
            return Win32.CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
        }
    }

    
}
