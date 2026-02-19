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
using System.Runtime.CompilerServices;

namespace DTC.AtariST;

/// <summary>
/// Minimal Atari ST DMA/FDC register block for floppy-drive detection.
/// </summary>
/// <remarks>
/// This is intentionally narrow: it implements enough command/status behavior
/// for TOS to detect attached floppy drives and continue booting gracefully
/// with no media inserted.
/// </remarks>
public sealed class FloppyDmaFdcDevice : IMemDevice
{
    private const uint BaseAddress = 0x00FF8600;
    private const uint DataRegisterAddress = BaseAddress + 0x04;
    private const uint ControlRegisterAddress = BaseAddress + 0x06;
    private const uint DmaAddressHighRegisterAddress = BaseAddress + 0x09;
    private const uint DmaAddressMidRegisterAddress = BaseAddress + 0x0B;
    private const uint DmaAddressLowRegisterAddress = BaseAddress + 0x0D;
    private const uint ModeControlRegisterAddress = BaseAddress + 0x0F;
    private const ushort DmaA0 = 0x0002;
    private const ushort DmaA1 = 0x0004;
    private const ushort DmaScReg = 0x0010;
    private const ushort DmaCsAcsi = 0x0008;
    private const ushort DmaDrqFloppy = 0x0080;
    private const byte FdcTypeOpcodeMask = 0xF0;
    private const byte FdcTrackZeroMask = 0x04;
    private const byte FdcCrcErrorMask = 0x08;
    private const byte FdcRecordNotFoundMask = 0x10;
    private const byte FdcWriteProtectMask = 0x40;
    private const int SectorSizeBytes = 512;
    private const int MaxTraceLines = 256;
    private const uint DmaTransferAddressMaskSt = 0x003F_FFFF;
    private const uint DmaAddressMaskSt = 0x003F_FFFE;
    private const double DefaultTransferSpeedMultiplier = 1.5;
    private const double FloppyRotationRpm = 300.0;
    private const int DefaultSectorsPerTrackTiming = 9;
    private const double TypeICommandBaseMilliseconds = 2.0;
    private const double TypeIiWriteRejectMilliseconds = 3.0;
    private const double TypeIiiForceInterruptMilliseconds = 0.2;
    private static readonly int[] PreferredSectorSizes = [9, 10, 11, 8, 12];
    private readonly Lock m_stateLock = new();
    private readonly bool[] m_drivePresent;
    private readonly byte[][] m_mountedImageByDrive;
    private readonly string[] m_mountedImageNameByDrive;
    private readonly bool[] m_writeProtectedByDrive;
    private readonly long m_cpuHz;
    private readonly double m_transferSpeedMultiplier;
    private readonly bool m_hasTimingModel;
    private readonly Queue<string> m_traceLines = [];
    private ushort m_controlRegister;
    private byte m_dataHighByte;
    private byte m_controlHighByte;
    private byte m_fdcStatusRegister;
    private byte m_fdcTrackRegister;
    private byte m_fdcSectorRegister;
    private byte m_fdcDataRegister;
    private byte m_modeControlRegister;
    private byte m_sectorCountRegister;
    private byte m_dmaStatusBits = 0x01;
    private byte m_lastCommand;
    private byte m_lastStatusWord;
    private ushort m_recentDataRegisterValue;
    private ushort m_lastDmaStatusWord;
    private ushort m_dataReadLatch;
    private bool m_hasDataReadLatch;
    private ushort m_statusReadLatch;
    private bool m_hasStatusReadLatch;
    private uint m_dmaAddressRegister;
    private uint m_dmaAddressLimitExclusive = DmaTransferAddressMaskSt + 1;
    private byte m_selectedSide;
    private int m_selectedDrive = -1;
    private bool m_interruptLineIsActiveLow;
    private bool m_statusWordRepresentsTypeI = true;
    private long m_commandCount;
    private long m_readSectorCommandCount;
    private long m_successfulReadSectorCommandCount;
    private long m_dmaBytesWritten;
    private string m_lastTraceLine = string.Empty;
    private bool m_isTraceEnabled;
    private bool m_hasPendingCompletion;
    private long m_pendingCompletionTicks;
    private byte m_pendingCompletionStatus;
    private bool m_pendingCompletionHasDmaError;
    private string m_pendingCompletionDetail = string.Empty;

    /// <inheritdoc />
    public uint FromAddr => BaseAddress;

    /// <inheritdoc />
    public uint ToAddr => BaseAddress + 0x0F;

    /// <summary>
    /// Raised when the emulated FDC interrupt/completion line changes.
    /// </summary>
    public event Action<bool> InterruptLineChanged;

    /// <summary>
    /// Callback used to write DMA transfer bytes into system memory.
    /// </summary>
    public Action<uint, byte> DmaWrite8 { private get; set; }
    
    /// <summary>
    /// Sets whether floppy trace lines are captured.
    /// </summary>
    /// <remarks>
    /// Disabled by default so normal emulation stays allocation-light.
    /// </remarks>
    public void SetTraceEnabled(bool value)
    {
        lock (m_stateLock)
        {
            if (m_isTraceEnabled == value)
                return;

            m_isTraceEnabled = value;
            if (m_isTraceEnabled)
                return;

            m_traceLines.Clear();
            m_lastTraceLine = string.Empty;
        }
    }

