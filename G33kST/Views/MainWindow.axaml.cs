// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia;
using G33kST.ViewModels;

namespace G33kST.Views;

/// <summary>
/// Main desktop shell for the Atari ST emulator UI.
/// </summary>
public partial class MainWindow : Window
{
    private bool m_isLoaded;
    private static readonly Cursor HiddenCursor = new(StandardCursorType.None);
    private static readonly Cursor ArrowCursor = new(StandardCursorType.Arrow);
    private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext;

    public MainWindow() =>
        InitializeComponent();

    private void OnAboutDialogClicked(object sender, PointerPressedEventArgs e) =>
        Host.CloseDialogCommand.Execute(sender);

    private void OnDisplayPointerMoved(object sender, PointerEventArgs e) =>
        ForwardPointerStateToMachine(e);

    private void OnDisplayPointerEntered(object sender, PointerEventArgs e)
    {
        MainDisplay.Cursor = HiddenCursor;
        ForwardPointerStateToMachine(e);
    }

    private void OnDisplayPointerPressed(object sender, PointerPressedEventArgs e)
    {
        if (sender is InputElement element)
            e.Pointer.Capture(element);
        ForwardPointerStateToMachine(e);
    }

    private void OnDisplayPointerReleased(object sender, PointerReleasedEventArgs e)
    {
        ForwardPointerStateToMachine(e);

        var properties = e.GetCurrentPoint(MainDisplay).Properties;
        if (!properties.IsLeftButtonPressed && !properties.IsRightButtonPressed && sender is InputElement)
            e.Pointer.Capture(null);
    }

    private void OnDisplayPointerExited(object sender, PointerEventArgs e) =>
        HandleDisplayPointerExit(e);

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        Activated += (_, _) => ViewModel?.SetInputActive(true);
        Deactivated += (_, _) => ViewModel?.SetInputActive(false);
        ViewModel?.SetInputActive(IsActive);
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
                AmbientDisplay.InvalidateVisual();
                MainDisplay.InvalidateVisual();
            }, DispatcherPriority.Render);
        };
    }

    private void ForwardPointerStateToMachine(PointerEventArgs e, bool? isPointerWithinDisplay = null)
    {
        var pointerPoint = e.GetCurrentPoint(MainDisplay);
        var isLeftButtonPressed = pointerPoint.Properties.IsLeftButtonPressed;
        var isRightButtonPressed = pointerPoint.Properties.IsRightButtonPressed;

        if (isPointerWithinDisplay.HasValue)
        {
            ViewModel.UpdateMouseState(0, 0, isLeftButtonPressed, isRightButtonPressed, isPointerWithinDisplay.Value);
            return;
        }

        var pointerPosition = e.GetPosition(MainDisplay);
        if (!TryMapPointerToNormalized(pointerPosition, out var normalizedX, out var normalizedY))
        {
            ViewModel.UpdateMouseState(0, 0, isLeftButtonPressed, isRightButtonPressed, false);
            return;
        }

        ViewModel.UpdateMouseState(normalizedX, normalizedY, isLeftButtonPressed, isRightButtonPressed, true);
    }

    private void HandleDisplayPointerExit(PointerEventArgs e)
    {
        MainDisplay.Cursor = ArrowCursor;
        ForwardPointerStateToMachine(e, isPointerWithinDisplay: false);
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
}
