[![Twitter URL](https://img.shields.io/twitter/url/https/twitter.com/deanthecoder.svg?style=social&label=Follow%20%40deanthecoder)](https://twitter.com/deanthecoder)

<p align="center">xs
  <img src="img/g33kst.png" alt="G33kST Logo">
</p>

# G33kST
A cross-platform Avalonia-based Atari ST emulator.

## Purpose
After building a [ZX Spectrum emulator](https://github.com/deanthecoder/ZXSpeculator), a [Game Boy emulator](https://github.com/deanthecoder/G33kBoy), and a [Sega Master System emulator](https://github.com/deanthecoder/MasterG33k), I wanted to tackle the Atari ST.
G33kST is my way of learning its hardware properly, starting from a pragmatic and reusable Motorola 68000 core.

## Status
- Early bring-up.
- Focus is on CPU correctness, basic memory mapping, and a minimal boot path.

## Pragmatic compatibility notes
- Goal is to run Atari ST software first, then improve edge-case accuracy where needed.
- Some exception-heavy CPU behavior (for example, full 68000 address-error frame handling) may be deferred unless real software depends on it.
- Current single-step tests temporarily treat expected address-error cases as skipped until full exception flow is implemented.

## Deferred behavior (post phase 1)
- `RESET` is currently privilege-gated; external bus/device reset side effects are still to be modeled.
- `STOP` currently follows the single-step test harness expectation; full halted-state + interrupt wake behavior is still to be modeled.
- Full 68000 address-error/bus-error stack frame behavior is still to be modeled.

## CPU roadmap (toward full 68000 support)
- [x] Add an opcode-completeness audit so legal opcodes are implemented and illegal ones route to the exception path.
- [x] Implement broad instruction coverage and status-flag behavior validated by single-step suites.
- [x] Add baseline cycle accounting (`Cpu.CyclesSinceCpuStart`) with pragmatic per-instruction timing.
- [x] Boot EmuTOS to desktop as a software-level smoke test.
- [ ] Complete remaining exception/interrupt edge behavior (detailed bus/address error frames, precise trace flow, and full interrupt edge cases).
- [ ] Complete execution-state edge behavior for `STOP`/halt/wake and full reset side effects.
- [ ] Tighten timing accuracy (EA + exception timing, and only then case-by-case cycle tuning where compatibility needs it).
- [ ] Improve prefetch fidelity where software depends on queue refill/flush details.
- [ ] Expand platform bring-up for ST peripherals (floppy path, DMA/FDC behavior, GEM-visible device behavior).

Current note for exception/interrupt work:
- Basic interrupt request + acknowledge plumbing is in place (`RequestInterrupt`, autovector/spurious/explicit vector result support, and interrupt mask update on entry).
- Trace exception entry support exists behind `EnableTraceExceptions` so single-step compatibility can remain stable while the behavior is being integrated.

## Highlights
- **68000 core** - A clean, reusable Motorola 68000 implementation (work in progress).
- **Shared core utilities** - `DTC.Core` provides commands, extensions, converters, and Avalonia helpers shared across projects.
- **Shared emulator host** - `DTC.Emulation` is the shared host repo used to write each emulator in this family.
- **Avalonia UI shell** - A cross-platform desktop shell will host the emulator.
- **Unit tests** - NUnit-based tests will validate CPU behavior and core subsystems.

## Input shortcuts
- `Ctrl+J` / `⌘+J` toggles joystick input mode (off by default, not persisted).
- With joystick input enabled:
  - Arrow keys map to joystick directions.
  - `Z` maps to joystick fire.
  - `A` maps to joystick auto-fire.

## Test data
The single-step 68000 test data comes from the excellent `m68000` repo by SingleStepTests, used under its license:
https://github.com/SingleStepTests/m68000

The MC68000 opcode test suite by **Ted Fried / MicroCore Labs** is included under:
`external/MC68000_Test_Code`
Source: https://github.com/MicroCoreLabs/Projects/tree/master/MCL68/MC68000_Test_Code

## EmuTOS ROM usage
For integration/boot tests this repository currently uses an EmuTOS ROM image:
- `DTC.AtariST/TOS/etos192us.img`

References:
- EmuTOS project: https://github.com/emutos/emutos
- EmuTOS manual (license section): https://emutos.github.io/manual/#license

Licensing note:
- EmuTOS is GPL-licensed (GNU GPL v2, per EmuTOS documentation).
- G33kST source code remains MIT-licensed.
- The bundled EmuTOS ROM is third-party software under its own license terms and attribution requirements.
- If/when redistributing binaries or source bundles, keep EmuTOS attribution and GPL materials alongside that ROM artifact.

## Useful links
- [Motorola 68000 Programmer's Reference Manual](https://www.nxp.com/docs/en/reference-manual/M68000PRM.pdf)
- [Atari ST Wiki (Hardware Overview)](https://en.wikipedia.org/wiki/Atari_ST)
- [M68k opcode maps (PDF)](http://goldencrystal.free.fr/M68kOpcodes-v2.3.pdf)

## License
Licensed under the MIT License. See [LICENSE](LICENSE) for details.
