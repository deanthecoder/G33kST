// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using Avalonia;
using G33kST.Views;
using JetBrains.Annotations;

namespace G33kST;

/// <summary>
/// Entrypoint for the desktop emulator shell.
/// </summary>
internal static class Program
{
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    [UsedImplicitly]
    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont();
#if DEBUG
        builder = builder.LogToTrace();
#endif
        return builder;
    }
}
