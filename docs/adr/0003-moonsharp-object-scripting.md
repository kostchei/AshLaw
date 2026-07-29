# ADR 0003: Use MoonSharp for object scripting

- **Status:** Accepted
- **Date:** 2026-07-29

## Context

Content needs hot-reloadable object scripts, cooperative waits, and a small sandboxed
engine API. The script host must remain pure C# so headless simulation tests do not
acquire a native runtime dependency.

## Decision

Use MoonSharp Lua for object scripts. Lua coroutines map script waits to the simulation
process kernel. Scripts receive only a bounded intrinsic table; `io`, `os`, `require`,
and dynamic code loading are unavailable. An instruction budget terminates runaway
scripts.

MoonSharp coroutine stacks are not serialized. A persistent process is instead recorded
as:

```text
{ script_id, entrypoint, args, wait_condition, resume_label, local_state }
```

Scripts may yield only at declared safe points. The content validator treats any other
yield as a build error and resumes saved processes from their declared label.

## Consequences

- Scripts and coroutine-based waits remain portable, sandboxable, and testable in C#.
- Save compatibility depends on stable script identifiers, resume labels, and explicit
  local state.
- Content authors must design resumable processes rather than relying on implicit Lua
  stack state.
- MoonSharp becomes an `Ash.Script` dependency and must not leak into `Ash.Sim` APIs.

