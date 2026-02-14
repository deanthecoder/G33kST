// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System.Reflection;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using DTC.Core.UI;

namespace G33kST.ViewModels;

/// <summary>
/// Provides static metadata used by About dialogs and app menu integration.
/// </summary>
internal static class AboutInfoProvider
{
    public static AboutInfo Info => new()
    {
        Title = "G33kST",
        Version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "Unknown",
        Copyright = "Copyright © 2026 Dean Edis (DeanTheCoder).",
        WebsiteUrl = "https://github.com/deanthecoder/G33kST",
        Icon = LoadIcon()
    };

    private static Bitmap LoadIcon()
    {
        using var stream = AssetLoader.Open(new Uri("avares://G33kST/Assets/app.png"));
        return new Bitmap(stream);
    }
}
