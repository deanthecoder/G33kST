# agents.md — Guidance for AI-assisted Development
_An overview of this repository for AI-assisted development._

## Summary for Agents
This repository is **G33kST**, a C#/.NET emulator project initially targeting the **Atari 520 STFM**.

Primary goals:
- Build a **good, pragmatic, reusable Motorola 68000 core** suitable for reuse in other retro projects.
- Prioritize **correct behaviour for common software** over perfect cycle accuracy (at least initially).
- Keep the codebase **approachable and testable**.

Non-goals (initially):
- Full cycle-exact 68000 bus timing / prefetch edge cases.
- Demo-scene-perfect raster tricks from day one.

## Agent Instructions
- Prefer **small, incremental changes** that keep the emulator booting/running.
- When uncertain, implement behaviour that is:
  1) Documented in primary references, and
  2) Verified via unit tests or small ROM/program tests.
- Avoid speculative “accuracy” work unless it is required for a failing test or a concrete software issue.
- Keep context usage lean; do not ingest large files wholesale.
- If adding a new subsystem, start with a minimal API and wire it end-to-end before expanding.

## Target Hardware
Initial target: **Atari 520 STFM**
- Vanilla **Motorola 68000** (no Atari-specific CPU tweaks).
- ST video (bitplanes / shifter), YM2149 PSG audio.
- Internal floppy assumed (STF/STFM class machine). RF modulator (the “M”) is irrelevant for emulation.

## 68000 CPU Guidance
### Scope
- Implement the 68000 **architecturally correctly** (instructions, addressing modes, exceptions, privilege) but allow pragmatic simplifications where they do not break real software.

### Must-haves early
- Supervisor vs user mode (S bit) and USP/SSP switching.
- Exceptions and vectoring for: reset, address error, bus error (at least stubs), illegal instruction, privilege violation, trap, and interrupt acknowledge.
- Correct status flags for implemented instructions.

### Can be deferred initially
- Cycle-exact timings.
- Exact prefetch behaviour.
- Rare/obscure instructions if not needed to boot TOS / run target software.

### Trace bit
- Implement the trace (T) bit as standard 68000 behaviour (trace exception after instruction completes) so debuggers/monitors don’t misbehave.

## Coding Conventions
Follow the repository’s established style. When in doubt, align with these preferences:
- C# style:
  - Use `var` wherever possible.
  - Use `string.Empty` (not `""`).
  - Fields use an `m_` prefix.
  - If a braced block has one line, drop the braces (except `if/else`: if one block is multi-line, both get braces).
  - Prefer `FileInfo`/`DirectoryInfo` over raw path strings when practical.
  - No underscores in method names.
- Comments:
  - All source files include the standard header comment.
  - `//` comments end with a full stop if they’re sentences.
- Classes and interfaces (except unit tests) include a brief XML summary comment.
- Commit messages are prefixed with `Fix:`, `Feature:`, or `Other:`. The remainder sentence starts with capital letter.
- Keep public APIs small and intention-revealing.

## Tests
- Prefer NUnit-based unit tests if tests exist in this repo.
- Match existing assertion style (e.g., `Assert.That(...)`, `Is.*`, `Does.*`) used in the repository.
- When implementing an instruction or addressing mode, add at least one focused test for:
  - Operand decode,
  - Result,
  - Flags.

## Performance
- Performance matters, but correctness + clarity first.
- Avoid premature micro-optimisation. Do optimise hotspots once profiling indicates them.

## Suggested Work Pattern
1. Add/adjust a small test or a minimal repro (tiny 68k program) for the behaviour you’re changing.
2. Implement the smallest change that makes it pass.
3. Keep changes local; don’t refactor unrelated code.

## References
Use these references when implementing/validating 68000 decoding and semantics:
- NJIT 68000 notes (PDF): https://web.njit.edu/~rosensta/classes/architecture/252software/code.pdf
- Atari-Wiki 68000 assembly guide: https://www.atari-wiki.com/index.php?title=The_Guide_to_68000_Assembly_Language
- M68k opcode maps (PDF): http://goldencrystal.free.fr/M68kOpcodes-v2.3.pdf
- 68000 single-step test suite: https://github.com/SingleStepTests/m68000
- 680x0 single-step test suite: https://github.com/SingleStepTests/680x0

> **Agent note:** Prefer primary documentation and tests over forum posts. If two sources disagree, add a test that captures expected behaviour and document the decision in-code.
