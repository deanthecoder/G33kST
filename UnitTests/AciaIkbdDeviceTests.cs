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
    private const long ClockResponseDelayCpuTicks = 56_000;
    private const long ClockResponseInterByteDelayCpuTicks = 10_500;

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
    public void EmptyKeyboardDataReadShouldReturnLastLatchedByte()
    {
        var device = new AciaIkbdDevice();
        device.QueueKeyboardByte(0x3B);

        var firstRead = device.Read8(KeyboardDataAddress);
        var emptyRead = device.Read8(KeyboardDataAddress);

        Assert.Multiple(() =>
        {
            Assert.That(firstRead, Is.EqualTo(0x3B));
            Assert.That(emptyRead, Is.EqualTo(0x3B));
        });
    }

    [Test]
    public void QueueKeyboardByteShouldDropTransientInputBacklogAndPrioritizeKeyboardInput()
    {
        var device = new AciaIkbdDevice();

        device.QueueRelativeMousePacket(5, -2, isLeftButtonPressed: true, isRightButtonPressed: false);
        device.QueueJoystickState(1, new JoystickState(
            IsUpPressed: false,
            IsDownPressed: false,
            IsLeftPressed: false,
            IsRightPressed: true,
            IsFirePressed: true));
        device.QueueKeyboardByte(0x3B);

        var queuedBeforeRead = device.PendingReceiveQueueCount;
        var keyboardBytesBeforeRead = device.PendingKeyboardInjectedByteCount;
        var mouseBytesBeforeRead = device.PendingMousePacketByteCount;
        var joystickBytesBeforeRead = device.PendingJoystickEventByteCount;
        var value = device.Read8(KeyboardDataAddress);
        var statusAfterRead = device.Read8(KeyboardStatusAddress);

        Assert.Multiple(() =>
        {
            Assert.That(queuedBeforeRead, Is.EqualTo(1), "Expected stale mouse/joystick packets to be dropped before keyboard injection.");
            Assert.That(keyboardBytesBeforeRead, Is.EqualTo(1));
            Assert.That(mouseBytesBeforeRead, Is.Zero);
            Assert.That(joystickBytesBeforeRead, Is.Zero);
            Assert.That(value, Is.EqualTo(0x3B));
            Assert.That(statusAfterRead & 0x01, Is.Zero);
        });
    }

    [Test]
    public void QueueKeyboardByteShouldNotDropGenericIkbdResponseBytes()
    {
        var device = new AciaIkbdDevice();

        device.Write8(KeyboardDataAddress, 0x80);
        device.Write8(KeyboardDataAddress, 0x01);
        device.QueueRelativeMousePacket(1, 0, isLeftButtonPressed: false, isRightButtonPressed: false);
        device.QueueKeyboardByte(0x3B);

        var first = device.Read8(KeyboardDataAddress);
        var second = device.Read8(KeyboardDataAddress);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(0xF1), "Reset-ack response should be preserved.");
            Assert.That(second, Is.EqualTo(0xF8), "Queued mouse packet should remain when generic IKBD response bytes are present.");
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
    public void DeferredInterruptReassertCanBeDisabledAtRuntime()
    {
        var device = new AciaIkbdDevice();
        var assertedCount = 0;
        device.KeyboardInterruptLineChanged += isActiveLow =>
        {
            if (isActiveLow)
                assertedCount++;
        };
        device.Write8(KeyboardStatusAddress, 0x80); // Enable receive IRQ signaling.
        device.SetDeferredInterruptReassertEnabled(false);

        device.QueueJoystickState(0, new JoystickState(
            IsUpPressed: false,
            IsDownPressed: false,
            IsLeftPressed: false,
            IsRightPressed: true,
            IsFirePressed: false));

        _ = device.Read8(KeyboardDataAddress); // Header byte.
        device.Advance();
        _ = device.Read8(KeyboardDataAddress); // State byte.

        Assert.That(assertedCount, Is.EqualTo(1), "Expected no deferred reassert edge while feature is disabled.");
    }

    [Test]
    public void ReadingClockResponseBytesShouldRearmKeyboardInterruptForRemainingPacketAfterAdvance()
    {
        var device = new AciaIkbdDevice();
        var assertedCount = 0;
        device.KeyboardInterruptLineChanged += isActiveLow =>
        {
            if (isActiveLow)
                assertedCount++;
        };
        device.Write8(KeyboardStatusAddress, 0x80); // Enable receive IRQ signaling.

        device.Write8(KeyboardDataAddress, 0x1C); // Read clock.
        device.Advance(ClockResponseDelayCpuTicks);

        _ = device.Read8(KeyboardDataAddress); // 0xFC header.
        device.Advance(ClockResponseInterByteDelayCpuTicks);
        _ = device.Read8(KeyboardDataAddress); // First clock byte.

        Assert.That(assertedCount, Is.EqualTo(2), "Expected deferred reassert to flow multi-byte clock response packets.");
    }

    [Test]
    public void MousePacketBytesShouldNotRequireDeferredInterruptReassertAdvance()
    {
        var device = new AciaIkbdDevice();
        var assertedCount = 0;
        device.KeyboardInterruptLineChanged += isActiveLow =>
        {
            if (isActiveLow)
                assertedCount++;
        };
        device.Write8(KeyboardStatusAddress, 0x80); // Enable receive IRQ signaling.

        device.QueueRelativeMousePacket(5, -2, isLeftButtonPressed: false, isRightButtonPressed: false);

        _ = device.Read8(KeyboardDataAddress); // Header byte.
        _ = device.Read8(KeyboardDataAddress); // Delta X.
        var statusBeforeAdvance = device.Read8(KeyboardStatusAddress);
        device.Advance();
        _ = device.Read8(KeyboardDataAddress); // Delta Y.

        Assert.Multiple(() =>
        {
            Assert.That(statusBeforeAdvance & 0x01, Is.Not.Zero, "Expected remaining mouse bytes to stay readable without deferred reassert.");
            Assert.That(assertedCount, Is.EqualTo(1), "Expected no deferred reassert edge for mouse bytes.");
        });
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
    public void JoystickInterrogateResponseCoalescingCanBeDisabledAtRuntime()
    {
        var device = new AciaIkbdDevice();
        const byte setJoystickInterrogateModeCommand = 0x15;
        const byte interrogateJoystickStateCommand = 0x16;
        device.SetJoystickInterrogateCoalescingEnabled(false);

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

        _ = device.Read8(KeyboardDataAddress);
        _ = device.Read8(KeyboardDataAddress);
        _ = device.Read8(KeyboardDataAddress);
        var statusAfterFirstResponse = device.Read8(KeyboardStatusAddress);

        Assert.That(statusAfterFirstResponse & 0x01, Is.Not.Zero, "Expected second unread response to remain queued.");
    }

    [Test]
    public void JoystickInterrogateCoalescingShouldDropOlderRepliesEvenWithMixedQueueBytes()
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
        device.QueueKeyboardByte(0x1C);
        device.Write8(KeyboardDataAddress, interrogateJoystickStateCommand);
        device.QueueJoystickState(1, new JoystickState(
            IsUpPressed: false,
            IsDownPressed: false,
            IsLeftPressed: false,
            IsRightPressed: true,
            IsFirePressed: true));
        device.Write8(KeyboardDataAddress, interrogateJoystickStateCommand);

        var queuedReplyBytes = device.PendingJoystickInterrogateResponseByteCount;
        var queuedKeyboardInjectedBytes = device.PendingKeyboardInjectedByteCount;
        var queuedMousePacketBytes = device.PendingMousePacketByteCount;
        var queuedJoystickEventBytes = device.PendingJoystickEventByteCount;
        var queuedTotalBytes = device.PendingReceiveQueueCount;
        var keyboardByte = device.Read8(KeyboardDataAddress);
        var header = device.Read8(KeyboardDataAddress);
        var joystick0State = device.Read8(KeyboardDataAddress);
        var joystick1State = device.Read8(KeyboardDataAddress);
        var statusAfterRead = device.Read8(KeyboardStatusAddress);

        Assert.Multiple(() =>
        {
            Assert.That(queuedReplyBytes, Is.EqualTo(3), "Expected only the latest interrogate reply to remain queued.");
            Assert.That(queuedKeyboardInjectedBytes, Is.EqualTo(1), "Expected one injected keyboard byte to remain queued.");
            Assert.That(queuedMousePacketBytes, Is.Zero, "Expected no mouse-packet bytes in this queue snapshot.");
            Assert.That(queuedJoystickEventBytes, Is.Zero, "Expected no joystick-event bytes in this queue snapshot.");
            Assert.That(queuedTotalBytes, Is.EqualTo(4), "Expected one keyboard byte plus one interrogate reply triplet.");
            Assert.That(keyboardByte, Is.EqualTo(0x1C));
            Assert.That(header, Is.EqualTo(0xFD));
            Assert.That(joystick0State, Is.EqualTo(0x00));
            Assert.That(joystick1State, Is.EqualTo(0x88));
            Assert.That(statusAfterRead & 0x01, Is.Zero);
        });
    }

    [Test]
    public void ClearReceiveQueueShouldResetQueuedByteKindCountersAndPeakDepth()
    {
        var device = new AciaIkbdDevice();
        const byte setJoystickInterrogateModeCommand = 0x15;
        const byte interrogateJoystickStateCommand = 0x16;

        device.QueueKeyboardByte(0x1C);
        device.QueueRelativeMousePacket(2, -1, isLeftButtonPressed: false, isRightButtonPressed: false);
        device.QueueJoystickState(1, new JoystickState(
            IsUpPressed: false,
            IsDownPressed: false,
            IsLeftPressed: true,
            IsRightPressed: false,
            IsFirePressed: false));
        device.Write8(KeyboardDataAddress, setJoystickInterrogateModeCommand);
        device.Write8(KeyboardDataAddress, interrogateJoystickStateCommand);

        var pendingBeforeClear = device.PendingReceiveQueueCount;
        var peakBeforeClear = device.PeakReceiveQueueCount;
        device.ClearReceiveQueue();

        Assert.Multiple(() =>
        {
            Assert.That(pendingBeforeClear, Is.EqualTo(9), "Expected 1 keyboard + 3 mouse + 2 joystick-event + 3 interrogate bytes.");
            Assert.That(peakBeforeClear, Is.EqualTo(9));
            Assert.That(device.PendingReceiveQueueCount, Is.Zero);
            Assert.That(device.PendingKeyboardInjectedByteCount, Is.Zero);
            Assert.That(device.PendingMousePacketByteCount, Is.Zero);
            Assert.That(device.PendingJoystickEventByteCount, Is.Zero);
            Assert.That(device.PendingJoystickInterrogateResponseByteCount, Is.Zero);
            Assert.That(device.PeakReceiveQueueCount, Is.Zero);
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
    public void DisableMouseReportingCommandShouldDropQueuedMouseBytes()
    {
        var device = new AciaIkbdDevice();

        device.QueueRelativeMousePacket(1, 1, isLeftButtonPressed: false, isRightButtonPressed: false);
        device.Write8(KeyboardDataAddress, 0x12);
        device.QueueJoystickState(1, new JoystickState(
            IsUpPressed: false,
            IsDownPressed: false,
            IsLeftPressed: false,
            IsRightPressed: true,
            IsFirePressed: true));
        var firstByte = device.Read8(KeyboardDataAddress);
        var secondByte = device.Read8(KeyboardDataAddress);

        Assert.Multiple(() =>
        {
            Assert.That(firstByte, Is.EqualTo(0xFF));
            Assert.That(secondByte, Is.EqualTo(0x88));
        });
    }

    [Test]
    public void JoystickMonitoringModeShouldEmitPackedStates()
    {
        var device = new AciaIkbdDevice();

        device.Write8(KeyboardDataAddress, 0x17);
        device.Write8(KeyboardDataAddress, 0x01);
        device.QueueJoystickState(1, new JoystickState(
            IsUpPressed: false,
            IsDownPressed: false,
            IsLeftPressed: false,
            IsRightPressed: true,
            IsFirePressed: true));
        var fireBits = device.Read8(KeyboardDataAddress);
        var directionBits = device.Read8(KeyboardDataAddress);

        Assert.Multiple(() =>
        {
            Assert.That(fireBits, Is.EqualTo(0x01));
            Assert.That(directionBits, Is.EqualTo(0x08));
        });
    }

    [Test]
    public void MouseButtonActionModeShouldReportConfiguredValueAndEmitButtonKeycodes()
    {
        var device = new AciaIkbdDevice();

        device.Write8(KeyboardDataAddress, 0x07);
        device.Write8(KeyboardDataAddress, 0x04);
        device.Write8(KeyboardDataAddress, 0x87);
        var statusReport = new byte[8];
        for (var i = 0; i < statusReport.Length; i++)
            statusReport[i] = device.Read8(KeyboardDataAddress);

        device.QueueRelativeMousePacket(12, -7, isLeftButtonPressed: true, isRightButtonPressed: false);
        var leftPress = device.Read8(KeyboardDataAddress);
        var statusAfterPress = device.Read8(KeyboardStatusAddress);
        device.QueueRelativeMousePacket(0, 0, isLeftButtonPressed: false, isRightButtonPressed: false);
        var leftRelease = device.Read8(KeyboardDataAddress);

        Assert.Multiple(() =>
        {
            Assert.That(statusReport, Is.EqualTo(new byte[] { 0xF6, 0x07, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00 }));
            Assert.That(leftPress, Is.EqualTo(0x74), "0x07 bit 0x04 should map mouse-button transitions to keycodes.");
            Assert.That(statusAfterPress & 0x01, Is.Zero, "Mouse motion bytes should be suppressed in button-action keycode mode.");
            Assert.That(leftRelease, Is.EqualTo(0xF4));
        });
    }

    [Test]
    public void MouseThresholdReportShouldReturnConfiguredThresholdValues()
    {
        var device = new AciaIkbdDevice();

        device.Write8(KeyboardDataAddress, 0x0B);
        device.Write8(KeyboardDataAddress, 0x03);
        device.Write8(KeyboardDataAddress, 0x07);
        device.Write8(KeyboardDataAddress, 0x8B);
        var statusReport = new byte[8];
        for (var i = 0; i < statusReport.Length; i++)
            statusReport[i] = device.Read8(KeyboardDataAddress);

        Assert.That(statusReport, Is.EqualTo(new byte[] { 0xF6, 0x0B, 0x03, 0x07, 0x00, 0x00, 0x00, 0x00 }));
    }

    [Test]
    public void MouseYOriginReportShouldReturnConfiguredOriginMode()
    {
        var device = new AciaIkbdDevice();

        device.Write8(KeyboardDataAddress, 0x0F);
        device.Write8(KeyboardDataAddress, 0x90);
        var bottomReport = new byte[8];
        for (var i = 0; i < bottomReport.Length; i++)
            bottomReport[i] = device.Read8(KeyboardDataAddress);

        device.Write8(KeyboardDataAddress, 0x10);
        device.Write8(KeyboardDataAddress, 0x8F);
        var topReport = new byte[8];
        for (var i = 0; i < topReport.Length; i++)
            topReport[i] = device.Read8(KeyboardDataAddress);

        Assert.Multiple(() =>
        {
            Assert.That(bottomReport, Is.EqualTo(new byte[] { 0xF6, 0x0F, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }));
            Assert.That(topReport, Is.EqualTo(new byte[] { 0xF6, 0x10, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }));
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

    [Test]
    public void SetAndReadClockCommandsShouldReturnClockPacketHeaderAndRawClockBytes()
    {
        var device = new AciaIkbdDevice();
        var expectedClockBytes = new byte[] { 0x25, 0x06, 0x07, 0x12, 0x34, 0x56 };

        device.Write8(KeyboardDataAddress, 0x1B);
        foreach (var b in expectedClockBytes)
            device.Write8(KeyboardDataAddress, b);
        device.Write8(KeyboardDataAddress, 0x1C);
        device.Advance(ClockResponseDelayCpuTicks);

        var header = device.Read8(KeyboardDataAddress);
        var actualClockBytes = new byte[6];
        for (var i = 0; i < actualClockBytes.Length; i++)
        {
            device.Advance(ClockResponseInterByteDelayCpuTicks);
            actualClockBytes[i] = device.Read8(KeyboardDataAddress);
        }

        Assert.Multiple(() =>
        {
            Assert.That(header, Is.EqualTo(0xFC));
            Assert.That(actualClockBytes, Is.EqualTo(expectedClockBytes));
        });
    }

    [Test]
    public void ReadClockCommandShouldDefaultToCurrentHostLocalTime()
    {
        var device = new AciaIkbdDevice();
        var beforeRead = DateTime.Now;

        device.Write8(KeyboardDataAddress, 0x1C);
        device.Advance(ClockResponseDelayCpuTicks);

        var header = device.Read8(KeyboardDataAddress);
        var clockBytes = new byte[6];
        for (var i = 0; i < clockBytes.Length; i++)
        {
            device.Advance(ClockResponseInterByteDelayCpuTicks);
            clockBytes[i] = device.Read8(KeyboardDataAddress);
        }

        var afterRead = DateTime.Now;
        var decodedClock = DecodeIkbdClock(clockBytes);

        Assert.Multiple(() =>
        {
            Assert.That(header, Is.EqualTo(0xFC));
            Assert.That(decodedClock, Is.GreaterThanOrEqualTo(beforeRead.AddSeconds(-1)));
            Assert.That(decodedClock, Is.LessThanOrEqualTo(afterRead.AddSeconds(1)));
        });
    }

    [Test]
    public void RepeatedReadClockCommandsShouldCoalesceToLatestClockResponsePacket()
    {
        var device = new AciaIkbdDevice();

        device.Write8(KeyboardDataAddress, 0x1B);
        foreach (var b in new byte[] { 0x25, 0x06, 0x07, 0x00, 0x00, 0x01 })
            device.Write8(KeyboardDataAddress, b);
        device.Write8(KeyboardDataAddress, 0x1C);
        device.Write8(KeyboardDataAddress, 0x1B);
        foreach (var b in new byte[] { 0x25, 0x06, 0x07, 0x00, 0x00, 0x02 })
            device.Write8(KeyboardDataAddress, b);
        device.Write8(KeyboardDataAddress, 0x1C);
        device.Advance(ClockResponseDelayCpuTicks + (ClockResponseInterByteDelayCpuTicks * 6));

        var values = new byte[7];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = device.Read8(KeyboardDataAddress);
            device.Advance(ClockResponseInterByteDelayCpuTicks);
        }

        Assert.Multiple(() =>
        {
            Assert.That(device.PendingClockResponseByteCount, Is.Zero);
            Assert.That(values, Is.EqualTo(new byte[] { 0xFC, 0x25, 0x06, 0x07, 0x00, 0x00, 0x02 }));
        });
    }

    [Test]
    public void QueueKeyboardByteShouldNotDropBackloggedClockResponsePacket()
    {
        var device = new AciaIkbdDevice();

        device.Write8(KeyboardDataAddress, 0x1B);
        foreach (var b in new byte[] { 0x25, 0x06, 0x07, 0x00, 0x00, 0x01 })
            device.Write8(KeyboardDataAddress, b);
        device.Write8(KeyboardDataAddress, 0x1C);
        device.Advance(ClockResponseDelayCpuTicks); // Header is now queued.
        device.QueueKeyboardByte(0x3B);

        Assert.Multiple(() =>
        {
            Assert.That(device.PendingClockResponseByteCount, Is.EqualTo(1));
            Assert.That(device.PendingKeyboardInjectedByteCount, Is.EqualTo(1));
            Assert.That(device.PendingReceiveQueueCount, Is.EqualTo(2));
            Assert.That(device.Read8(KeyboardDataAddress), Is.EqualTo(0xFC));
        });
    }

    private static DateTime DecodeIkbdClock(ReadOnlySpan<byte> clockBytes)
    {
        Assert.That(clockBytes.Length, Is.EqualTo(6), "Expected YY/MM/DD/hh/mm/ss clock payload.");

        var year = DecodeBcd(clockBytes[0]);
        var month = DecodeBcd(clockBytes[1]);
        var day = DecodeBcd(clockBytes[2]);
        var hour = DecodeBcd(clockBytes[3]);
        var minute = DecodeBcd(clockBytes[4]);
        var second = DecodeBcd(clockBytes[5]);
        var fullYear = year >= 80 ? 1900 + year : 2000 + year;

        return new DateTime(fullYear, month, day, hour, minute, second, DateTimeKind.Local);
    }

    private static int DecodeBcd(byte value)
    {
        var highNibble = (value >> 4) & 0x0F;
        var lowNibble = value & 0x0F;
        Assert.That(highNibble, Is.InRange(0, 9), $"Invalid BCD high nibble in 0x{value:X2}.");
        Assert.That(lowNibble, Is.InRange(0, 9), $"Invalid BCD low nibble in 0x{value:X2}.");
        return (highNibble * 10) + lowNibble;
    }
}
