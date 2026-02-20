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
public sealed class AciaIkbdDeviceTests
{
    private const uint KeyboardStatusAddress = 0x00FFFC00;
    private const uint KeyboardDataAddress = 0x00FFFC02;
    private const uint MidiStatusAddress = 0x00FFFC04;
    private const uint MidiDataAddress = 0x00FFFC06;

    [Test]
    public void KeyboardStatusShouldReportTransmitReadyWhenQueueIsEmpty()
    {
        var device = new AciaIkbdDevice();

        var status = device.Read8(KeyboardStatusAddress);

        Assert.That(status, Is.EqualTo(0x02));
    }

    [Test]
    public void KeyboardResetCommandShouldQueueResetAcknowledge()
    {
        var device = new AciaIkbdDevice();

        device.Write8(KeyboardDataAddress, 0x80);
        device.Write8(KeyboardDataAddress, 0x01);
        var status = device.Read8(KeyboardStatusAddress);
        var acknowledge = device.Read8(KeyboardDataAddress);
        var statusAfterRead = device.Read8(KeyboardStatusAddress);

        Assert.Multiple(() =>
        {
            Assert.That(status & 0x01, Is.Not.Zero);
            Assert.That(acknowledge, Is.EqualTo(0xF1));
            Assert.That(statusAfterRead & 0x01, Is.Zero);
        });
    }

    [Test]
    public void UnknownKeyboardCommandShouldNotQueueResponse()
    {
        var device = new AciaIkbdDevice();

        device.Write8(KeyboardDataAddress, 0x01);
        var status = device.Read8(KeyboardStatusAddress);

        Assert.That(status & 0x01, Is.Zero);
    }

    [Test]
    public void KeyboardControlMasterResetShouldClearReceiveQueue()
    {
        var device = new AciaIkbdDevice();
        device.Write8(KeyboardDataAddress, 0x80);
        device.Write8(KeyboardDataAddress, 0x01);
        Assert.That(device.Read8(KeyboardStatusAddress) & 0x01, Is.Not.Zero);

        device.Write8(KeyboardStatusAddress, 0x03);
        var statusAfterReset = device.Read8(KeyboardStatusAddress);

        Assert.That(statusAfterReset & 0x01, Is.Zero);
    }

    [Test]
    public void MidiReadsShouldReturnDefaultValues()
    {
        var device = new AciaIkbdDevice();

        var midiStatus = device.Read8(MidiStatusAddress);
        var midiData = device.Read8(MidiDataAddress);

        Assert.Multiple(() =>
        {
            Assert.That(midiStatus, Is.EqualTo(0x02));
            Assert.That(midiData, Is.EqualTo(0x00));
        });
    }

    [Test]
    public void UnsupportedAddressShouldReturnOpenBusValue()
    {
        var device = new AciaIkbdDevice();

        var value = device.Read8(0x00FFFC40);

        Assert.That(value, Is.EqualTo(0xFF));
    }

    [Test]
    public void QueueKeyboardByteShouldRaiseDataReadyEventForEachQueuedByte()
    {
        var device = new AciaIkbdDevice();
        var eventCount = 0;
        device.KeyboardDataReady += (_, _) => eventCount++;

        device.QueueKeyboardByte(0x1C);
        device.QueueKeyboardByte(0x9C);

        Assert.That(eventCount, Is.EqualTo(2));
    }

    [Test]
    public void QueueKeyboardByteShouldExposeQueuedValueThroughKeyboardDataRegister()
    {
        var device = new AciaIkbdDevice();
        device.QueueKeyboardByte(0x1C);

        var status = device.Read8(KeyboardStatusAddress);
        var value = device.Read8(KeyboardDataAddress);

        Assert.Multiple(() =>
        {
            Assert.That(status & 0x01, Is.Not.Zero);
            Assert.That(value, Is.EqualTo(0x1C));
        });
    }

    [Test]
    public void QueueRelativeMousePacketShouldExposeHeaderAndSignedDeltas()
    {
        var device = new AciaIkbdDevice();

        device.QueueRelativeMousePacket(5, -2, isLeftButtonPressed: true, isRightButtonPressed: false);

        var header = device.Read8(KeyboardDataAddress);
        var deltaX = device.Read8(KeyboardDataAddress);
        var deltaY = device.Read8(KeyboardDataAddress);

        Assert.Multiple(() =>
        {
            Assert.That(header, Is.EqualTo(0xFA));
            Assert.That(deltaX, Is.EqualTo(0x05));
            Assert.That(deltaY, Is.EqualTo(0xFE));
        });
    }

