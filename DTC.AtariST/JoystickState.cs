// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

namespace DTC.AtariST;

/// <summary>
/// Represents Atari ST joystick state for one port.
/// </summary>
public readonly record struct JoystickState(
    bool IsUpPressed,
    bool IsDownPressed,
    bool IsLeftPressed,
    bool IsRightPressed,
    bool IsFirePressed)
{
    /// <summary>
    /// Gets a neutral joystick state with no directions or fire pressed.
    /// </summary>
    public static JoystickState Neutral => default;

    /// <summary>
    /// Gets whether any direction or fire input is currently active.
    /// </summary>
    public bool HasAnyInput =>
        IsUpPressed || IsDownPressed || IsLeftPressed || IsRightPressed || IsFirePressed;

    /// <summary>
    /// Clears opposing directions on each axis to match physical joystick constraints.
    /// </summary>
    public JoystickState NormalizeOpposingDirections()
    {
        var normalized = this;
        if (normalized.IsUpPressed && normalized.IsDownPressed)
            normalized = normalized with { IsUpPressed = false, IsDownPressed = false };
        if (normalized.IsLeftPressed && normalized.IsRightPressed)
            normalized = normalized with { IsLeftPressed = false, IsRightPressed = false };
        return normalized;
    }

    /// <summary>
    /// Returns a copy of this state with the fire button set to the requested value.
    /// </summary>
    public JoystickState WithFire(bool isPressed) =>
        this with { IsFirePressed = isPressed };
}
