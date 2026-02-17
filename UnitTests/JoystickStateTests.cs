// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using DTC.AtariST;

namespace UnitTests;

[TestFixture]
public sealed class JoystickStateTests
{
    [Test]
    public void NormalizeOpposingDirectionsShouldClearBothAxesWhenBothDirectionsPressed()
    {
        var state = new JoystickState(
            IsUpPressed: true,
            IsDownPressed: true,
            IsLeftPressed: true,
            IsRightPressed: true,
            IsFirePressed: true);

        var normalized = state.NormalizeOpposingDirections();

        Assert.Multiple(() =>
        {
            Assert.That(normalized.IsUpPressed, Is.False);
            Assert.That(normalized.IsDownPressed, Is.False);
            Assert.That(normalized.IsLeftPressed, Is.False);
            Assert.That(normalized.IsRightPressed, Is.False);
            Assert.That(normalized.IsFirePressed, Is.True);
        });
    }

    [Test]
    public void HasAnyInputShouldReflectDirectionalOrFireState()
    {
        var neutral = JoystickState.Neutral;
        var fireOnly = neutral.WithFire(isPressed: true);
        var directionOnly = new JoystickState(
            IsUpPressed: false,
            IsDownPressed: false,
            IsLeftPressed: true,
            IsRightPressed: false,
            IsFirePressed: false);

        Assert.Multiple(() =>
        {
            Assert.That(neutral.HasAnyInput, Is.False);
            Assert.That(fireOnly.HasAnyInput, Is.True);
            Assert.That(directionOnly.HasAnyInput, Is.True);
        });
    }
}
