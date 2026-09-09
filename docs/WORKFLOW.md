# WORKFLOW.md — building NetShield with Claude Code

## Part A — Environment (one time)

Your machine is already set up from the previous project. Verify rather than reinstall:

```bash
dotnet --version      # 10.x
aspire --version      # 13.x
node -v               # 24.x
python3 --version     # 3.13+
uv --version
docker run hello-world
claude --version
```

Anything missing, from the Ubuntu WSL terminal:

```bash
sudo apt update && sudo apt install -y git curl build-essential unzip
sudo apt install -y dotnet-sdk-10.0 \
  || (curl -fsSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --channel 10.0)
curl -fsSL https://aspire.dev/install.sh | bash
curl -fsSL https://raw.githubusercontent.com/nvm-sh/nvm/master/install.sh | bash
nvm install 24 && nvm alias default 24
curl -LsSf https://astral.sh/uv/install.sh | sh
curl -fsSL https://claude.ai/install.sh | bash
```

> The rule that saves the most pain, unchanged: **the repo lives in the WSL filesystem (`~/projects/…`), never under `/mnt/c/`.**

NetShield needs more headroom than the last project — flow and syslog ingest testing is memory-hungry. Bump `C:\Users\<you>\.wslconfig` to `memory=16GB` and `processors=8` if you have it, then `wsl --shutdown` once.

## Part B — Repo bootstrap (one time)

```bash
cd ~/projects
mkdir netshield && cd netshield
git init -b main
mkdir -p docs/design .claude
```

1. Copy the ten steering files into `docs/`: `SESSION.md`, `STATUS.md`, `WORK_PACKAGES.md`, `WORKFLOW.md`, `ARCHITECTURE.md`, `CONVENTIONS.md`, `DESIGN.md`, `DECISIONS.md`, `SPEC.md`, `ROADMAP.md`. Put `reference-dashboard.png` in `docs/design/`.
2. Copy `CLAUDE.md` to the **repo root** — not into `docs/`. Claude Code loads it automatically at every session start.
3. Copy `.claude/settings.json` into `.claude/`.
4. **Read `SPEC.md`, `ARCHITECTURE.md`, and `CONVENTIONS.md` once and edit them to your taste.** After this commit they are law and every session obeys them verbatim. `SPEC.md` §3 especially — that Defer list is the thing standing between you and a two-year build. Cut more from it if anything looks optimistic.
5. Verify and commit:
   ```bash
   for f in SESSION STATUS WORK_PACKAGES WORKFLOW ARCHITECTURE CONVENTIONS DESIGN DECISIONS SPEC ROADMAP; do
     [ -f "docs/$f.md" ] && echo "OK  docs/$f.md" || echo "MISSING  docs/$f.md"
   done
   [ -f CLAUDE.md ] && echo "OK  CLAUDE.md" || echo "MISSING  CLAUDE.md"
   [ -f docs/design/reference-dashboard.png ] && echo "OK  screenshot" || echo "MISSING  screenshot"

   git add -A
   git commit -m "chore: bootstrap steering docs"
   git remote add origin <your-remote-url>
   git push -u origin main
   ```
6. Open the project: `code .` from WSL. Its integrated terminal is your WSL shell — do everything there.

## Claude Code configuration

**`CLAUDE.md` at the root** is read at every session start and imports `docs/SESSION.md`, so the protocol is in context before you type anything.

**`.claude/settings.json`** pre-approves the commands you would otherwise approve fifty times a day and denies the ones that must never run unattended. Merging and pushing stay yours — the human is the merge gate in this workflow.

Two denials are specific to this project and worth understanding: `snmpset` and `.env`/key reads. The collector must never gain a write path to a device, and no session should ever read a credential file. If Claude asks to run something that would cross either line, the answer is no and the WP is wrong.

**Auto memory** accumulates per-repo notes. Harmless, but not the source of truth: `STATUS.md` and `DECISIONS.md` are. If a session cites something not in the steering files, correct it and check `/memory`.

## Part C — First package (WP-0.1), the shakedown run

```bash
cd ~/projects/netshield
git checkout -b feat/wp-0.1-skeleton
claude
```

