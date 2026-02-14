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
using G33kST.ViewModels;

namespace G33kST.Views;

/// <summary>
/// Main desktop shell for the Atari ST emulator UI.
/// </summary>
public partial class MainWindow : Window
{
    private bool m_isLoaded;
    private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext;

    public MainWindow() =>
        InitializeComponent();

    private void OnAboutDialogClicked(object sender, PointerPressedEventArgs e) =>
        Host.CloseDialogCommand.Execute(sender);

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
}