    [Test]
    public void QueueRelativeMousePacketShouldSetRightButtonBit()
    {
        var device = new AciaIkbdDevice();

        device.QueueRelativeMousePacket(0, 0, isLeftButtonPressed: false, isRightButtonPressed: true);

        var header = device.Read8(KeyboardDataAddress);
        _ = device.Read8(KeyboardDataAddress);
        _ = device.Read8(KeyboardDataAddress);

        Assert.That(header, Is.EqualTo(0xF9));
    }

    [Test]
    public void QueueJoystickStateShouldExposePort0HeaderAndStateBits()
    {
        var device = new AciaIkbdDevice();

        device.QueueJoystickState(0, new JoystickState(
            IsUpPressed: true,
            IsDownPressed: false,
            IsLeftPressed: true,
            IsRightPressed: false,
            IsFirePressed: true));

        var header = device.Read8(KeyboardDataAddress);
        var state = device.Read8(KeyboardDataAddress);

        Assert.Multiple(() =>
        {
            Assert.That(header, Is.EqualTo(0xFE));
            Assert.That(state, Is.EqualTo(0x85));
        });
    }

    [Test]
    public void QueueJoystickStateShouldNeutralizeOppositeDirectionsOnEachAxis()
    {
        var device = new AciaIkbdDevice();

        device.QueueJoystickState(0, new JoystickState(
            IsUpPressed: true,
            IsDownPressed: true,
            IsLeftPressed: true,
            IsRightPressed: true,
            IsFirePressed: true));

        var header = device.Read8(KeyboardDataAddress);
        var state = device.Read8(KeyboardDataAddress);

        Assert.Multiple(() =>
        {
            Assert.That(header, Is.EqualTo(0xFE));
            Assert.That(state, Is.EqualTo(0x80), "Opposite directions should cancel out, leaving only fire.");
        });
    }

    [Test]
    public void ReadingOneByteOfJoystickPacketShouldRearmKeyboardInterruptForRemainingByteAfterAdvance()
    {
        var device = new AciaIkbdDevice();
        var assertedCount = 0;
        device.KeyboardInterruptLineChanged += isActiveLow =>
        {
            if (isActiveLow)
                assertedCount++;
        };
        device.Write8(KeyboardStatusAddress, 0x80); // Enable receive IRQ signaling.

        device.QueueJoystickState(0, new JoystickState(
            IsUpPressed: false,
            IsDownPressed: false,
            IsLeftPressed: false,
            IsRightPressed: true,
            IsFirePressed: false));

        _ = device.Read8(KeyboardDataAddress); // Header byte.
        device.Advance();
        _ = device.Read8(KeyboardDataAddress); // State byte.

        Assert.That(assertedCount, Is.EqualTo(2), "Expected one interrupt edge per queued joystick byte.");
    }

    [Test]
    public void JoystickInterrogateModeShouldSuppressEventsUntilEventModeIsRestored()
    {
        var device = new AciaIkbdDevice();
        const byte setJoystickInterrogateModeCommand = 0x15;
        const byte setJoystickEventModeCommand = 0x14;

        device.Write8(KeyboardDataAddress, setJoystickInterrogateModeCommand);
        device.QueueJoystickState(0, new JoystickState(
            IsUpPressed: false,
            IsDownPressed: false,
            IsLeftPressed: false,
            IsRightPressed: true,
            IsFirePressed: false));
        var statusInInterrogateMode = device.Read8(KeyboardStatusAddress);
        device.Write8(KeyboardDataAddress, setJoystickEventModeCommand);
        device.QueueJoystickState(0, new JoystickState(
            IsUpPressed: false,
            IsDownPressed: false,
            IsLeftPressed: false,
            IsRightPressed: true,
            IsFirePressed: false));
        var statusInEventMode = device.Read8(KeyboardStatusAddress);

        Assert.Multiple(() =>
        {
            Assert.That(statusInInterrogateMode & 0x01, Is.Zero);
            Assert.That(statusInEventMode & 0x01, Is.Not.Zero);
        });
    }

    [Test]
    public void SetJoystickEventModeShouldReEmitCurrentActiveState()
    {
        var device = new AciaIkbdDevice();
        const byte setJoystickInterrogateModeCommand = 0x15;
        const byte setJoystickEventModeCommand = 0x14;

        device.Write8(KeyboardDataAddress, setJoystickInterrogateModeCommand);
        device.QueueJoystickState(1, new JoystickState(
            IsUpPressed: false,
            IsDownPressed: false,
            IsLeftPressed: false,
            IsRightPressed: false,
            IsFirePressed: true));
        device.Write8(KeyboardDataAddress, setJoystickEventModeCommand);
        var header = device.Read8(KeyboardDataAddress);
        var state = device.Read8(KeyboardDataAddress);

        Assert.Multiple(() =>
        {
            Assert.That(header, Is.EqualTo(0xFF));
            Assert.That(state, Is.EqualTo(0x80));
        });
    }