1. Type: **"Read docs/SESSION.md and proceed."**
2. Claude loads the steering files, sees `STATUS.md` pointing at WP-0.1, and states its scope summary — the solution skeleton on `net10.0`. Read it. If it matches, say **"go"**.
3. It builds, ends with the Package Completion Report, updates `STATUS.md` and `DECISIONS.md`, and stops.
4. Verify: `dotnet build` succeeds, nothing targets anything but `net10.0`, no `PackageReference` carries a version, the layout matches `CONVENTIONS.md`, and its manual checklist passes.
5. Merge with the standard block (memorize this shape):
   ```bash
   git add -A
   git commit -m "feat: solution skeleton (WP-0.1)"
   git checkout main
   git merge --squash feat/wp-0.1-skeleton
   git commit -m "feat: solution skeleton (WP-0.1)"
   git push
   git branch -D feat/wp-0.1-skeleton
   ```
6. Back in Claude Code, run `/clear`. The system is now self-advancing.

## Part D — Every package after (the steady-state loop)

**1. Orient (1 min).** Open `docs/STATUS.md`. Note the Current WP, its branch name, and anything under *In flight*.

**2. Branch (1 min).**
```bash
git checkout main && git pull
git checkout -b feat/wp-X.Y-short-name
```

**3. Kick off (10 sec).** `/clear` for a fresh context, then: **"Read docs/SESSION.md and proceed."**

For anything large or `[SENSITIVE]`, enter **plan mode** first — `Shift+Tab` until the indicator shows Plan. Claude researches and proposes without touching files, which is exactly the shape of the scope gate.

**4. Gate the scope.** Claude states what it will and will not build, then waits. Read it properly. Matches your intent → **"go"**. Doesn't → correct it now, while no code exists. Bundling? Say "do WP-2.1 and WP-2.2 together" at kickoff — never with a `[SENSITIVE]` package.

**5. Build.** Let it work. It may want a dependency — approve only if the package justifies it. If it drifts: "note that in STATUS.md under In flight and stay in scope."

**6. Receive.** It ends with the Completion Report, updates `STATUS.md` and `DECISIONS.md`, and stops on its own.

**7. Verify (15–45 min — your actual job, never skipped).**
- Run the regression command it gave you. Red → step 8.
- Start the stack (see *Running the stack* — plain `aspire run` brings it up half-broken), then walk its manual checklist personally in your Windows browser. Click the things. Try the failure cases, not just the happy path.
- `git diff main` in VS Code — skim normally; **line by line if the package is `[SENSITIVE]`**.
- Read the `STATUS.md` and `DECISIONS.md` updates for accuracy. A wrong `STATUS.md` breaks the next session.

**Two extra checks that apply to this project specifically.** On any package touching the collector or credentials, grep the diff for a write path before you merge:
```bash
git diff main | grep -inE "snmpset|set_cmd|configure terminal|conf t|write mem|copy run" && echo "!!! REVIEW THIS"
git diff main | grep -inE "audit_log" | grep -iE "update|delete" && echo "!!! REVIEW THIS"
```
Both should print nothing. If either fires, do not merge — the package crossed a hard line.

**8. Fix loop (only if needed).** Same session: *"Manual check 3 failed — expected 409, got 500. Fix it."* Then re-verify. If the session has gone long and confused, don't fight it:
```bash
git checkout main && git branch -D feat/wp-X.Y-short-name
git checkout -b feat/wp-X.Y-short-name
# /clear, kick off again
```

**9. Merge (2 min).**
```bash
git add -A
git commit -m "feat(module): short description (WP-X.Y)"
git checkout main
git merge --squash feat/wp-X.Y-short-name
git commit -m "feat(module): short description (WP-X.Y)"
git push
git branch -D feat/wp-X.Y-short-name
```
`-D` is required after a squash merge — git can't tell the branch is merged. It's safe; the commit is on `main`.

**10. `/clear`.** The next package starts at step 1, and `STATUS.md` already points at it.

## Context management inside a session

- `/clear` between packages, always. A stale context is how a session starts "remembering" a decision that was never made.
- If a single package genuinely runs long, `/compact` preserves the thread. The root `CLAUDE.md` is re-read from disk after compaction, so the protocol survives.
- `/context` shows which memory files actually loaded. If `CLAUDE.md` isn't listed there, you launched from the wrong directory.

## Test data

You cannot build this against production kit, and you should not try. Before Phase 1, stand up a lab:

- **Containerlab or GNS3** with a few Arista cEOS or Nokia SR Linux nodes gives you real LLDP, real SNMP, and real config.
- **`snmpsim`** replays recorded walks — the cheapest way to get all seven vendors' fingerprints without seven devices.
- **`nflow-generator`** or `softflowd` for NetFlow/IPFIX, **`loggen`** for syslog volume.

