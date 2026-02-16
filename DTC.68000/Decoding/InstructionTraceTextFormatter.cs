// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using DTC.M68000.Addressing;
using System.Text;
using System.Text.RegularExpressions;

namespace DTC.M68000.Decoding;

/// <summary>
/// Expands placeholder-heavy mnemonics into trace-friendly text without mutating CPU state.
/// This keeps debugging output readable while avoiding extra memory fetches for extension words.
/// </summary>
public static class InstructionTraceTextFormatter
{
    private static readonly Regex PlaceholderRegex =
        new("<(?<token>[^>]+)>", RegexOptions.Compiled);

    /// <summary>
    /// Returns instruction text suitable for tracing from the decoded instruction + opcode bits.
    /// </summary>
    private static string Format(ushort opcode, Instruction instruction)
    {
        if (instruction == null)
            return string.Empty;

        var text = ExpandEffectiveAddressPlaceholders(opcode, instruction.Mnemonic);
        return SimplifyPlaceholders(text);
    }

    /// <summary>
    /// Returns instruction text with lightweight runtime values (registers/immediates) expanded.
    /// This is intended for trace readability and does not mutate CPU state.
    /// </summary>
    public static string Format(ushort opcode, Instruction instruction, Cpu cpu, uint opcodeAddress)
    {
        var text = Format(opcode, instruction);
        if (cpu == null || text.Length == 0)
            return text;

        text = ExpandImmediatePlaceholders(opcode, opcodeAddress, text, cpu.Bus);
        return ExpandRegisterValues(opcode, text, cpu.Registers);
    }

    private static string ExpandEffectiveAddressPlaceholders(ushort opcode, string mnemonic)
    {
        if (!mnemonic.Contains("<ea>", StringComparison.Ordinal))
            return mnemonic;

        if (mnemonic.Contains("<ea>,<ea>", StringComparison.Ordinal))
        {
            var source = FormatEffectiveAddress(EffectiveAddressDecoder.DecodeLowSixBits(opcode));
            var destination = FormatEffectiveAddress(EffectiveAddressDecoder.DecodeMoveDestination(opcode));
            return mnemonic.Replace("<ea>,<ea>", $"{source},{destination}", StringComparison.Ordinal);
        }

        var eaText = FormatEffectiveAddress(EffectiveAddressDecoder.DecodeLowSixBits(opcode));
        return mnemonic.Replace("<ea>", eaText, StringComparison.Ordinal);
    }

    private static string FormatEffectiveAddress(EffectiveAddress ea) =>
        ea.Mode switch
        {
            EffectiveAddressMode.DataRegisterDirect => $"D{ea.Register}",
            EffectiveAddressMode.AddressRegisterDirect => $"A{ea.Register}",
            EffectiveAddressMode.AddressRegisterIndirect => $"(A{ea.Register})",
            EffectiveAddressMode.AddressRegisterIndirectPostIncrement => $"(A{ea.Register})+",
            EffectiveAddressMode.AddressRegisterIndirectPreDecrement => $"-(A{ea.Register})",
            EffectiveAddressMode.AddressRegisterIndirectDisplacement => $"(d16,A{ea.Register})",
            EffectiveAddressMode.AddressRegisterIndirectIndex => $"(d8,A{ea.Register},Xn)",
            EffectiveAddressMode.Other => FormatOtherMode(ea.Register),
            _ => "<ea?>"
        };

    private static string FormatOtherMode(byte register) =>
        register switch
        {
            0 => "(xxx).W",
            1 => "(xxx).L",
            2 => "(d16,PC)",
            3 => "(d8,PC,Xn)",
            4 => "#<imm>",
            _ => "<illegal-ea>"
        };

    private static string SimplifyPlaceholders(string instructionText) =>
        PlaceholderRegex.Replace(instructionText, "${token}");

    private static string ExpandImmediatePlaceholders(ushort opcode, uint opcodeAddress, string instructionText, Emulation.Bus bus)
    {
        var text = instructionText;

        if (text.Contains("#data", StringComparison.Ordinal))
        {
            var quickValue = (opcode >> 9) & 0x07;
            if (quickValue == 0)
                quickValue = 8;
            text = ReplaceFirst(text, "#data", $"#{quickValue:X1}");
        }

        if (text.Contains("#imm8", StringComparison.Ordinal))
        {
            var immediateByte = text.StartsWith("MOVEQ", StringComparison.OrdinalIgnoreCase)
                ? (byte)(opcode & 0x00FF)
                : (byte)(ReadWordAt(bus, opcodeAddress, 2) & 0x00FF);
            text = ReplaceFirst(text, "#imm8", $"#{immediateByte:X2}");
        }

        if (text.Contains("#imm", StringComparison.Ordinal))
        {
            var isLong = text.Contains(".L #imm", StringComparison.OrdinalIgnoreCase);
            if (isLong)
            {
                var immediateLong = ReadLongAt(bus, opcodeAddress, 2);
                text = ReplaceFirst(text, "#imm", $"#{immediateLong:X8}");
            }
            else
            {
                var immediateWord = ReadWordAt(bus, opcodeAddress, 2);
                text = ReplaceFirst(text, "#imm", $"#{immediateWord:X4}");
            }
        }

        return text;
    }