    [Test]
    public void JoystickInterrogateCommandShouldReturnLatestState()
    {
        var device = new AciaIkbdDevice();
        const byte setJoystickInterrogateModeCommand = 0x15;
        const byte interrogateJoystickStateCommand = 0x16;

        device.Write8(KeyboardDataAddress, setJoystickInterrogateModeCommand);
        device.QueueJoystickState(0, new JoystickState(
            IsUpPressed: true,
            IsDownPressed: false,
            IsLeftPressed: false,
            IsRightPressed: true,
            IsFirePressed: true));
        device.QueueJoystickState(1, new JoystickState(
            IsUpPressed: true,
            IsDownPressed: false,
            IsLeftPressed: false,
            IsRightPressed: true,
            IsFirePressed: true));
        device.Write8(KeyboardDataAddress, interrogateJoystickStateCommand);
        var header = device.Read8(KeyboardDataAddress);
        var joystick0State = device.Read8(KeyboardDataAddress);
        var joystick1State = device.Read8(KeyboardDataAddress);

        Assert.Multiple(() =>
        {
            Assert.That(header, Is.EqualTo(0xFD));
            Assert.That(joystick0State, Is.EqualTo(0x89));
            Assert.That(joystick1State, Is.EqualTo(0x89));
        });
    }

    [Test]
    public void JoystickInterrogateCommandShouldReturnMostRecentQueuedState()
    {
        var device = new AciaIkbdDevice();
        const byte setJoystickInterrogateModeCommand = 0x15;
        const byte interrogateJoystickStateCommand = 0x16;

        device.Write8(KeyboardDataAddress, setJoystickInterrogateModeCommand);
        device.QueueJoystickState(0, new JoystickState(
            IsUpPressed: true,
            IsDownPressed: false,
            IsLeftPressed: false,
            IsRightPressed: false,
            IsFirePressed: false));
        device.QueueJoystickState(0, new JoystickState(
            IsUpPressed: false,
            IsDownPressed: true,
            IsLeftPressed: true,
            IsRightPressed: false,
            IsFirePressed: false));
        device.Write8(KeyboardDataAddress, interrogateJoystickStateCommand);
        var header = device.Read8(KeyboardDataAddress);
        var joystick0State = device.Read8(KeyboardDataAddress);
        var joystick1State = device.Read8(KeyboardDataAddress);

        Assert.Multiple(() =>
        {
            Assert.That(header, Is.EqualTo(0xFD));
            Assert.That(joystick0State, Is.EqualTo(0x06), "Expected latest joystick 0 state (Down + Left) to be returned.");
            Assert.That(joystick1State, Is.EqualTo(0x00));
        });
    }

    [Test]
    public void JoystickInterrogateCommandShouldCoalesceUnreadResponsesToMostRecentState()
    {
        var device = new AciaIkbdDevice();
        const byte setJoystickInterrogateModeCommand = 0x15;
        const byte interrogateJoystickStateCommand = 0x16;

        device.Write8(KeyboardDataAddress, setJoystickInterrogateModeCommand);
        device.QueueJoystickState(1, new JoystickState(
            IsUpPressed: false,
            IsDownPressed: false,
            IsLeftPressed: true,
            IsRightPressed: false,
            IsFirePressed: false));
        device.Write8(KeyboardDataAddress, interrogateJoystickStateCommand);
        device.QueueJoystickState(1, new JoystickState(
            IsUpPressed: false,
            IsDownPressed: false,
            IsLeftPressed: false,
            IsRightPressed: true,
            IsFirePressed: true));
        device.Write8(KeyboardDataAddress, interrogateJoystickStateCommand);

        var header = device.Read8(KeyboardDataAddress);
        var joystick0State = device.Read8(KeyboardDataAddress);
        var joystick1State = device.Read8(KeyboardDataAddress);
        var statusAfterRead = device.Read8(KeyboardStatusAddress);

        Assert.Multiple(() =>
        {
            Assert.That(header, Is.EqualTo(0xFD));
            Assert.That(joystick0State, Is.EqualTo(0x00));
            Assert.That(joystick1State, Is.EqualTo(0x88));
            Assert.That(statusAfterRead & 0x01, Is.Zero);
        });
    }

