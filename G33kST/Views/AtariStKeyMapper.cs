// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using Avalonia.Input;

namespace G33kST.Views;

/// <summary>
/// Converts Avalonia key values to Atari ST IKBD scan codes.
/// </summary>
internal static class AtariStKeyMapper
{
    private static readonly Dictionary<string, byte> KeyByName = new(StringComparer.Ordinal)
    {
        // Top row.
        ["Escape"] = 0x01,
        ["D1"] = 0x02,
        ["D2"] = 0x03,
        ["D3"] = 0x04,
        ["D4"] = 0x05,
        ["D5"] = 0x06,
        ["D6"] = 0x07,
        ["D7"] = 0x08,
        ["D8"] = 0x09,
        ["D9"] = 0x0A,
        ["D0"] = 0x0B,
        ["OemMinus"] = 0x0C,
        ["OemPlus"] = 0x0D,
        ["Back"] = 0x0E,

        // Q row.
        ["Tab"] = 0x0F,
        ["Q"] = 0x10,
        ["W"] = 0x11,
        ["E"] = 0x12,
        ["R"] = 0x13,
        ["T"] = 0x14,
        ["Y"] = 0x15,
        ["U"] = 0x16,
        ["I"] = 0x17,
        ["O"] = 0x18,
        ["P"] = 0x19,
        ["OemOpenBrackets"] = 0x1A,
        ["OemCloseBrackets"] = 0x1B,
        ["Enter"] = 0x1C,

        // A row.
        ["LeftCtrl"] = 0x1D,
        ["RightCtrl"] = 0x1D,
        ["A"] = 0x1E,
        ["S"] = 0x1F,
        ["D"] = 0x20,
        ["F"] = 0x21,
        ["G"] = 0x22,
        ["H"] = 0x23,
        ["J"] = 0x24,
        ["K"] = 0x25,
        ["L"] = 0x26,
        ["OemSemicolon"] = 0x27,
        ["OemQuotes"] = 0x28,
        ["OemTilde"] = 0x29,

        // Z row.
        ["LeftShift"] = 0x2A,
        ["OemBackslash"] = 0x2B,
        ["Z"] = 0x2C,
        ["X"] = 0x2D,
        ["C"] = 0x2E,
        ["V"] = 0x2F,
        ["B"] = 0x30,
        ["N"] = 0x31,
        ["M"] = 0x32,
        ["OemComma"] = 0x33,
        ["OemPeriod"] = 0x34,
        ["OemQuestion"] = 0x35,
        ["RightShift"] = 0x36,

        // Modifiers and function keys.
        ["LeftAlt"] = 0x38,
        ["RightAlt"] = 0x38,
        ["Space"] = 0x39,
        ["CapsLock"] = 0x3A,
        ["F1"] = 0x3B,
        ["F2"] = 0x3C,
        ["F3"] = 0x3D,
        ["F4"] = 0x3E,
        ["F5"] = 0x3F,
        ["F6"] = 0x40,
        ["F7"] = 0x41,
        ["F8"] = 0x42,
        ["F9"] = 0x43,
        ["F10"] = 0x44,

        // Cursor/navigation.
        ["Home"] = 0x47,
        ["Up"] = 0x48,
        ["Left"] = 0x4B,
        ["Right"] = 0x4D,
        ["Down"] = 0x50,
        ["Insert"] = 0x52,
        ["Delete"] = 0x53,

        // Keypad digits.
        ["NumPad0"] = 0x70,
        ["NumPad1"] = 0x6D,
        ["NumPad2"] = 0x6E,
        ["NumPad3"] = 0x6F,
        ["NumPad4"] = 0x6A,
        ["NumPad5"] = 0x6B,
        ["NumPad6"] = 0x6C,
        ["NumPad7"] = 0x67,
        ["NumPad8"] = 0x68,
        ["NumPad9"] = 0x69,
        ["Decimal"] = 0x71,
        ["Add"] = 0x4E,
        ["Subtract"] = 0x4A,
        ["Multiply"] = 0x66,
        ["Divide"] = 0x65
    };

    public static bool TryGetScanCode(Key key, out byte scanCode) =>
        KeyByName.TryGetValue(key.ToString(), out scanCode);
}
