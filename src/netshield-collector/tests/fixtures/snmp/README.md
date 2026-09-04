# SNMP walk fixtures

Seven recorded-shape walks, one per platform SPEC.md §4 names plus one the platform is meant not
to recognise. Every vendor's fingerprint resolution is tested against these and against nothing
else — CONVENTIONS.md §7: protocol interactions are tested against recorded fixtures, never a
live device.

## These are synthetic

**Nothing here was captured from a device.** Every file is hand-authored from the documented
`sysObjectID` arc and `sysDescr` format for its platform, and every serial number, MAC address,
host name and contact in them is invented. `WORKFLOW.md` § Test data describes the lab —
Containerlab plus an `snmpsim` corpus — that would produce real recordings; it is not stood up,
and WP-1.5 was deliberately not blocked on it, because what these fixtures have to prove is that
the resolver reads a `sysObjectID` and a `sysDescr` correctly, and that is fully determined by
their shape.

Two consequences worth knowing when a real corpus arrives:

- **The format is the contract, not the content.** A recorder writes the same JSON and the tests
  keep working. Replacing a file here with a real recording of the same platform should change
  nothing but realism.
- **A leaf may be wrong where the arc is right.** Where a specific product's `sysObjectID` leaf
  was not certain it is invented and the file's `note` says so. Resolution matches on the
  enterprise arc, so an invented leaf under the right arc exercises exactly the path a real one
  would.

Committing real captures from a production network is forbidden (CONVENTIONS.md §9). If real
fixtures are ever recorded they come from the lab, never from the estate.

## Format

```json
{
  "description": "what this device is",
  "synthetic": true,
  "note": "where it came from",
  "values": { "<dotted OID>": "<decoded value>" }
}
```

`values` is flat: one entry per object instance, dotted numeric OID to the string
`collector.snmp.session.decode` would have produced for it. That is why an `ifPhysAddress` is
written as `00:1A:2B:3C:4D:01` rather than as raw bytes — the fixture records what a walk
decoded to, and `FixtureSession` replays it without decoding again.

Order in the file does not matter. `FixtureSession` sorts numerically, the way an agent must
walk.