    public FloppyDmaFdcDevice(bool driveAPresent = true, bool driveBPresent = false, long cpuHz = 0, double transferSpeedMultiplier = DefaultTransferSpeedMultiplier)
    {
        m_drivePresent = [driveAPresent, driveBPresent];
        m_mountedImageByDrive = new byte[m_drivePresent.Length][];
        m_mountedImageNameByDrive = new string[m_drivePresent.Length];
        m_writeProtectedByDrive = new bool[m_drivePresent.Length];
        m_cpuHz = Math.Max(0, cpuHz);
        m_transferSpeedMultiplier = transferSpeedMultiplier <= 0 ? DefaultTransferSpeedMultiplier : transferSpeedMultiplier;
        m_hasTimingModel = m_cpuHz > 0;
        Reset();
    }

    /// <summary>
    /// Returns a snapshot of recent floppy command activity for diagnostics.
    /// </summary>
    public FloppyDebugStats GetDebugStats()
    {
        lock (m_stateLock)
            return new FloppyDebugStats(
                m_commandCount,
                m_readSectorCommandCount,
                m_successfulReadSectorCommandCount,
                m_dmaBytesWritten,
                m_lastCommand,
                m_lastStatusWord,
                m_lastDmaStatusWord,
                m_lastTraceLine);
    }

    /// <summary>
    /// Returns up to <paramref name="maxLines"/> of the latest floppy trace lines.
    /// </summary>
    public IReadOnlyList<string> GetRecentTraceLines(int maxLines)
    {
        lock (m_stateLock)
        {
            if (maxLines <= 0 || m_traceLines.Count == 0)
                return [];
            if (maxLines >= m_traceLines.Count)
                return [.. m_traceLines];
            return [.. m_traceLines.Skip(m_traceLines.Count - maxLines)];
        }
    }

    /// <summary>
    /// Restricts floppy DMA writes to the configured ST-RAM window.
    /// </summary>
    public void ConfigureDmaAddressLimit(uint addressLimitExclusive)
    {
        lock (m_stateLock)
            m_dmaAddressLimitExclusive = addressLimitExclusive == 0 ? 1 : addressLimitExclusive;
    }

    /// <summary>
    /// Advances pending FDC command completion timing.
    /// </summary>
    public void Advance(long deltaTicks)
    {
        if (deltaTicks <= 0)
            return;

        lock (m_stateLock)
        {
            if (!m_hasPendingCompletion)
                return;

            m_pendingCompletionTicks -= deltaTicks;
            if (m_pendingCompletionTicks > 0)
                return;

            CompletePendingCommandNoLock();
        }
    }

    /// <summary>
    /// Tries to mount one floppy image into the requested drive.
    /// </summary>
    public bool TryMountImage(int driveIndex, byte[] imageData, string imageName)
    {
        if (imageData == null || imageData.Length == 0)
            return false;

        lock (m_stateLock)
        {
            if (!IsValidDriveIndex(driveIndex) || !m_drivePresent[driveIndex])
                return false;

            m_mountedImageByDrive[driveIndex] = [.. imageData];
            m_mountedImageNameByDrive[driveIndex] = imageName;
            m_writeProtectedByDrive[driveIndex] = true;
            AddTraceLine($"Mount drive={GetDriveName(driveIndex)} bytes={imageData.Length} name='{imageName}'.");
            return true;
        }
    }

    /// <summary>
    /// Unmounts any currently mounted floppy image from the requested drive.
    /// </summary>
    public void UnmountImage(int driveIndex)
    {
        lock (m_stateLock)
        {
            if (!IsValidDriveIndex(driveIndex))
                return;

            m_mountedImageByDrive[driveIndex] = null;
            m_mountedImageNameByDrive[driveIndex] = null;
            m_writeProtectedByDrive[driveIndex] = false;
            AddTraceLine($"Unmount drive={GetDriveName(driveIndex)}.");
        }
    }

    /// <summary>
    /// Returns <c>true</c> when a drive exists and currently has a mounted image.
    /// </summary>
    public bool IsImageMounted(int driveIndex)
    {
        lock (m_stateLock)
            return IsValidDriveIndex(driveIndex) && m_mountedImageByDrive[driveIndex] != null;
    }

    /// <summary>
    /// Gets the mounted image display name for the selected drive.
    /// </summary>
    public string GetMountedImageName(int driveIndex)
    {
        lock (m_stateLock)
            return IsValidDriveIndex(driveIndex) ? m_mountedImageNameByDrive[driveIndex] : null;
    }

    /// <summary>
    /// Resets DMA/FDC state to power-on defaults.
    /// </summary>
    public void Reset()
    {
        lock (m_stateLock)
        {
            m_controlRegister = 0;
            m_dataHighByte = 0;
            m_controlHighByte = 0;
            m_fdcStatusRegister = 0;
            m_fdcTrackRegister = 0;
            m_fdcSectorRegister = 0;
            m_fdcDataRegister = 0;
            m_modeControlRegister = 0;
            m_sectorCountRegister = 0;
            m_dmaStatusBits = 0x01;
            m_recentDataRegisterValue = 0;
            m_dataReadLatch = 0;
            m_hasDataReadLatch = false;
            m_statusReadLatch = 0;
            m_hasStatusReadLatch = false;
            m_dmaAddressRegister = 0;
            m_lastCommand = 0;
            m_lastStatusWord = 0;
            m_lastDmaStatusWord = 0;
            m_commandCount = 0;
            m_readSectorCommandCount = 0;
            m_successfulReadSectorCommandCount = 0;
            m_dmaBytesWritten = 0;
            m_statusWordRepresentsTypeI = true;
            m_traceLines.Clear();
            m_lastTraceLine = string.Empty;
            m_hasPendingCompletion = false;
            m_pendingCompletionTicks = 0;
            m_pendingCompletionStatus = 0;
            m_pendingCompletionHasDmaError = false;
            m_pendingCompletionDetail = string.Empty;
            m_selectedSide = 0;
            m_selectedDrive = -1;
            SetInterruptLine(activeLow: false);
            AddTraceLine("Reset.");
        }
    }

