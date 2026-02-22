// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System.Diagnostics;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using DTC.AtariST;
using G33kST.ViewModels;

namespace G33kST.Views;

/// <summary>
/// Main desktop shell for the Atari ST emulator UI.
/// </summary>
public partial class MainWindow : Window
{
    private const int KeyHoldDelayMs = 300;
    private const int AutoFireIntervalMs = 60; // Match MasterG33k cadence.
    private static readonly string[] SupportedDroppedFileExtensions = [".st", ".zip"];
    private bool m_isLoaded;
    private bool m_isPointerInsideDisplay;
    private bool m_isLeftMouseButtonPressed;
    private readonly Dictionary<Key, HeldKeyState> m_pressedMachineKeys = [];
    private readonly DispatcherTimer m_keyHoldTimer;
    private readonly DispatcherTimer m_joystickAutoFireTimer;
    private JoystickState m_joystickState;
    private bool m_isJoystickAutoFireHeld;
    private bool m_isJoystickAutoFirePulseOn;
    private static readonly Cursor HiddenCursor = new(StandardCursorType.None);
    private static readonly Cursor ArrowCursor = new(StandardCursorType.Arrow);
    private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext;

    public MainWindow()
    {
        InitializeComponent();
        m_keyHoldTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        m_keyHoldTimer.Tick += OnKeyHoldTick;
        m_joystickAutoFireTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(AutoFireIntervalMs) };
        m_joystickAutoFireTimer.Tick += OnJoystickAutoFireTick;
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnPreviewKeyUp, RoutingStrategies.Tunnel);
        AddHandler(DragDrop.DragOverEvent, OnWindowDragOver);
        AddHandler(DragDrop.DropEvent, OnWindowDrop);
    }

    private void OnAboutDialogClicked(object sender, PointerPressedEventArgs e) =>
        Host.CloseDialogCommand.Execute(sender);

    private void OnDisplayPointerMoved(object sender, PointerEventArgs e) =>
        ForwardPointerStateToMachine(e);

    private void OnDisplayPointerEntered(object sender, PointerEventArgs e)
    {
        m_isPointerInsideDisplay = true;
        var point = e.GetCurrentPoint(MainDisplay);
        m_isLeftMouseButtonPressed = point.Properties.IsLeftButtonPressed;
        MainDisplay.Cursor = HiddenCursor;
        FocusDisplaySurface();
        ForwardPointerStateToMachine(e);
    }

    private void OnDisplayPointerPressed(object sender, PointerPressedEventArgs e)
    {
        if (sender is InputElement element)
            e.Pointer.Capture(element);

        FocusDisplaySurface();

        var point = e.GetCurrentPoint(MainDisplay);
        var isLeftButtonPressed = point.Properties.IsLeftButtonPressed;
        var isRightButtonPressed = point.Properties.IsRightButtonPressed;

        // If we missed a release edge, force one so the emulated side still sees a fresh click.
        if (isLeftButtonPressed && m_isLeftMouseButtonPressed)
            ForwardPointerStateToMachine(e, isLeftButtonPressed: false, isRightButtonPressed: isRightButtonPressed);

        m_isLeftMouseButtonPressed = isLeftButtonPressed;
        ForwardPointerStateToMachine(e, isLeftButtonPressed: isLeftButtonPressed, isRightButtonPressed: isRightButtonPressed);
    }

    private void OnDisplayPointerReleased(object sender, PointerReleasedEventArgs e)
    {
        var point = e.GetCurrentPoint(MainDisplay);
        var isLeftButtonPressed = point.Properties.IsLeftButtonPressed;
        var isRightButtonPressed = point.Properties.IsRightButtonPressed;

        m_isLeftMouseButtonPressed = isLeftButtonPressed;
        ForwardPointerStateToMachine(e, isLeftButtonPressed: isLeftButtonPressed, isRightButtonPressed: isRightButtonPressed);

        var properties = e.GetCurrentPoint(MainDisplay).Properties;
        if (!properties.IsLeftButtonPressed && !properties.IsRightButtonPressed && sender is InputElement)
            e.Pointer.Capture(null);
    }

    private void OnDisplayPointerExited(object sender, PointerEventArgs e) =>
        HandleDisplayPointerExit(e);

    private void OnWindowDragOver(object sender, DragEventArgs e)
    {
        e.DragEffects = TryGetFirstSupportedDroppedFile(e, out _)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnWindowDrop(object sender, DragEventArgs e)
    {
        if (!TryGetFirstSupportedDroppedFile(e, out var file))
        {
            e.Handled = true;
            return;
        }

        _ = ViewModel.MountFloppyImageFromFile(file, addToMru: true);
        e.Handled = true;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        Activated += (_, _) => ViewModel?.SetInputActive(true);
        Deactivated += (_, _) =>
        {
            ViewModel?.SetInputActive(false);
            ReleaseAllPressedMachineKeys();
            ReleaseAllPressedJoystickKeys();
        };
        ViewModel?.SetInputActive(IsActive);
        if (ViewModel != null)
            ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        if (m_isLoaded)
            return;
        m_isLoaded = true;

        ViewModel.DisplayUpdated += (_, _) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (AmbientDisplay.IsVisible)
                    AmbientDisplay.InvalidateVisual();
                MainDisplay.InvalidateVisual();
            }, DispatcherPriority.Render);
        };
    }

    protected override void OnClosed(EventArgs e)
    {
        if (ViewModel != null)
            ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        m_joystickAutoFireTimer.Stop();
        m_joystickAutoFireTimer.Tick -= OnJoystickAutoFireTick;
        m_keyHoldTimer.Stop();
        m_keyHoldTimer.Tick -= OnKeyHoldTick;
        base.OnClosed(e);
    }

    private void ForwardPointerStateToMachine(PointerEventArgs e, bool? isPointerWithinDisplay = null, bool? isLeftButtonPressed = null, bool? isRightButtonPressed = null)
    {
        var pointerPoint = e.GetCurrentPoint(MainDisplay);
        var leftButtonPressed = isLeftButtonPressed ?? pointerPoint.Properties.IsLeftButtonPressed;
        var rightButtonPressed = isRightButtonPressed ?? pointerPoint.Properties.IsRightButtonPressed;

        if (isPointerWithinDisplay.HasValue)
        {
            ViewModel.UpdateMouseState(0, 0, leftButtonPressed, rightButtonPressed, isPointerWithinDisplay.Value);
            return;
        }

        var pointerPosition = e.GetPosition(MainDisplay);
        if (!TryMapPointerToNormalized(pointerPosition, out var normalizedX, out var normalizedY))
        {
            ViewModel.UpdateMouseState(0, 0, leftButtonPressed, rightButtonPressed, false);
            return;
        }

        ViewModel.UpdateMouseState(normalizedX, normalizedY, leftButtonPressed, rightButtonPressed, true);
    }

    private void HandleDisplayPointerExit(PointerEventArgs e)
    {
        m_isPointerInsideDisplay = false;
        m_isLeftMouseButtonPressed = false;
        MainDisplay.Cursor = ArrowCursor;
        ForwardPointerStateToMachine(e, isPointerWithinDisplay: false);
        ReleaseAllPressedMachineKeys();
    }

    private void FocusDisplaySurface()
    {
        if (!IsActive)
            return;
        MainDisplay.Focus(NavigationMethod.Pointer);
    }

    private bool TryMapPointerToNormalized(Point pointerPosition, out double normalizedX, out double normalizedY)
    {
        normalizedX = 0;
        normalizedY = 0;

        var displayWidth = MainDisplay.Bounds.Width;
        var displayHeight = MainDisplay.Bounds.Height;
        if (displayWidth <= 0 || displayHeight <= 0)
            return false;

        var sourceWidth = ViewModel.Display?.Size.Width ?? 0;
        var sourceHeight = ViewModel.Display?.Size.Height ?? 0;
        if (sourceWidth <= 0 || sourceHeight <= 0)
            return false;

        var sourceAspect = sourceWidth / sourceHeight;
        var displayAspect = displayWidth / displayHeight;
        var contentWidth = displayWidth;
        var contentHeight = displayHeight;
        var contentOffsetX = 0.0;
        var contentOffsetY = 0.0;

        if (displayAspect > sourceAspect)
        {
            contentWidth = displayHeight * sourceAspect;
            contentOffsetX = (displayWidth - contentWidth) * 0.5;
        }
        else if (displayAspect < sourceAspect)
        {
            contentHeight = displayWidth / sourceAspect;
            contentOffsetY = (displayHeight - contentHeight) * 0.5;
        }

        var contentBounds = new Rect(contentOffsetX, contentOffsetY, contentWidth, contentHeight);
        if (!contentBounds.Contains(pointerPosition))
            return false;

        normalizedX = (pointerPosition.X - contentBounds.X) / contentBounds.Width;
        normalizedY = (pointerPosition.Y - contentBounds.Y) / contentBounds.Height;
        normalizedX = Math.Clamp(normalizedX, 0, 1);
        normalizedY = Math.Clamp(normalizedY, 0, 1);
        return true;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (ViewModel.IsJoystickInputEnabled && IsJoystickControlKey(e.Key))
        {
            TryHandleJoystickKeyDown(e.Key);
            e.Handled = true;
            return;
        }

        if (ShouldTreatAsHostShortcut(e))
            return;
        if (TryHandleJoystickKeyDown(e.Key))
        {
            e.Handled = true;
            return;
        }
        if (!m_isPointerInsideDisplay)
            return;
        if (!AtariStKeyMapper.TryGetScanCode(e.Key, out var scanCode))
            return;
        if (m_pressedMachineKeys.ContainsKey(e.Key))
            return;

        if (IsImmediateHoldModifierKey(e.Key))
        {
            ViewModel.UpdateKeyboardState(scanCode, isPressed: true);
            m_pressedMachineKeys[e.Key] = new HeldKeyState(scanCode, holdActivationTick: 0)
            {
                IsHeld = true
            };
            e.Handled = true;
            return;
        }

        // Immediate press/release gives one clean character.
        ViewModel.UpdateKeyboardState(scanCode, isPressed: true);
        ViewModel.UpdateKeyboardState(scanCode, isPressed: false);

        // If still held after a short delay, treat it as a held key.
        var now = Stopwatch.GetTimestamp();
        m_pressedMachineKeys[e.Key] = new HeldKeyState(scanCode, now + MsToTicks(KeyHoldDelayMs));
        if (!m_keyHoldTimer.IsEnabled)
            m_keyHoldTimer.Start();
        e.Handled = true;
    }

    private void OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (ViewModel.IsJoystickInputEnabled && IsJoystickControlKey(e.Key))
        {
            TryHandleJoystickKeyUp(e.Key);
            e.Handled = true;
            return;
        }

        if (TryHandleJoystickKeyUp(e.Key))
        {
            e.Handled = true;
            return;
        }

        if (!m_pressedMachineKeys.Remove(e.Key, out var state))
            return;

        if (state.IsHeld)
            ViewModel.UpdateKeyboardState(state.ScanCode, isPressed: false);
        if (m_pressedMachineKeys.Count == 0)
            m_keyHoldTimer.Stop();
        e.Handled = true;
    }

    private void OnKeyHoldTick(object sender, EventArgs e)
    {
        if (!m_isPointerInsideDisplay || m_pressedMachineKeys.Count == 0)
            return;

        var now = Stopwatch.GetTimestamp();
        foreach (var state in m_pressedMachineKeys.Values)
        {
            if (state.IsHeld || now < state.HoldActivationTick)
                continue;

            ViewModel.UpdateKeyboardState(state.ScanCode, isPressed: true);
            state.IsHeld = true;
        }
    }

    private void ReleaseAllPressedMachineKeys()
    {
        if (m_pressedMachineKeys.Count == 0)
            return;

        foreach (var state in m_pressedMachineKeys.Values)
        {
            if (state.IsHeld)
                ViewModel.UpdateKeyboardState(state.ScanCode, isPressed: false);
        }

        m_pressedMachineKeys.Clear();
        m_keyHoldTimer.Stop();
    }

    private void ReleaseAllPressedJoystickKeys()
    {
        if (!HasAnyJoystickState())
            return;

        m_joystickState = JoystickState.Neutral;
        m_isJoystickAutoFireHeld = false;
        m_isJoystickAutoFirePulseOn = false;
        m_joystickAutoFireTimer.Stop();
        ViewModel.UpdateJoystickState(m_joystickState);
    }

    private void OnJoystickAutoFireTick(object sender, EventArgs e)
    {
        if (!ViewModel.IsJoystickInputEnabled || !m_isJoystickAutoFireHeld)
        {
            m_isJoystickAutoFirePulseOn = false;
            m_joystickAutoFireTimer.Stop();
            PushJoystickStateToMachine();
            return;
        }

        m_isJoystickAutoFirePulseOn = !m_isJoystickAutoFirePulseOn;
        PushJoystickStateToMachine();
    }

    private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainWindowViewModel.IsJoystickInputEnabled))
            return;
        if (ViewModel.IsJoystickInputEnabled)
        {
            ReleaseAllPressedMachineKeys();
            ViewModel.ClearKeyboardInputQueue();
            return;
        }
        ReleaseAllPressedJoystickKeys();
    }

    private bool TryHandleJoystickKeyDown(Key key)
    {
        if (!ViewModel.IsJoystickInputEnabled)
            return false;
        if (!IsActive)
            return false;

        switch (key)
        {
            case Key.Up:
                if (m_joystickState.IsUpPressed)
                    return true;
                m_joystickState = m_joystickState with { IsUpPressed = true };
                break;
            case Key.Down:
                if (m_joystickState.IsDownPressed)
                    return true;
                m_joystickState = m_joystickState with { IsDownPressed = true };
                break;
            case Key.Left:
                if (m_joystickState.IsLeftPressed)
                    return true;
                m_joystickState = m_joystickState with { IsLeftPressed = true };
                break;
            case Key.Right:
                if (m_joystickState.IsRightPressed)
                    return true;
                m_joystickState = m_joystickState with { IsRightPressed = true };
                break;
            case Key.Z:
                if (m_joystickState.IsFirePressed)
                    return true;
                m_joystickState = m_joystickState with { IsFirePressed = true };
                break;
            case Key.A:
                if (m_isJoystickAutoFireHeld)
                    return true;
                m_isJoystickAutoFireHeld = true;
                m_isJoystickAutoFirePulseOn = true; // Engage fire immediately.
                m_joystickAutoFireTimer.Start();
                break;
            default:
                return false;
        }

        PushJoystickStateToMachine();
        return true;
    }

    private bool TryHandleJoystickKeyUp(Key key)
    {
        if (!IsJoystickControlKey(key))
            return false;
        if (!ViewModel.IsJoystickInputEnabled && !HasAnyJoystickState())
            return false;

        var changed = false;
        switch (key)
        {
            case Key.Up:
                changed = m_joystickState.IsUpPressed;
                m_joystickState = m_joystickState with { IsUpPressed = false };
                break;
            case Key.Down:
                changed = m_joystickState.IsDownPressed;
                m_joystickState = m_joystickState with { IsDownPressed = false };
                break;
            case Key.Left:
                changed = m_joystickState.IsLeftPressed;
                m_joystickState = m_joystickState with { IsLeftPressed = false };
                break;
            case Key.Right:
                changed = m_joystickState.IsRightPressed;
                m_joystickState = m_joystickState with { IsRightPressed = false };
                break;
            case Key.Z:
                changed = m_joystickState.IsFirePressed;
                m_joystickState = m_joystickState with { IsFirePressed = false };
                break;
            case Key.A:
                changed = m_isJoystickAutoFireHeld || m_isJoystickAutoFirePulseOn;
                m_isJoystickAutoFireHeld = false;
                m_isJoystickAutoFirePulseOn = false;
                m_joystickAutoFireTimer.Stop();
                break;
        }

        if (!changed)
            return false;

        PushJoystickStateToMachine();
        return true;
    }

    private void PushJoystickStateToMachine()
    {
        var isFirePressed = m_joystickState.IsFirePressed || (m_isJoystickAutoFireHeld && m_isJoystickAutoFirePulseOn);
        ViewModel.UpdateJoystickState(m_joystickState.WithFire(isFirePressed));
    }

    private bool HasAnyJoystickState() =>
        m_joystickState.HasAnyInput ||
        m_isJoystickAutoFireHeld ||
        m_isJoystickAutoFirePulseOn;

    private static bool IsJoystickControlKey(Key key) =>
        key is Key.Up or Key.Down or Key.Left or Key.Right or Key.Z or Key.A;

    private static bool IsImmediateHoldModifierKey(Key key) =>
        key is Key.LeftShift or Key.RightShift or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt;

    private static long MsToTicks(int milliseconds) =>
        milliseconds * Stopwatch.Frequency / 1000;

    private static bool ShouldTreatAsHostShortcut(KeyEventArgs e)
    {
        var modifiers = e.KeyModifiers;
        var hostShortcutModifier = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        var usesHostShortcutModifier = (modifiers & hostShortcutModifier) != 0;
        if (!usesHostShortcutModifier)
            return false;

        // Allow raw modifier keys to pass through to the emulated machine.
        return e.Key is not Key.LeftCtrl and not Key.RightCtrl and not Key.LeftAlt and not Key.RightAlt and not Key.LWin and not Key.RWin;
    }

    private sealed class HeldKeyState
    {
        public HeldKeyState(byte scanCode, long holdActivationTick)
        {
            ScanCode = scanCode;
            HoldActivationTick = holdActivationTick;
        }

        public byte ScanCode { get; }

        public long HoldActivationTick { get; }

        public bool IsHeld { get; set; }
    }

    private static bool TryGetFirstSupportedDroppedFile(DragEventArgs e, out FileInfo file)
    {
        file = null;

        var storageItems = e?.Data.GetFiles();
        if (storageItems == null)
            return false;
        
        foreach (var storageItem in storageItems)
        {
            var localPath = storageItem.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(localPath))
                continue;
            var droppedFile = new FileInfo(localPath);
            if (!IsSupportedDroppedFile(droppedFile))
                continue;
            file = droppedFile;
            return true;
        }

        return false;
    }

    private static bool IsSupportedDroppedFile(FileInfo file) =>
        file != null &&
        file.Exists &&
        SupportedDroppedFileExtensions.Contains(file.Extension, StringComparer.OrdinalIgnoreCase);
}
