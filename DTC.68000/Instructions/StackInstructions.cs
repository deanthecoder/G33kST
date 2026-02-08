// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

namespace DTC.M68000.Instructions;

/// <summary>
/// Stack-frame instruction decode and execution helpers.
/// LINK creates a function stack frame, and UNLK tears it down.
/// </summary>
public static class StackInstructions
{
    private static readonly Instruction InstrLink = new("LINK An,#<disp16>", ExecuteLink);
    private static readonly Instruction InstrUnlink = new("UNLK An", ExecuteUnlink);

    /// <summary>
    /// Decodes stack/frame opcodes handled by this module.
    /// </summary>
    public static Instruction TryDecode(ushort opcode)
    {
        // 0100 1110 0101 0 nnn = LINK An,#<disp16>.
        if ((opcode & 0xFFF8) == 0x4E50)
            return InstrLink;

        // 0100 1110 0101 1 nnn = UNLK An.
        return (opcode & 0xFFF8) == 0x4E58 ? InstrUnlink : null;
    }

    /// <summary>
    /// Executes <c>LINK An,#&lt;disp16&gt;</c> to create a stack frame.
    /// Pushes the old An, copies SP into An as the frame pointer, then applies the signed displacement to SP.
    /// </summary>
    private static void ExecuteLink(Cpu cpu, ushort opcode)
    {
        var registerIndex = opcode & 0x07;
        var oldAddressRegisterValue = cpu.Registers.GetAddressRegister(registerIndex);

        PushLongToStack(cpu, oldAddressRegisterValue);
        var frameBase = cpu.Registers.StackPointer;
        cpu.Registers.SetAddressRegister(registerIndex, frameBase);

        var displacement = (short)cpu.FetchPcWord();
        cpu.Registers.StackPointer = unchecked((uint)(cpu.Registers.StackPointer + displacement));
    }

    /// <summary>
    /// Executes <c>UNLK An</c> to destroy a stack frame.
    /// Restores SP from An, pops the saved frame pointer from the stack into An, then advances SP.
    /// </summary>
    private static void ExecuteUnlink(Cpu cpu, ushort opcode)
    {
        var registerIndex = opcode & 0x07;
        cpu.Registers.StackPointer = cpu.Registers.GetAddressRegister(registerIndex);
        var restoredAddressRegisterValue = cpu.Read32(cpu.Registers.StackPointer);
        cpu.Registers.StackPointer += 4;
        cpu.Registers.SetAddressRegister(registerIndex, restoredAddressRegisterValue);
    }

    /// <summary>
    /// Pushes a long value to the active stack using pre-decrement semantics.
    /// </summary>
    private static void PushLongToStack(Cpu cpu, uint value)
    {
        var newStackPointer = cpu.Registers.StackPointer - 4;
        if ((newStackPointer & 1) != 0)
            throw new AddressErrorException(newStackPointer, ".l");

        cpu.Registers.StackPointer = newStackPointer;
        cpu.Write32(newStackPointer, value);
    }
}