    /// <summary>
    /// Applies the PSG port-A drive-select/side lines to the controller.
    /// </summary>
    public void ApplyPortA(byte portAValue)
    {
        lock (m_stateLock)
        {
            var oldDrive = m_selectedDrive;
            var oldSide = m_selectedSide;
            var driveASelected = (portAValue & 0x02) == 0;
            var driveBSelected = (portAValue & 0x04) == 0;
            // PSG bit 0 is driven as an active-low side-select line on ST hardware.
            m_selectedSide = (byte)((portAValue & 0x01) == 0 ? 1 : 0);

            if (driveASelected && !driveBSelected)
                m_selectedDrive = 0;
            else if (driveBSelected && !driveASelected)
                m_selectedDrive = 1;
            else if (driveASelected)
                m_selectedDrive = 0;
            else
                m_selectedDrive = -1;

            if (m_selectedDrive != oldDrive || m_selectedSide != oldSide)
                AddTraceLine($"Select drive={GetDriveName(m_selectedDrive)} side={m_selectedSide}.");
        }
    }

    /// <inheritdoc />
    public byte Read8(uint address)
    {
        lock (m_stateLock)
        {
            if (address < BaseAddress || address > ToAddr)
                return 0xFF;
            if (address == DataRegisterAddress)
            {
                m_dataReadLatch = ReadDataRegister();
                m_hasDataReadLatch = true;
                return (byte)(m_dataReadLatch >> 8);
            }
            if (address == DataRegisterAddress + 1)
            {
                if (!m_hasDataReadLatch)
                    m_dataReadLatch = ReadDataRegister();
                m_hasDataReadLatch = false;
                return (byte)(m_dataReadLatch & 0xFF);
            }
            if (address == ControlRegisterAddress)
            {
                m_statusReadLatch = BuildDmaStatusWord();
                m_hasStatusReadLatch = true;
                return (byte)(m_statusReadLatch >> 8);
            }
            if (address == ControlRegisterAddress + 1)
            {
                if (!m_hasStatusReadLatch)
                    m_statusReadLatch = BuildDmaStatusWord();
                m_hasStatusReadLatch = false;
                return (byte)(m_statusReadLatch & 0xFF);
            }
            if (address == DmaAddressHighRegisterAddress)
                return (byte)((m_dmaAddressRegister >> 16) & 0xFF);
            if (address == DmaAddressMidRegisterAddress)
                return (byte)((m_dmaAddressRegister >> 8) & 0xFF);
            if (address == DmaAddressLowRegisterAddress)
                return (byte)(m_dmaAddressRegister & 0xFF);
            if (address == ModeControlRegisterAddress)
                return m_modeControlRegister;
            return 0xFF;
        }
    }

    /// <inheritdoc />
    public void Write8(uint address, byte value)
    {
        lock (m_stateLock)
        {
            if (address < BaseAddress || address > ToAddr)
                return;
            if (address == DataRegisterAddress)
            {
                m_dataHighByte = value;
                return;
            }
            if (address == DataRegisterAddress + 1)
            {
                var wordValue = (ushort)((m_dataHighByte << 8) | value);
                m_recentDataRegisterValue = wordValue;
                m_hasDataReadLatch = false;
                m_hasStatusReadLatch = false;
                WriteDataRegister(wordValue);
                return;
            }
            if (address == ControlRegisterAddress)
            {
                m_controlHighByte = value;
                return;
            }
            if (address == ControlRegisterAddress + 1)
            {
                var previousMode = m_controlRegister;
                m_controlRegister = (ushort)((m_controlHighByte << 8) | value);
                m_hasStatusReadLatch = false;
                if (previousMode != m_controlRegister)
                    AddTraceLine($"Mode=0x{m_controlRegister:X4} (was 0x{previousMode:X4}).");
                if (((previousMode ^ m_controlRegister) & 0x0100) != 0)
                {
                    ResetDmaTransferStateNoLock();
                    AddTraceLine($"DMA WRBIT toggled: mode 0x{previousMode:X4} -> 0x{m_controlRegister:X4}.");
                }
                return;
            }
            if (address == DmaAddressHighRegisterAddress)
            {
                var previousAddress = m_dmaAddressRegister;
                m_dmaAddressRegister = NormalizeDmaAddress((m_dmaAddressRegister & 0x0000FFFF) | ((uint)(value & 0x3F) << 16));
                if (m_dmaAddressRegister != previousAddress)
                    AddTraceLine($"DMA address high write 0x{value:X2} -> 0x{m_dmaAddressRegister:X6}.");
                return;
            }
            if (address == DmaAddressMidRegisterAddress)
            {
                var previousAddress = m_dmaAddressRegister;
                m_dmaAddressRegister = NormalizeDmaAddress((m_dmaAddressRegister & 0x00FF00FF) | ((uint)value << 8));
                if (m_dmaAddressRegister != previousAddress)
                    AddTraceLine($"DMA address mid write 0x{value:X2} -> 0x{m_dmaAddressRegister:X6}.");
                return;
            }
            if (address == DmaAddressLowRegisterAddress)
            {
                var previousAddress = m_dmaAddressRegister;
                m_dmaAddressRegister = NormalizeDmaAddress((m_dmaAddressRegister & 0x00FFFF00) | (uint)(value & 0xFE));
                if (m_dmaAddressRegister != previousAddress)
                    AddTraceLine($"DMA address low write 0x{value:X2} -> 0x{m_dmaAddressRegister:X6}.");
                return;
            }
            if (address == ModeControlRegisterAddress)
                m_modeControlRegister = value;
        }
    }

