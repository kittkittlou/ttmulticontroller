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

                    for (int i = 0; i < Properties.Settings.Default.numberOfGroups; i++)
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
                    case MulticontrollerMode.MirrorGroup:
                        return ControllerGroups[CurrentGroupIndex].AllControllers;
                    case MulticontrollerMode.Pair:
                        if (CurrentControllerPair != null)
                        {
                            return CurrentControllerPair.AllControllers;
                        }
                        break;
                    case MulticontrollerMode.AllGroup:
                    case MulticontrollerMode.MirrorAll:
                        return AllControllers;
                    case MulticontrollerMode.MirrorIndividual:
                        if (CurrentIndividualController != null)
                        {
                            return new[] { CurrentIndividualController };
                        }
                        break;
                    case MulticontrollerMode.Focused:
                        // In focused mode, return all windows (movement filtering happens in ProcessKeyboardInput)
                        return AllControllersWithWindows;
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
                else if (CurrentMode == MulticontrollerMode.Pair)
                {
                    if (CurrentControllerPair != null)
                    {
                        return new[] { CurrentControllerPair?.LeftController };
                    } 
                    else
                    {
                        return new ToontownController[] { };
                    }
                }
                else
                {
                    return ControllerGroups[CurrentGroupIndex].LeftControllers;
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
                    return ControllerGroups[CurrentGroupIndex].RightControllers;
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
                    
                    // If becoming inactive while in Focused mode, reset to MirrorAll
                    if (!_isActive && CurrentMode == MulticontrollerMode.Focused)
                    {
                        _focusedController = null;
                        _currentMode = MulticontrollerMode.MirrorAll; // Don't use CurrentMode setter to avoid extra events
                    }

                    ActiveChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        MulticontrollerMode _currentMode = MulticontrollerMode.Group;
        ToontownController _focusedController = null;

        internal MulticontrollerMode CurrentMode
        {
            get { return _currentMode; }
            set
            {
                if (_currentMode != value)
                {
                    _currentMode = value;
                    
                    // Clear focused controller when mode is explicitly changed (unless changing TO Focused mode)
                    if (_focusedController != null && value != MulticontrollerMode.Focused)
                    {
                        _focusedController = null;
                    }
                    
                    ModeChanged?.Invoke(this, EventArgs.Empty);
                    ActiveControllersChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }
        
        /// <summary>
        /// Set focus to a specific controller window (single window mode)
        /// </summary>
        internal void SetFocusedController(ToontownController controller)
        {
            if (_focusedController != controller)
            {
                _focusedController = controller;
                
                // Activate the multicontroller if not already active
                if (!IsActive)
                {
                    ShouldActivate?.Invoke(this, EventArgs.Empty);
                }
                
                // Set mode to Focused
                CurrentMode = MulticontrollerMode.Focused;
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
            var controllersWithWindows = AllControllersWithWindows.ToList();
            
            for (int i = 0; i < controllersWithWindows.Count; i++)
            {
                var controller = controllersWithWindows[i];
                var borderWnd = GetBorderWindow(controller);
                if (borderWnd != null)
                {
                    borderWnd.SwitchingMode = true;
                    borderWnd.SwitchingNumber = i + 1;
                    borderWnd.SwitchingSelected = (controller == _firstSelectedController || controller == _secondSelectedController || _switchedControllers.Contains(controller));
                }
            }
        }

        private void ExitSwitchingMode()
        {
            _switchingMode = false;
            _switchingModeTimer.Stop();
            _firstSelectedController = null;
            _secondSelectedController = null;

            // Reset all border windows, but keep SwitchingSelected true for switched controllers
            foreach (var controller in AllControllersWithWindows)
            {
                var borderWnd = GetBorderWindow(controller);
                if (borderWnd != null)
                {
                    borderWnd.SwitchingMode = false;
                    borderWnd.SwitchingNumber = 0;
                    // Keep SwitchingSelected true for controllers that were switched
                    if (!_switchedControllers.Contains(controller))
                    {
                        borderWnd.SwitchingSelected = false;
                    }
                }
            }
        }

        /// <summary>
        /// Clear the list of switched controllers and reset their yellow highlighting
        /// </summary>
        internal void ClearSwitchedControllers()
        {
            foreach (var controller in _switchedControllers)
            {
                var borderWnd = GetBorderWindow(controller);
                if (borderWnd != null)
                {
                    borderWnd.SwitchingSelected = false;
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
                    case MulticontrollerMode.MirrorGroup:
                        ControllerGroup group = ControllerGroups.First(g => g.AllControllers.Contains(controller));

                        CurrentGroupIndex = ControllerGroups.IndexOf(group);
                        break;
                    case MulticontrollerMode.Pair:
                        ControllerPair pair = AllControllerPairs.First(p => p.AllControllers.Contains(controller));

                        CurrentPairIndex = AllControllerPairsWithWindows.ToList().IndexOf(pair);
                        break;
                    case MulticontrollerMode.MirrorIndividual:
                        CurrentIndividualControllerIndex = AllControllersWithWindows.ToList().IndexOf(controller);
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
                    if (IsActive)
                    {
                        // If in focused mode, exit it and return to Mirror mode
                        if (CurrentMode == MulticontrollerMode.Focused)
                        {
                            _focusedController = null;
                            CurrentMode = MulticontrollerMode.MirrorAll;
                            return true;
                        }
                        
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

                        if (Properties.Settings.Default.mirrorGroupModeCycleWithModeHotkey)
                        {
                            availableModesToCycle.Add(MulticontrollerMode.MirrorGroup);
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
            else if (keysPressed == (Keys)Properties.Settings.Default.mirrorGroupModeKeyCode)
            {
                CurrentMode = MulticontrollerMode.MirrorGroup;
            }
            else if (keysPressed == (Keys)Properties.Settings.Default.pairModeKeyCode)
            {
                if (msg == Win32.WM.KEYDOWN && IsActive && AllControllerPairsWithWindows.Count() > 0)
                {
                    if (CurrentMode == MulticontrollerMode.Pair)
                    {
                        CurrentPairIndex = (CurrentPairIndex + 1) % AllControllerPairsWithWindows.Count();
                    }
                    else
                    {
                        CurrentMode = MulticontrollerMode.Pair;
                    }
                }
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
            else if (keysPressed == (Keys)Properties.Settings.Default.individualControlKeyCode)
            {
                if (msg == Win32.WM.KEYDOWN && IsActive && AllControllersWithWindows.Count() > 0)
                {
                    if (CurrentMode == MulticontrollerMode.MirrorIndividual)
                    {
                        CurrentIndividualControllerIndex = (CurrentIndividualControllerIndex + 1) % AllControllersWithWindows.Count();
                    }
                    else
                    {
                        CurrentMode = MulticontrollerMode.MirrorIndividual;
                    }
                }
            }
            else if ((CurrentMode == MulticontrollerMode.Group || CurrentMode == MulticontrollerMode.MirrorGroup)
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
            else if (keysPressed == Keys.Menu) // Alt key
            {
                // Handle Alt key for switching mode
                if (msg == Win32.WM.SYSKEYDOWN || msg == Win32.WM.KEYDOWN)
                {
                    if (!_switchingMode && IsActive && AllControllersWithWindows.Any())
                    {
                        // Enter switching mode
                        _switchingMode = true;
                        _firstSelectedController = null;
                        _secondSelectedController = null;
                        _switchingModeTimer.Start();
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
            else if (_switchingMode && keysPressed == Keys.X)
            {
                // Handle X key in switching mode (can be KEYDOWN or SYSKEYDOWN when Alt is held)
                if (msg == Win32.WM.KEYDOWN || msg == Win32.WM.SYSKEYDOWN)
                {
                    var controllerUnderCursor = GetControllerUnderCursor();
                    if (controllerUnderCursor != null)
                    {
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
                    }
                    return true;
                }
            }
            else if (keysPressed == (Keys)Properties.Settings.Default.zeroPowerThrowKeyCode 
                && Properties.Settings.Default.zeroPowerThrowKeyCode != 0)
            {
                // Handle Zero Power Throw Hotkey
                if (msg == Win32.WM.KEYDOWN || msg == Win32.WM.HOTKEY)
                {
                    // For HOTKEY messages, we can't rely on KEYUP, so just execute without repeat prevention
                    // For KEYDOWN messages (when multicontroller is active), use repeat prevention
                    if (msg == Win32.WM.KEYDOWN)
                    {
                        if (zeroPowerThrowKeyPressed)
                        {
                            return true; // Ignore repeated KEYDOWN
                        }
                        zeroPowerThrowKeyPressed = true;
                    }
                    
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
                            // Multicontroller is NOT active: Focus the foreground window and activate multicontroller
                            IntPtr foregroundWindow = Win32.GetForegroundWindow();
                            var foregroundController = AllControllersWithWindows.FirstOrDefault(c => c.WindowHandle == foregroundWindow);
                            
                            if (foregroundController != null)
                            {
                                // Set this window as focused - this will activate the multicontroller in focused mode
                                // In this mode: all inputs go to all windows EXCEPT movement keys which only go to focused window
                                SetFocusedController(foregroundController);
                                
                                // Now send the throw to ALL windows
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
                }
                else if (msg == Win32.WM.KEYUP)
                {
                    // Reset flag when key is released (only applies when multicontroller is active)
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
                
                // In Focused mode, check if this is a directional movement key
                if (CurrentMode == MulticontrollerMode.Focused && _focusedController != null)
                {
                    // Define ONLY directional movement key titles (NOT Jump)
                    var directionalMovementTitles = new[] { "Forward", "Backward", "Left", "Right" };
                    var movementBindings = Properties.SerializedSettings.Default.Bindings
                        .Where(b => directionalMovementTitles.Contains(b.Title));
                    
                    foreach (var binding in movementBindings)
                    {
                        if (keysPressed == binding.Key)
                        {
                            // In Focused mode, send the Toontown Key directly (not the mapped left/right keys)
                            _focusedController.PostMessage(msg, wParam, lParam);
                            return true;
                        }
                    }
                    
                    // If not a directional movement key (including Jump and all other keys), send to ALL windows
                    affectedControllers = AllControllersWithWindows;
                }

                if (CurrentMode == MulticontrollerMode.Group 
                    || CurrentMode == MulticontrollerMode.AllGroup 
                    || CurrentMode == MulticontrollerMode.Pair)
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
                
                if (CurrentMode == MulticontrollerMode.MirrorAll
                    || CurrentMode == MulticontrollerMode.MirrorGroup
                    || CurrentMode == MulticontrollerMode.MirrorIndividual
                    || CurrentMode == MulticontrollerMode.Focused)  // Focused mode acts like mirror for non-directional keys
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

            // First, find all empty controller slots (controllers without windows)
            var emptyControllers = AllControllers.Where(c => !c.HasWindow).ToList();

            // Assign new windows to empty slots first, then create new groups if needed
            int newWindowIndex = 0;
            
            // Fill empty slots first
            foreach (var emptyController in emptyControllers)
            {
                if (newWindowIndex >= newWindows.Count)
                    break;
                
                emptyController.WindowHandle = newWindows[newWindowIndex];
                newWindowIndex++;
            }

            // If there are still new windows to assign, create new groups and assign them
            while (newWindowIndex < newWindows.Count)
            {
                // Calculate which group this window belongs to (every 2 windows = 1 group)
                // Count all currently assigned windows to determine the next group
                int totalAssignedCount = AllControllersWithWindows.Count();
                int groupIndex = totalAssignedCount / 2;
                
                // Ensure we have enough groups
                while (groupIndex >= ControllerGroups.Count)
                {
                    AddControllerGroup();
                }

                var group = ControllerGroups[groupIndex];

                // Ensure we have at least one pair in this group
                if (group.ControllerPairs.Count == 0)
                {
                    group.AddPair();
                }

                // Determine if this is left (even index) or right (odd index)
                bool isLeft = (totalAssignedCount % 2) == 0;
                int pairIndex = 0; // Always use first pair in each group

                var pair = group.ControllerPairs[pairIndex];
                var controller = isLeft ? pair.LeftController : pair.RightController;

                // Assign the window
                controller.WindowHandle = newWindows[newWindowIndex];
                newWindowIndex++;
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

            // Automatically set to mirror mode and apply layout preset 1
            CurrentMode = MulticontrollerMode.MirrorAll;
            
            // Load and apply preset 1
            var preset1 = LayoutPreset.LoadFromSettings(1);
            if (preset1.Enabled)
            {
                ApplyLayoutPreset(preset1);
            }
        }

        /// <summary>
        /// Apply a layout preset to all windows with handles
        /// </summary>
        public void ApplyLayoutPreset(LayoutPreset preset)
        {
            if (preset == null || !preset.Enabled)
                return;

            var controllersWithWindows = AllControllersWithWindows.ToList();
            if (controllersWithWindows.Count == 0)
                return;

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
    }

    
}
