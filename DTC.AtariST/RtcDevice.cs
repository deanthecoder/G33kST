// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using DTC.Emulation;

namespace DTC.AtariST;

/// <summary>
/// Minimal ST real-time clock window at $FFFC20-$FFFC3F.
/// On real hardware this exposes BCD-ish clock/control registers on odd addresses.
/// EmuTOS probes these registers during bring-up, so we model nibble-level read/write behavior.
/// </summary>
public sealed class RtcDevice : IMemDevice
{
    private const uint BaseAddress = 0x00FFFC20;
    private const int RegisterCount = 16;
    private readonly byte[] m_registerNibbles = new byte[RegisterCount];

    /// <inheritdoc />
    public uint FromAddr => BaseAddress;

    /// <inheritdoc />
    public uint ToAddr => BaseAddress + 0x1F;

    /// <summary>
    /// Resets register contents to the host clock.
    /// </summary>
    public void Reset()
    {
        Array.Clear(m_registerNibbles, 0, m_registerNibbles.Length);
        SeedFromHostClock();
    }

    /// <inheritdoc />
    public byte Read8(uint address)
    {
        if (!TryGetRegisterIndex(address, out var registerIndex))
            return 0xFF;

        return (byte)(m_registerNibbles[registerIndex] & 0x0F);
    }

    /// <inheritdoc />
    public void Write8(uint address, byte value)
    {
        if (!TryGetRegisterIndex(address, out var registerIndex))
            return;

        m_registerNibbles[registerIndex] = (byte)(value & 0x0F);
    }

    private void SeedFromHostClock()
    {
        var now = DateTime.Now;
        SetDigit(0, now.Second % 10);
        SetDigit(1, now.Second / 10);
        SetDigit(2, now.Minute % 10);
        SetDigit(3, now.Minute / 10);
        SetDigit(4, now.Hour % 10);
        SetDigit(5, now.Hour / 10);
        SetDigit(6, ((int)now.DayOfWeek + 6) % 7); // 0=Monday style.
        SetDigit(7, now.Day % 10);
        SetDigit(8, now.Day / 10);
        SetDigit(9, now.Month % 10);
        SetDigit(10, now.Month / 10);
        var year = now.Year % 100;
        SetDigit(11, year % 10);
        SetDigit(12, year / 10);
    }

    private void SetDigit(int registerIndex, int digit) =>
        m_registerNibbles[registerIndex] = (byte)(digit & 0x0F);

    private static bool TryGetRegisterIndex(uint address, out int registerIndex)
    {
        registerIndex = -1;
        if (address < BaseAddress || address > BaseAddress + 0x1F)
            return false;

        var offset = (int)(address - BaseAddress);
        if ((offset & 1) == 0)
            return false;

        registerIndex = offset >> 1;
        return registerIndex is >= 0 and < RegisterCount;
    }
}