    private ushort ReadDataRegister()
    {
        if (!IsFloppyRegisterAccess(m_controlRegister))
            return 0;
        if ((m_controlRegister & DmaScReg) != 0)
            return m_recentDataRegisterValue;

        var registerSelect = m_controlRegister & (DmaA1 | DmaA0);
        return registerSelect switch
        {
            0 => ReadStatusWord(),
            DmaA0 => m_fdcTrackRegister,
            DmaA1 => m_fdcSectorRegister,
            DmaA1 | DmaA0 => m_fdcDataRegister,
            _ => 0
        };
    }

    private ushort ReadStatusWord()
    {
        var status = BuildDriveStatus();
        m_lastStatusWord = status;
        AddTraceLine($"Read status=0x{status:X2} dma=0x{BuildDmaStatusWord():X4} cmd=0x{m_lastCommand:X2}.");

        // Reading status acknowledges command completion on ST software paths.
        SetInterruptLine(activeLow: false);
        return status;
    }

    private void WriteDataRegister(ushort value)
    {
        if (!IsFloppyRegisterAccess(m_controlRegister))
            return;
        if ((m_controlRegister & DmaScReg) != 0)
        {
            m_sectorCountRegister = (byte)(value & 0xFF);
            UpdateSectorCountStatusNoLock();
            return;
        }

        var registerSelect = m_controlRegister & (DmaA1 | DmaA0);
        var byteValue = (byte)(value & 0xFF);
        switch (registerSelect)
        {
            case 0:
                ExecuteCommand(byteValue);
                return;
            case DmaA0:
                m_fdcTrackRegister = byteValue;
                return;
            case DmaA1:
                m_fdcSectorRegister = byteValue;
                return;
            case DmaA1 | DmaA0:
                m_fdcDataRegister = byteValue;
                return;
        }
    }

    private void ExecuteCommand(byte command)
    {
        var opcode = (byte)(command & FdcTypeOpcodeMask);
        if (m_hasPendingCompletion && opcode != 0xD0)
        {
            AddTraceLine($"Command 0x{command:X2} ignored while previous command is still in progress.");
            return;
        }

        m_commandCount++;
        m_lastCommand = command;
        m_statusWordRepresentsTypeI = IsTypeICommand(command);

        // Starting a new command clears any previous completion IRQ state.
        SetInterruptLine(activeLow: false);
        if (opcode == 0xD0)
            CancelPendingCompletionNoLock();
        if (opcode is 0x80 or 0x90)
            m_readSectorCommandCount++;
        var driveConnected = IsSelectedDrivePresent(out var selectedDrive);
        AddTraceLine($"Command 0x{command:X2} ({DescribeCommand(opcode)}) drive={GetDriveName(selectedDrive)} track={m_fdcTrackRegister} sector={m_fdcSectorRegister} side={m_selectedSide} sc={m_sectorCountRegister} dma=0x{m_dmaAddressRegister:X6}.");

        switch (opcode)
        {
            case 0xD0: // Force interrupt.
                CompleteCommand(0, hasDmaError: false, "Force interrupt.", GetCommandDelayTicks(opcode, isMultiSectorRead: false));
                return;

            case 0x00: // Restore.
                if (driveConnected)
                {
                    m_fdcTrackRegister = 0;
                    CompleteCommand(0, hasDmaError: false, "Restore.", GetCommandDelayTicks(opcode, isMultiSectorRead: false));
                }
                else
                    CompleteCommand(FdcRecordNotFoundMask, hasDmaError: true, "Restore failed: no selected drive.", GetCommandDelayTicks(opcode, isMultiSectorRead: false));
                return;

            case 0x10: // Seek.
                if (driveConnected)
                {
                    m_fdcTrackRegister = m_fdcDataRegister;
                    CompleteCommand(0, hasDmaError: false, "Seek.", GetCommandDelayTicks(opcode, isMultiSectorRead: false));
                }
                else
                    CompleteCommand(FdcRecordNotFoundMask, hasDmaError: true, "Seek failed: no selected drive.", GetCommandDelayTicks(opcode, isMultiSectorRead: false));
                return;

            case 0x20: // Step.
            case 0x30: // Step + update.
                CompleteCommand(
                    driveConnected ? (byte)0 : FdcRecordNotFoundMask,
                    hasDmaError: !driveConnected,
                    driveConnected ? "Step." : "Step failed: no selected drive.",
                    GetCommandDelayTicks(opcode, isMultiSectorRead: false));
                return;

            case 0x40: // Step in.
            case 0x50: // Step in + update.
                if (driveConnected && m_fdcTrackRegister < byte.MaxValue)
                    m_fdcTrackRegister++;
                CompleteCommand(
                    driveConnected ? (byte)0 : FdcRecordNotFoundMask,
                    hasDmaError: !driveConnected,
                    driveConnected ? "Step-in." : "Step-in failed: no selected drive.",
                    GetCommandDelayTicks(opcode, isMultiSectorRead: false));
                return;

            case 0x60: // Step out.
            case 0x70: // Step out + update.
                if (driveConnected && m_fdcTrackRegister > 0)
                    m_fdcTrackRegister--;
                CompleteCommand(
                    driveConnected ? (byte)0 : FdcRecordNotFoundMask,
                    hasDmaError: !driveConnected,
                    driveConnected ? "Step-out." : "Step-out failed: no selected drive.",
                    GetCommandDelayTicks(opcode, isMultiSectorRead: false));
                return;

            case 0x80: // Read sector (single).
            case 0x90: // Read sector (multi).
                if (!driveConnected)
                {
                    CompleteCommand(FdcRecordNotFoundMask, hasDmaError: true, "Read sector failed: no selected drive.", GetCommandDelayTicks(opcode, isMultiSectorRead: opcode == 0x90));
                    return;
                }

                var isMultiSectorRead = opcode == 0x90;
                if (TryReadSectorIntoDma(selectedDrive, isMultiSectorRead, out var readSectorHasDmaError))
                    CompleteCommand(0, hasDmaError: readSectorHasDmaError, isMultiSectorRead ? "Read multi-sector." : "Read sector.", GetCommandDelayTicks(opcode, isMultiSectorRead));
                else
                    CompleteCommand(FdcRecordNotFoundMask, hasDmaError: true, "Read sector failed.", GetCommandDelayTicks(opcode, isMultiSectorRead));
                return;

            case 0xA0: // Write sector (single).
            case 0xB0: // Write sector (multi).
                // For now mounted media is read-only.
                CompleteCommand(FdcWriteProtectMask, hasDmaError: true, "Write sector rejected: read-only media.", GetCommandDelayTicks(opcode, isMultiSectorRead: opcode == 0xB0));
                return;

            case 0xC0: // Read address.
                if (!driveConnected)
                {
                    CompleteCommand(FdcRecordNotFoundMask, hasDmaError: true, "Read address failed: no selected drive.", GetCommandDelayTicks(opcode, isMultiSectorRead: false));
                    return;
                }

                if (TryReadAddressIntoDma(selectedDrive, out var readAddressHasDmaError))
                    CompleteCommand(0, hasDmaError: readAddressHasDmaError, "Read address.", GetCommandDelayTicks(opcode, isMultiSectorRead: false));
                else
                    CompleteCommand(FdcRecordNotFoundMask, hasDmaError: true, "Read address failed.", GetCommandDelayTicks(opcode, isMultiSectorRead: false));
                return;

            case 0xE0: // Read track.
            case 0xF0: // Write track.
                CompleteCommand(
                    driveConnected ? FdcRecordNotFoundMask : (byte)(FdcRecordNotFoundMask | FdcWriteProtectMask),
                    hasDmaError: true,
                    "Track command not implemented.",
                    GetCommandDelayTicks(opcode, isMultiSectorRead: false));
                return;

            default:
                CompleteCommand(0, hasDmaError: false, "Unhandled opcode treated as no-op.", GetCommandDelayTicks(opcode, isMultiSectorRead: false));
                return;
        }
    }

