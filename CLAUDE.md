# NetShield — agent instructions

On every session start: read `docs/SESSION.md` and follow it exactly. It defines your context files, the current work package, the scope gate, and the completion protocol.

`docs/STATUS.md` is the source of truth for what to build next. `docs/DECISIONS.md` is the source of truth for what has already been settled. Neither your memory nor this conversation overrides them.

## Never, in any package

- Write to a network device. No SNMP `set`, no configuration mode, no command outside the per-vendor read-only allowlist.
- Add an update or delete path for `audit_log`.
- Make an outbound internet call at runtime.
- Build anything from the Defer column of `docs/SPEC.md` §3.
- Commit a secret, a real device credential, a real config backup, or real capture data.
- Change `ARCHITECTURE.md`, `CONVENTIONS.md`, or `DESIGN.md` without being told to.

## Always

- State the scope and wait for "go" before writing code.
- A package with no tests is not done.
- End with the Package Completion Report, update `STATUS.md` and `DECISIONS.md`, then stop.

## Commands

```bash
aspire run                                        # start everything (from repo root)
dotnet build && dotnet test                       # backend
dotnet format --verify-no-changes                 # style gate
npm test --prefix src/NetShield.Web.Client        # frontend
uv run ruff check . && uv run mypy . && uv run pytest   # collector (from src/netshield-collector)
```
