// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using DTC.Core.Settings;

namespace G33kST.ViewModels;

/// <summary>
/// Persistent UI settings for the G33kST desktop shell.
/// </summary>
public sealed class Settings : UserSettingsBase
{
    public static Settings Instance { get; } = new();

    protected override void ApplyDefaults()
    {
        IsSoundEnabled = true;
        IsAmbientBlurred = true;
        IsCrtEmulationEnabled = true;
        IsCpuHistoryTracked = false;
    }

    public bool IsSoundEnabled
    {
        get => Get<bool>();
        set => Set(value);
    }

    public bool IsAmbientBlurred
    {
        get => Get<bool>();
        set => Set(value);
    }

    public bool IsCrtEmulationEnabled
    {
        get => Get<bool>();
        set => Set(value);
    }

    public bool IsCpuHistoryTracked
    {
        get => Get<bool>();
        set => Set(value);
    }

    public string FloppyImageMru
    {
        get => Get<string>();
        set => Set(value);
    }

    public string LastFloppyImagePath
    {
        get => Get<string>();
        set => Set(value);
    }

    public string SelectedRomPath
    {
        get => Get<string>();
        set => Set(value);
    }
}