    private void CompleteCommand(byte status, bool hasDmaError, string detail, long delayTicks = 0)
    {
        if (delayTicks > 0)
        {
            m_hasPendingCompletion = true;
            m_pendingCompletionTicks = delayTicks;
            m_pendingCompletionStatus = status;
            m_pendingCompletionHasDmaError = hasDmaError;
            m_pendingCompletionDetail = detail;
            return;
        }

        CompleteCommandNoLock(status, hasDmaError, detail);
    }

    private void CompleteCommandNoLock(byte status, bool hasDmaError, string detail)
    {
        m_fdcStatusRegister = status;
        SetDmaErrorStatusNoLock(hasDmaError);
        UpdateSectorCountStatusNoLock();
        m_lastStatusWord = status;
        SetInterruptLine(activeLow: true);
        AddTraceLine($"{detail} status=0x{status:X2} dma=0x{BuildDmaStatusWord():X4}.");
    }

    private void CompletePendingCommandNoLock()
    {
        if (!m_hasPendingCompletion)
            return;

        var status = m_pendingCompletionStatus;
        var hasDmaError = m_pendingCompletionHasDmaError;
        var detail = m_pendingCompletionDetail;
        CancelPendingCompletionNoLock();
        CompleteCommandNoLock(status, hasDmaError, detail);
    }

    private void CancelPendingCompletionNoLock()
    {
        m_hasPendingCompletion = false;
        m_pendingCompletionTicks = 0;
        m_pendingCompletionStatus = 0;
        m_pendingCompletionHasDmaError = false;
        m_pendingCompletionDetail = string.Empty;
    }

    private long GetCommandDelayTicks(byte opcode, bool isMultiSectorRead)
    {
        if (!m_hasTimingModel)
            return 0;

        var milliseconds = opcode switch
        {
            0xD0 => TypeIiiForceInterruptMilliseconds,
            0x00 or 0x10 or 0x20 or 0x30 or 0x40 or 0x50 or 0x60 or 0x70 => TypeICommandBaseMilliseconds,
            0x80 => GetReadSectorDelayMilliseconds(1),
            0x90 => GetReadSectorDelayMilliseconds(isMultiSectorRead ? Math.Max(1, (int)m_sectorCountRegister) : 1),
            0xA0 or 0xB0 => TypeIiWriteRejectMilliseconds,
            0xC0 => GetReadSectorDelayMilliseconds(1),
            0xE0 or 0xF0 => GetReadSectorDelayMilliseconds(1),
            _ => TypeICommandBaseMilliseconds
        };

        var cycles = milliseconds * m_cpuHz / 1000.0;
        return Math.Max(1, (long)Math.Round(cycles));
    }

