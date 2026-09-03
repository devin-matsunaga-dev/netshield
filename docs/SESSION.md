# SESSION.md — read this first, every session

You are building **NetShield**, a unified network and security operations platform. This file is your operating protocol. Follow it exactly.

## 1. Load context, in this order

1. `docs/STATUS.md` — where the build is right now and which work package is current.
2. `docs/WORK_PACKAGES.md` — the package protocol at the top, then the entry for the current WP.
3. `docs/ARCHITECTURE.md` — binding system structure.
4. `docs/CONVENTIONS.md` — binding code standards.
5. `docs/SPEC.md` — scope, and the Defer list you must not cross.
6. `docs/DECISIONS.md` — what has already been settled. Do not relitigate these.
7. `docs/DESIGN.md` and `docs/design/reference-dashboard.png` — **only if the package touches UI**, and then both, not one.

If `STATUS.md` and this conversation disagree about what is current, `STATUS.md` wins. If your own memory of a prior session disagrees with `DECISIONS.md`, `DECISIONS.md` wins.

## 2. State the scope, then stop

Before writing any code, output:

- **Building:** what this package produces.
- **Not building:** the adjacent things you will leave alone, especially anything nearby on the Defer list.
- **Files:** what you will create and what you will change.
- **Dependencies:** any new package, with a one-line justification.
- **Questions:** anything ambiguous in the WP entry.

Then **wait for "go"**. Do not write code, do not create files, do not run a migration before you have it.

## 3. Build

- Only what the package specifies. Something worth doing but out of scope goes into `STATUS.md` under *In flight / noticed* — not into the diff.
- Follow `CONVENTIONS.md` verbatim. A package that works but violates conventions is not done.
- Tests are part of the package, not a follow-up. **A package with no tests is not done.**
- Run the gates before you report: `dotnet build && dotnet test`, `dotnet format --verify-no-changes`, plus the frontend or collector gates if you touched them.

## 4. Hard stops — ask the human, do not proceed

Stop and ask if the package appears to require any of these:

- Writing to a network device in any form, or an SNMP `set`.
- Anything in the Defer column of `docs/SPEC.md` §3.
- Changing auth, RBAC, the credential encryption envelope, or the collector job contract, unless this WP explicitly says to.
- An update or delete path for `audit_log`. This is never permitted, in any package, for any reason.
- Editing a migration that is already on `main`.
- An outbound internet call at runtime.
- A new external service, database, or message broker.
- Changing anything in `ARCHITECTURE.md`, `CONVENTIONS.md`, or `DESIGN.md`.

## 5. Package Completion Report

End every session with exactly this:

```
## Package Completion Report — WP-X.Y

**Built:** …
**Files added:** …
**Files changed:** …
**Decisions made:** … (or "none")
**Dependencies added:** … with justification (or "none")
**Deferred / noticed:** … (or "none")

**Regression command:**
    <the exact command the human should run>

**Manual verification checklist:**
1. …
2. …
```

The manual checklist must be things a human clicks or observes in a browser or terminal — specific, ordered, and including at least one failure case, not just the happy path.

## 6. Then update and stop

1. `docs/STATUS.md` — mark this WP done, set Current WP to the next one with its branch name, record anything noticed.
2. `docs/DECISIONS.md` — append one-liners: "chose X over Y because Z (WP-X.Y, YYYY-MM-DD)". If none, write nothing.
3. **Stop.** Do not start the next package. Do not merge. Do not push. The human verifies, merges, and opens the next session.

## 7. Standing rules

- One package per session. Never carry a session across a phase boundary.
- Never commit a secret, a real device credential, a real config backup, or real capture data.
- Never invent a visual direction — the reference screenshot is law.
- If the steering files are wrong or contradictory, say so and fix them first. That is a legitimate package of work and it takes priority over building.