    private static string ExpandRegisterValues(ushort opcode, string instructionText, Registers registers)
    {
        var highRegisterIndex = (opcode >> 9) & 0x07;
        var lowRegisterIndex = opcode & 0x07;
        var usesLowDataRegister = instructionText.StartsWith("DB", StringComparison.OrdinalIgnoreCase);
        var usesLowAddressRegister =
            instructionText.StartsWith("LINK ", StringComparison.OrdinalIgnoreCase) ||
            instructionText.StartsWith("UNLK ", StringComparison.OrdinalIgnoreCase) ||
            instructionText.StartsWith("MOVEP.", StringComparison.OrdinalIgnoreCase) ||
            instructionText.StartsWith("MOVE An,USP", StringComparison.OrdinalIgnoreCase) ||
            instructionText.StartsWith("MOVE USP,An", StringComparison.OrdinalIgnoreCase);

        var genericDataIndex = usesLowDataRegister ? lowRegisterIndex : highRegisterIndex;
        var genericAddressIndex = usesLowAddressRegister ? lowRegisterIndex : highRegisterIndex;
        if (instructionText.IndexOf('D') < 0 && instructionText.IndexOf('A') < 0)
            return instructionText;

        var dataValueTokens = new string[8];
        var addressValueTokens = new string[8];
        StringBuilder builder = null;
        var copyStart = 0;

        for (var index = 0; index < instructionText.Length; index++)
        {
            if (!TryGetRegisterTokenReplacement(index, out var tokenLength, out var replacement))
                continue;

            builder ??= new StringBuilder(instructionText.Length + 32);
            builder.Append(instructionText, copyStart, index - copyStart);
            builder.Append(replacement);
            index += tokenLength - 1;
            copyStart = index + 1;
        }

        if (builder == null)
            return instructionText;

        if (copyStart < instructionText.Length)
            builder.Append(instructionText, copyStart, instructionText.Length - copyStart);
        return builder.ToString();

        bool TryGetRegisterTokenReplacement(int tokenStart, out int tokenLength, out string replacement)
        {
            tokenLength = 0;
            replacement = string.Empty;
            if (tokenStart + 1 >= instructionText.Length)
                return false;

            var prefix = instructionText[tokenStart];
            if (prefix is not ('D' or 'A'))
                return false;

            var suffix = instructionText[tokenStart + 1];
            var registerIndex = ResolveRegisterIndex(prefix, suffix);
            if (registerIndex < 0)
                return false;
            if (!IsWholeWordToken(instructionText, tokenStart, 2))
                return false;

            tokenLength = 2;
            replacement = prefix == 'D'
                ? GetDataValueToken(registerIndex)
                : GetAddressValueToken(registerIndex);
            return true;
        }

        int ResolveRegisterIndex(char prefix, char suffix)
        {
            if (suffix is >= '0' and <= '7')
                return suffix - '0';

            if (prefix == 'D')
            {
                if (suffix == 'n')
                    return genericDataIndex;
                if (suffix == 'm')
                    return lowRegisterIndex;
                return -1;
            }

            if (suffix == 'n')
                return genericAddressIndex;
            if (suffix == 'm')
                return lowRegisterIndex;
            return -1;
        }

        string GetDataValueToken(int registerIndex)
        {
            dataValueTokens[registerIndex] ??= $"D{registerIndex}={registers.GetDataRegister(registerIndex):X8}";
            return dataValueTokens[registerIndex];
        }

        string GetAddressValueToken(int registerIndex)
        {
            addressValueTokens[registerIndex] ??= $"A{registerIndex}={registers.GetAddressRegister(registerIndex):X8}";
            return addressValueTokens[registerIndex];
        }
    }

    private static bool IsWholeWordToken(string text, int tokenStart, int tokenLength)
    {
        var hasWordBefore = tokenStart > 0 && IsWordCharacter(text[tokenStart - 1]);
        if (hasWordBefore)
            return false;

        var tokenEnd = tokenStart + tokenLength;
        var hasWordAfter = tokenEnd < text.Length && IsWordCharacter(text[tokenEnd]);
        return !hasWordAfter;
    }

    private static bool IsWordCharacter(char value) =>
        char.IsLetterOrDigit(value) || value == '_';

    private static ushort ReadWordAt(Emulation.Bus bus, uint opcodeAddress, uint relativeOffset)
    {
        var address = EffectiveAddressMath.NormalizeAddress24(opcodeAddress + relativeOffset);
        return bus.Read16BigEndian(address);
    }

    private static uint ReadLongAt(Emulation.Bus bus, uint opcodeAddress, uint relativeOffset)
    {
        var address = EffectiveAddressMath.NormalizeAddress24(opcodeAddress + relativeOffset);
        return bus.Read32BigEndian(address);
    }

    private static string ReplaceFirst(string source, string oldValue, string newValue)
    {
        var index = source.IndexOf(oldValue, StringComparison.Ordinal);
        if (index < 0)
            return source;

        return source[..index] + newValue + source[(index + oldValue.Length)..];
    }
}