    private double GetReadSectorDelayMilliseconds(int sectorCount)
    {
        var sectors = Math.Max(1, sectorCount);
        var sectorsPerSecond = (FloppyRotationRpm / 60.0) * DefaultSectorsPerTrackTiming;
        var millisecondsPerSectorSlot = 1000.0 / sectorsPerSecond;
        return (sectors * millisecondsPerSectorSlot) / m_transferSpeedMultiplier;
    }

    private byte BuildDriveStatus()
    {
        var status = m_fdcStatusRegister;
        if (!IsSelectedDrivePresent(out var driveIndex))
        {
            // On ST hardware, "no selected/connected drive" reads as write-protected input.
            if (m_statusWordRepresentsTypeI)
                status = (byte)(status & ~(FdcTrackZeroMask | FdcCrcErrorMask));
            status |= FdcWriteProtectMask;
            return status;
        }

        var hasDisk = m_mountedImageByDrive[driveIndex] != null;
        var isWriteProtected = !hasDisk || m_writeProtectedByDrive[driveIndex];

        if (m_statusWordRepresentsTypeI)
        {
            // Type-I status updates some signal-backed bits (track 0 / write-protect) live.
            status = (byte)(status & ~FdcCrcErrorMask);
            if (m_fdcTrackRegister == 0)
                status |= FdcTrackZeroMask;
            else
                status = (byte)(status & ~FdcTrackZeroMask);

            if (isWriteProtected)
                status |= FdcWriteProtectMask;
            else
                status = (byte)(status & ~FdcWriteProtectMask);
        }
        else
        {
            // Type-II/III commands treat bit2 differently and do not mirror write-protect live.
            status = (byte)(status & ~FdcTrackZeroMask);
        }

        return status;
    }

    private static bool IsTypeICommand(byte command) =>
        (command & 0x80) == 0 || (command & FdcTypeOpcodeMask) == 0xD0;

    private static string DescribeCommand(byte opcode) =>
        opcode switch
        {
            0x00 => "RESTORE",
            0x10 => "SEEK",
            0x20 => "STEP",
            0x30 => "STEP+UPDATE",
            0x40 => "STEP-IN",
            0x50 => "STEP-IN+UPDATE",
            0x60 => "STEP-OUT",
            0x70 => "STEP-OUT+UPDATE",
            0x80 => "READ-SECTOR",
            0x90 => "READ-SECTOR-MULTI",
            0xA0 => "WRITE-SECTOR",
            0xB0 => "WRITE-SECTOR-MULTI",
            0xC0 => "READ-ADDRESS",
            0xD0 => "FORCE-INTERRUPT",
            0xE0 => "READ-TRACK",
            0xF0 => "WRITE-TRACK",
            _ => "UNKNOWN"
        };

    private void AddTraceLine(string message)
    {
        if (m_isTraceEnabled)
            AddTraceLineCore(message);
    }

    private void AddTraceLine([InterpolatedStringHandlerArgument("")] FloppyTraceInterpolatedStringHandler message)
    {
        if (message.IsEnabled)
            AddTraceLineCore(message.ToStringAndClear());
    }

    private void AddTraceLineCore(string message)
    {
        var line = $"{m_commandCount,6} | {message}";
        m_lastTraceLine = line;
        m_traceLines.Enqueue(line);
        while (m_traceLines.Count > MaxTraceLines)
            _ = m_traceLines.Dequeue();
    }

    private static string GetDriveName(int driveIndex) =>
        driveIndex switch
        {
            0 => "A",
            1 => "B",
            _ => "-"
        };

    private bool IsSelectedDrivePresent(out int driveIndex)
    {
        driveIndex = m_selectedDrive;
        if (m_selectedDrive < 0 || m_selectedDrive >= m_drivePresent.Length)
            return false;
        return m_drivePresent[m_selectedDrive];
    }

    private bool TryReadSectorIntoDma(int driveIndex, bool isMultiSectorTransfer, out bool hasDmaError)
    {
        hasDmaError = false;
        if (!TryGetSelectedDriveImage(driveIndex, out var imageData))
        {
            AddTraceLine("Read sector failed: no mounted image.");
            return false;
        }
        if (!TryInferGeometry(imageData, out var geometry))
        {
            AddTraceLine("Read sector failed: geometry inference failed.");
            return false;
        }
        if (!TryGetLinearSectorIndex(geometry, m_fdcTrackRegister, m_selectedSide, m_fdcSectorRegister, out var firstSectorIndex))
        {
            AddTraceLine($"Read sector failed: invalid CHS track={m_fdcTrackRegister} side={m_selectedSide} sector={m_fdcSectorRegister}.");
            return false;
        }
        if (DmaWrite8 == null)
        {
            AddTraceLine("Read sector failed: no DMA write callback.");
            return false;
        }

        if (m_sectorCountRegister == 0)
        {
            // Real ST DMA reports a transfer error when data arrives while sector count is zero.
            hasDmaError = true;
            AddTraceLine("Read sector aborted: sector count register is zero.");
            return true;
        }

        var sectorsToTransfer = isMultiSectorTransfer ? m_sectorCountRegister : 1;
        var dmaAddress = NormalizeDmaTransferAddress(m_dmaAddressRegister);
        var transferBytes = sectorsToTransfer * SectorSizeBytes;
        if (!IsDmaRangeValid(dmaAddress, transferBytes))
        {
            AddTraceLine($"Read sector failed: DMA range 0x{dmaAddress:X6}+{transferBytes} exceeds ST-RAM limit 0x{m_dmaAddressLimitExclusive:X6}.");
            return false;
        }

        for (var transferIndex = 0; transferIndex < sectorsToTransfer; transferIndex++)
        {
            var linearSector = firstSectorIndex + transferIndex;
            var byteOffset = linearSector * SectorSizeBytes;
            if (byteOffset + SectorSizeBytes > imageData.Length)
            {
                AddTraceLine($"Read sector failed: image index {linearSector} out of range.");
                return false;
            }

            for (var i = 0; i < SectorSizeBytes; i++)
            {
                DmaWrite8(NormalizeDmaTransferAddress(dmaAddress), imageData[byteOffset + i]);
                dmaAddress = NormalizeDmaTransferAddress(dmaAddress + 1);
            }
        }

        m_dmaAddressRegister = NormalizeDmaAddress(dmaAddress);
        m_sectorCountRegister = isMultiSectorTransfer
            ? (byte)0
            : (byte)Math.Max(0, m_sectorCountRegister - 1);
        m_successfulReadSectorCommandCount++;
        m_dmaBytesWritten += transferBytes;
        AddTraceLine($"Read sector ok: sectors={sectorsToTransfer} bytes={transferBytes} dma=0x{m_dmaAddressRegister:X6}.");
        return true;
    }

