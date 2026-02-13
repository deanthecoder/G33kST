// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using DTC.M68000;

namespace DTC.AtariST;

/// <summary>
/// Native Features (NatFeats) support for emulator-to-program communication.
/// This allows EmuTOS and other programs to output debug information without
/// requiring graphics or serial port emulation.
/// </summary>
public sealed class NatFeats
{
    private const ushort NatFeatsIdOpcode = 0x7300;
    private const ushort NatFeatsCallOpcode = 0x7301;
    private const int FeatureIdShift = 20;
    private const uint FeatureSubIdMask = (1u << FeatureIdShift) - 1;
    private const uint NfNameFeatureId = 1u << FeatureIdShift;
    private const uint NfVersionFeatureId = 2u << FeatureIdShift;
    private const uint NfStderrFeatureId = 3u << FeatureIdShift;
    private const uint NfDebuggerFeatureId = 4u << FeatureIdShift;
    private const uint NfVersion = 0x00010000;
    private const string EmulatorName = "G33kST";
    private static readonly IReadOnlyDictionary<string, uint> FeatureIdsByName = new Dictionary<string, uint>(StringComparer.Ordinal)
    {
        ["NF_NAME"] = NfNameFeatureId,
        ["NF_VERSION"] = NfVersionFeatureId,
        ["NF_STDERR"] = NfStderrFeatureId,
        ["NF_DEBUGGER"] = NfDebuggerFeatureId
    };

    public event EventHandler<NatFeatsMessageEventArgs> MessageReceived;

    public long TotalCalls { get; private set; }
    public long IdCalls { get; private set; }
    public long CallCalls { get; private set; }
    public long UnknownFeatureCalls { get; private set; }

    /// <summary>
    /// Attempts to handle a potential NatFeats opcode at the current PC.
    /// </summary>
    /// <param name="cpu">The CPU that encountered the illegal instruction.</param>
    /// <param name="opcode">The opcode at the current PC.</param>
    /// <returns>True if this was a NatFeats call and was handled; false otherwise.</returns>
    public bool TryHandle(Cpu cpu, ushort opcode)
    {
        switch (opcode)
        {
            case NatFeatsIdOpcode:
                TotalCalls++;
                IdCalls++;
                HandleNfId(cpu);
                AdvancePastNatFeatsOpcode(cpu);
                return true;

            case NatFeatsCallOpcode:
                TotalCalls++;
                CallCalls++;
                HandleNfCallFeature(cpu);
                AdvancePastNatFeatsOpcode(cpu);
                return true;

            default:
                return false;
        }
    }

    private static void AdvancePastNatFeatsOpcode(Cpu cpu)
    {
        cpu.Registers.ProgramCounter = unchecked((cpu.Registers.ProgramCounter + 2) & 0x00FF_FFFF);
        cpu.InternalWait(4);
    }

    private static uint ReadStackLong(Cpu cpu, uint offset) =>
        cpu.Read32(cpu.Registers.StackPointer + offset);

    private void HandleNfId(Cpu cpu)
    {
        var featureNameAddress = ReadStackLong(cpu, 4);
        var featureName = ReadString(cpu, featureNameAddress);
        var featureId = FeatureIdsByName.TryGetValue(featureName, out var resolvedFeatureId)
            ? resolvedFeatureId
            : 0u;
        cpu.Registers.SetDataRegister(0, featureId);
    }

    private void HandleNfCallFeature(Cpu cpu)
    {
        var featureToken = ReadStackLong(cpu, 4);
        var featureId = featureToken & ~FeatureSubIdMask;
        var featureSubId = featureToken & FeatureSubIdMask;
        var paramsAddress = cpu.Registers.StackPointer + 8;
        uint returnValue;

        switch (featureId)
        {
            case NfNameFeatureId:
                returnValue = HandleNfName(cpu, paramsAddress, featureSubId);
                break;

            case NfVersionFeatureId:
                returnValue = NfVersion;
                break;

            case NfStderrFeatureId:
                returnValue = HandleNfStderr(cpu, paramsAddress, featureSubId);
                break;

            case NfDebuggerFeatureId:
                returnValue = HandleNfDebugger(featureSubId);
                break;

            default:
                UnknownFeatureCalls++;
                returnValue = 0;
                break;
        }

        cpu.Registers.SetDataRegister(0, returnValue);
    }

    private static uint HandleNfName(Cpu cpu, uint paramsAddress, uint featureSubId)
    {
        _ = featureSubId;
        var destinationAddress = cpu.Read32(paramsAddress);
        var destinationLength = cpu.Read32(paramsAddress + 4);
        return WriteString(cpu, destinationAddress, destinationLength, EmulatorName);
    }

    private void EmitMessage(string message) =>
        MessageReceived?.Invoke(this, new NatFeatsMessageEventArgs(message));

    private uint HandleNfStderr(Cpu cpu, uint paramsAddress, uint featureSubId)
    {
        _ = featureSubId;
        var stringAddress = cpu.Read32(paramsAddress);
        var message = ReadString(cpu, stringAddress);
        EmitMessage(message);
        return (uint)message.Length;
    }

    private uint HandleNfDebugger(uint featureSubId)
    {
        _ = featureSubId;
        EmitMessage("[DEBUGGER BREAK]");
        return 0;
    }

    private static uint WriteString(Cpu cpu, uint address, uint maxLength, string value)
    {
        if (maxLength == 0)
            return 0;

        var maxChars = (int)Math.Min(maxLength - 1, (uint)value.Length);
        for (var i = 0; i < maxChars; i++)
            cpu.Write8(address + (uint)i, (byte)value[i]);

        cpu.Write8(address + (uint)maxChars, 0);
        return (uint)maxChars;
    }

    private string ReadString(Cpu cpu, uint address)
    {
        var chars = new List<char>();
        var currentAddress = address;

        while (true)
        {
            var b = cpu.Read8(currentAddress++);
            if (b == 0)
                break;

            chars.Add((char)b);

            // Safety limit to prevent runaway reads
            if (chars.Count > 4096)
                break;
        }

        return new string(chars.ToArray());
    }
}

/// <summary>
/// Event args for NatFeats messages.
/// </summary>
public sealed class NatFeatsMessageEventArgs : EventArgs
{
    public string Message { get; }

    public NatFeatsMessageEventArgs(string message)
    {
        Message = message;
    }
}