Record fixtures from these once and commit them under `tests/fixtures/`. Never commit a capture or config from your real network — see `CONVENTIONS.md` §9.

## Running the stack

```bash
aspire run
```

That is the whole of it. There is no workaround and no environment variable to remember.

**There used to be**, and if you find the `DcpPublisher__CliPath` incantation in an old note or a
shell history, it is obsolete — delete it. What it worked around is described below, because the
underlying defect is still there and a future change could walk back into it.

**The defect.** DCP — Aspire's orchestrator — proxies every resource endpoint by default, and the
build shipped with Aspire 13.5.3 (DCP `0.25.13`) intermittently never wires an upstream to one of
those proxies: it accepts the TCP connection and never dials the application, which is answering
`200 Healthy` on its own port throughout. It picks a different endpoint to break on each run.
**13.4.6's DCP `0.24.3` does it too** — an earlier note here claimed otherwise and was wrong; it
was written after a run where the pin happened to come up clean, and a later session watched
13.4.6 break `web-host`'s https endpoint while the http one worked. Pinning the DCP version was
never a fix, only a different roll of the same dice. There is no newer DCP to move to: 13.5.3 is
the newest `Aspire.Hosting.Orchestration.linux-x64` on NuGet.

**Why it was so damaging.** The AppHost depended on the proxy twice, independently.
`WithHttpHealthCheck` probes the endpoint URL, so a dead proxy left `web-host` permanently
unhealthy and the two resources that `WaitFor` it — `collector` and `web-client` — never started
at all. And `webHost.GetEndpoint("http")` handed that same URL to both as `NETSHIELD_API_URL`, so
even on a run where they did start, the collector leased nothing and the dev server answered
`/api` with a hang. The visible symptoms were a blank page on a dark background, a device whose
fingerprint and state never changed, and walks that sat `Pending` for hours.

**The fix, in `AppHost.cs`.** `web-host` and `web-client` declare their endpoints with
`isProxied: false`, so the endpoint URL *is* the address the process is listening on and DCP's
proxy is out of both the health-check path and the data path. `web-host` also takes
`launchProfileName: null`, which is the half that was missing when this was tried before:
`Properties/launchSettings.json` pins 7235 and 5295, Aspire takes those as the *endpoint* ports
and gives the application random target ports behind them, so turning the proxy off while the
profile still pinned 5295 handed the API a port DCP had already bound and it died with "address
already in use". That read as the approach failing when it was the launch profile that had to go.

**Two things to keep in mind if you touch this.** The launch profile also carried
`ASPNETCORE_ENVIRONMENT`, so the resource sets it explicitly from the AppHost's own environment —
without it the API comes up in Production, the health endpoints are never mapped, and
`/health/ready` falls through to the SPA fallback and answers `200` with `index.html`, which is a
health check passing for the wrong reason. And the ports are Aspire-allocated now rather than the
5295/7235 in `launchSettings.json`; read them off the dashboard.

`db-migrator` still uses the launch profile. It binds no socket and exits, so nothing waits on an
endpoint of its own and there is nothing for a proxy to break.

## Phase gates

After a phase's final package, run the 🏁 gate from `ROADMAP.md`, plus the dependency-health pass: `aspire update`, review Dependabot, confirm nothing in the version table has crossed EOL. `aspire update` is also the moment to retest with the endpoints proxied again — a new Aspire brings a new DCP, and `isProxied: false` should be dropped the release it stops being needed. Then:
```bash
git tag v0.N-phaseN && git push --tags
```

## Part E — Quick reference

| Situation | Action |
|---|---|
| Start any session | `/clear` → "Read docs/SESSION.md and proceed." |
| Large or `[SENSITIVE]` package | `Shift+Tab` to Plan mode first |
| Claude drifts out of scope | "Note that in STATUS.md under In flight and stay in scope." |
| Claude cites a decision you don't recognize | Check `DECISIONS.md`; if absent, correct it and check `/memory` |
| Session confused mid-package | Delete the branch, re-branch, `/clear`, restart |
| Package runs long | `/compact`, not `/clear` |
| A manual check fails | Same session: state the expected and actual, ask for the fix |
| Diff contains a device write path | Do not merge. The package crossed a hard line. |
| Phase finished | Run the ROADMAP gate, `aspire update`, tag |
| `web-host` unhealthy, collector and SPA never start | Should not happen since the endpoints were unproxied — see *Running the stack*. If it does, check nothing has re-proxied them. |