    private bool TryReadAddressIntoDma(int driveIndex, out bool hasDmaError)
    {
        hasDmaError = false;
        if (!TryGetSelectedDriveImage(driveIndex, out var imageData))
        {
            AddTraceLine("Read address failed: no mounted image.");
            return false;
        }
        if (!TryInferGeometry(imageData, out var geometry))
        {
            AddTraceLine("Read address failed: geometry inference failed.");
            return false;
        }
        if (DmaWrite8 == null)
        {
            AddTraceLine("Read address failed: no DMA write callback.");
            return false;
        }

        var sector = m_fdcSectorRegister;
        if (sector < 1 || sector > geometry.SectorsPerTrack)
            sector = 1;

        if (m_sectorCountRegister == 0)
        {
            hasDmaError = true;
            AddTraceLine("Read address aborted: sector count register is zero.");
            return true;
        }

        var dmaAddress = NormalizeDmaTransferAddress(m_dmaAddressRegister);
        if (!IsDmaRangeValid(dmaAddress, 6))
        {
            AddTraceLine($"Read address failed: DMA range 0x{dmaAddress:X6}+6 exceeds ST-RAM limit 0x{m_dmaAddressLimitExclusive:X6}.");
            return false;
        }

        DmaWrite8(NormalizeDmaTransferAddress(dmaAddress), m_fdcTrackRegister);
        DmaWrite8(NormalizeDmaTransferAddress(dmaAddress + 1), m_selectedSide);
        DmaWrite8(NormalizeDmaTransferAddress(dmaAddress + 2), sector);
        DmaWrite8(NormalizeDmaTransferAddress(dmaAddress + 3), 2); // 512-byte sector size code.
        DmaWrite8(NormalizeDmaTransferAddress(dmaAddress + 4), 0);
        DmaWrite8(NormalizeDmaTransferAddress(dmaAddress + 5), 0);

        m_dmaAddressRegister = NormalizeDmaAddress(dmaAddress + 6);
        m_sectorCountRegister = (byte)Math.Max(0, m_sectorCountRegister - 1);
        m_dmaBytesWritten += 6;
        AddTraceLine($"Read address ok: track={m_fdcTrackRegister} side={m_selectedSide} sector={sector} dma=0x{m_dmaAddressRegister:X6}.");
        return true;
    }

    private bool TryGetSelectedDriveImage(int driveIndex, out byte[] imageData)
    {
        imageData = null;
        if (!IsValidDriveIndex(driveIndex))
            return false;
        imageData = m_mountedImageByDrive[driveIndex];
        return imageData != null && imageData.Length > 0;
    }

    private static bool TryGetLinearSectorIndex(FloppyGeometry geometry, int track, int side, int sectorOneBased, out int linearSectorIndex)
    {
        linearSectorIndex = -1;
        if (track < 0 || track >= geometry.Tracks)
            return false;
        if (side < 0 || side >= geometry.Sides)
            return false;
        if (sectorOneBased < 1 || sectorOneBased > geometry.SectorsPerTrack)
            return false;

        linearSectorIndex = (track * geometry.Sides + side) * geometry.SectorsPerTrack + (sectorOneBased - 1);
        return true;
    }

    private static bool TryInferGeometry(byte[] imageData, out FloppyGeometry geometry)
    {
        geometry = default;
        if (imageData == null || imageData.Length < SectorSizeBytes || imageData.Length % SectorSizeBytes != 0)
            return false;
        if (TryInferGeometryFromBootSector(imageData, out geometry))
            return true;

        var totalSectors = imageData.Length / SectorSizeBytes;
        FloppyGeometry bestCandidate = default;
        var hasCandidate = false;
        var bestScore = int.MaxValue;
        foreach (var sides in new[] { 2, 1 })
        {
            for (var sectorsIndex = 0; sectorsIndex < PreferredSectorSizes.Length; sectorsIndex++)
            {
                var sectorsPerTrack = PreferredSectorSizes[sectorsIndex];
                var sectorsPerCylinder = sectorsPerTrack * sides;
                if (totalSectors % sectorsPerCylinder != 0)
                    continue;

                var tracks = totalSectors / sectorsPerCylinder;
                if (tracks is < 35 or > 90)
                    continue;

                var score = Math.Abs(tracks - 80) * 100 + Math.Abs(sides - 2) * 10 + sectorsIndex;
                if (score >= bestScore)
                    continue;
                bestScore = score;
                bestCandidate = new FloppyGeometry(tracks, sides, sectorsPerTrack);
                hasCandidate = true;
            }
        }

        if (!hasCandidate)
            return false;
        geometry = bestCandidate;
        return true;
    }