    [Test]
    public void JoystickInterrogateCommandShouldReturnNeutralAfterReset()
    {
        var device = new AciaIkbdDevice();
        const byte setJoystickInterrogateModeCommand = 0x15;
        const byte interrogateJoystickStateCommand = 0x16;

        device.Write8(KeyboardDataAddress, setJoystickInterrogateModeCommand);
        device.QueueJoystickState(0, new JoystickState(
            IsUpPressed: false,
            IsDownPressed: false,
            IsLeftPressed: false,
            IsRightPressed: true,
            IsFirePressed: true));

        device.Reset();
        _ = device.Read8(KeyboardDataAddress); // Consume reset acknowledge.
        device.Write8(KeyboardDataAddress, setJoystickInterrogateModeCommand);
        device.Write8(KeyboardDataAddress, interrogateJoystickStateCommand);
        var header = device.Read8(KeyboardDataAddress);
        var joystick0State = device.Read8(KeyboardDataAddress);
        var joystick1State = device.Read8(KeyboardDataAddress);

        Assert.Multiple(() =>
        {
            Assert.That(header, Is.EqualTo(0xFD));
            Assert.That(joystick0State, Is.EqualTo(0x00));
            Assert.That(joystick1State, Is.EqualTo(0x00));
        });
    }

    [Test]
    public void DisableMouseReportingCommandShouldSuppressMousePacketsUntilRelativeModeIsSet()
    {
        var device = new AciaIkbdDevice();

        device.Write8(KeyboardDataAddress, 0x12);
        device.QueueRelativeMousePacket(2, 0, isLeftButtonPressed: false, isRightButtonPressed: false);
        var statusWhileDisabled = device.Read8(KeyboardStatusAddress);
        device.Write8(KeyboardDataAddress, 0x08);
        device.QueueRelativeMousePacket(2, 0, isLeftButtonPressed: false, isRightButtonPressed: false);
        var statusAfterRelativeMode = device.Read8(KeyboardStatusAddress);

        Assert.Multiple(() =>
        {
            Assert.That(statusWhileDisabled & 0x01, Is.Zero);
            Assert.That(statusAfterRelativeMode & 0x01, Is.Not.Zero);
        });
    }

    [Test]
    public void RelativeMousePacketShouldIgnoreYOriginCommands()
    {
        var device = new AciaIkbdDevice();

        device.Write8(KeyboardDataAddress, 0x0F); // Y=0 at bottom.
        device.QueueRelativeMousePacket(0, 5, isLeftButtonPressed: false, isRightButtonPressed: false);
        _ = device.Read8(KeyboardDataAddress); // Header.
        _ = device.Read8(KeyboardDataAddress); // Delta X.
        var deltaYAfterBottomOrigin = device.Read8(KeyboardDataAddress);
        device.Write8(KeyboardDataAddress, 0x10); // Y=0 at top.
        device.QueueRelativeMousePacket(0, 5, isLeftButtonPressed: false, isRightButtonPressed: false);
        _ = device.Read8(KeyboardDataAddress); // Header.
        _ = device.Read8(KeyboardDataAddress); // Delta X.
        var deltaYAfterTopOrigin = device.Read8(KeyboardDataAddress);

        Assert.Multiple(() =>
        {
            Assert.That(deltaYAfterBottomOrigin, Is.EqualTo(0x05));
            Assert.That(deltaYAfterTopOrigin, Is.EqualTo(0x05));
        });
    }

    [Test]
    public void KeyboardStatusShouldSetInterruptFlagWhenReceiveDataIsPending()
    {
        var device = new AciaIkbdDevice();
        device.Write8(KeyboardStatusAddress, 0x80);
        device.QueueKeyboardByte(0x1C);

        var status = device.Read8(KeyboardStatusAddress);

        Assert.That(status & 0x80, Is.Not.Zero);
    }

    [Test]
    public void ResetShouldQueueIkbdResetCompleteCode()
    {
        var device = new AciaIkbdDevice();

        device.Reset();
        var status = device.Read8(KeyboardStatusAddress);
        var value = device.Read8(KeyboardDataAddress);

        Assert.Multiple(() =>
        {
            Assert.That(status & 0x01, Is.Not.Zero);
            Assert.That(value, Is.EqualTo(0xF1));
        });
    }

    [Test]
    public void ClockRegisterWindowShouldNotAliasToKeyboardAcia()
    {
        var device = new AciaIkbdDevice();
        const uint clockWindowStatusAddress = 0x00FFFC3B;
        const uint clockWindowDataAddress = 0x00FFFC3D;

        var status = device.Read8(clockWindowStatusAddress);
        var data = device.Read8(clockWindowDataAddress);

        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(0xFF));
            Assert.That(data, Is.EqualTo(0xFF));
        });
    }
}