    private static bool TryInferGeometryFromBootSector(byte[] imageData, out FloppyGeometry geometry)
    {
        geometry = default;
        if (imageData.Length < SectorSizeBytes)
            return false;

        var bytesPerSector = (imageData[12] << 8) | imageData[11];
        if (bytesPerSector != SectorSizeBytes)
            return false;

        var sectorsPerTrack = (imageData[25] << 8) | imageData[24];
        var sides = (imageData[27] << 8) | imageData[26];
        if (sectorsPerTrack is < 1 or > 32 || sides is < 1 or > 2)
            return false;

        var totalSectors = (imageData[20] << 8) | imageData[19];
        if (totalSectors == 0)
            totalSectors = (imageData[35] << 24) | (imageData[34] << 16) | (imageData[33] << 8) | imageData[32];
        if (totalSectors == 0)
            totalSectors = imageData.Length / SectorSizeBytes;
        if (totalSectors * SectorSizeBytes > imageData.Length)
            return false;

        var sectorsPerCylinder = sectorsPerTrack * sides;
        if (totalSectors % sectorsPerCylinder != 0)
            return false;

        var tracks = totalSectors / sectorsPerCylinder;
        if (tracks is < 35 or > 90)
            return false;

        geometry = new FloppyGeometry(tracks, sides, sectorsPerTrack);
        return true;
    }

    private bool IsValidDriveIndex(int driveIndex) =>
        driveIndex >= 0 && driveIndex < m_drivePresent.Length;

    private static bool IsFloppyRegisterAccess(ushort controlRegister)
    {
        var isAcsiAccess = (controlRegister & DmaCsAcsi) != 0;
        var isFloppyDrqSelected = (controlRegister & DmaDrqFloppy) != 0;
        return !isAcsiAccess && isFloppyDrqSelected;
    }

    private ushort BuildDmaStatusWord()
    {
        UpdateSectorCountStatusNoLock();

        // Lower bits are DMA status flags; upper bits reflect last FF8604 data access.
        m_lastDmaStatusWord = (ushort)((m_recentDataRegisterValue & 0xFFF8) | (m_dmaStatusBits & 0x07));
        return m_lastDmaStatusWord;
    }

    private void ResetDmaTransferStateNoLock()
    {
        m_sectorCountRegister = 0;
        m_dmaStatusBits = 0x01;
        UpdateSectorCountStatusNoLock();
    }

    private void SetDmaErrorStatusNoLock(bool hasError)
    {
        if (hasError)
            m_dmaStatusBits = (byte)(m_dmaStatusBits & ~0x01);
        else
            m_dmaStatusBits = (byte)(m_dmaStatusBits | 0x01);
    }

    private void UpdateSectorCountStatusNoLock()
    {
        if (m_sectorCountRegister == 0)
            m_dmaStatusBits = (byte)(m_dmaStatusBits & ~0x02);
        else
            m_dmaStatusBits = (byte)(m_dmaStatusBits | 0x02);
    }

    private static uint NormalizeDmaAddress(uint address) =>
        address & DmaAddressMaskSt;

    private static uint NormalizeDmaTransferAddress(uint address) =>
        address & DmaTransferAddressMaskSt;

    private bool IsDmaRangeValid(uint startAddress, int lengthBytes)
    {
        var dmaAddress = NormalizeDmaTransferAddress(startAddress);
        for (var i = 0; i < lengthBytes; i++)
        {
            if (dmaAddress >= m_dmaAddressLimitExclusive)
                return false;
            dmaAddress = NormalizeDmaTransferAddress(dmaAddress + 1);
        }

        return true;
    }

    private void SetInterruptLine(bool activeLow)
    {
        if (m_interruptLineIsActiveLow == activeLow)
            return;

        m_interruptLineIsActiveLow = activeLow;
        AddTraceLine(activeLow ? "IRQ line asserted (active low)." : "IRQ line cleared.");
        InterruptLineChanged?.Invoke(activeLow);
    }

    [InterpolatedStringHandler]
    private ref struct FloppyTraceInterpolatedStringHandler
    {
        private DefaultInterpolatedStringHandler m_handler;

        public bool IsEnabled { get; }

        public FloppyTraceInterpolatedStringHandler(int literalLength, int formattedCount, FloppyDmaFdcDevice device, out bool shouldAppend)
        {
            shouldAppend = device.m_isTraceEnabled;
            IsEnabled = shouldAppend;
            m_handler = shouldAppend
                ? new DefaultInterpolatedStringHandler(literalLength, formattedCount)
                : default;
        }

        public void AppendLiteral(string value)
        {
            if (IsEnabled)
                m_handler.AppendLiteral(value);
        }

        public void AppendFormatted<T>(T value)
        {
            if (IsEnabled)
                m_handler.AppendFormatted(value);
        }

        public void AppendFormatted<T>(T value, string format)
        {
            if (IsEnabled)
                m_handler.AppendFormatted(value, format);
        }

        public string ToStringAndClear() =>
            IsEnabled
                ? m_handler.ToStringAndClear()
                : string.Empty;
    }

    private readonly record struct FloppyGeometry(int Tracks, int Sides, int SectorsPerTrack);
}
