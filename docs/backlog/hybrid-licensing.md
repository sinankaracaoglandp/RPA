# Hybrid Licensing Backlog

The first release is **offline**: an installation-bound, vendor-signed license verified entirely by
the customer's own WebAPI. See `docs/operations/offline-licensing.md`.

Online validation and revocation are **explicitly deferred**, not forgotten. The offline payload
already carries stable `licenseId`, `installationId`, `revision` and `schemaVersion` fields so a
future hybrid service can adopt existing licenses **without changing their identity**.

The items below are **mandatory backlog work for the hybrid release** and must remain visible in
planning documentation (spec: "Deferred Hybrid Licensing Roadmap").

---

## Mandatory hybrid items

1. **Vendor-hosted license validation service** — central validation of a license/installation pair
   against vendor records, instead of trusting only the local signature check.
2. **Signed revocation list and emergency revocation** — a vendor-signed revocation document the
   runtime honours, plus an emergency path. *Offline gap this closes:* today a leaked or
   commercially-terminated license cannot be withdrawn remotely.
3. **Duplicate license/installation-use detection** — detect the same license or installation identity
   in use in more than one place. *Offline gap:* a cloned VM (with a restorable key store) is
   invisible to the vendor.
4. **Periodically renewed signed online lease** — a short-lived, vendor-signed lease refreshed over
   the network, replacing "signature was valid at import time" with "still authorized now".
5. **Configurable offline grace period after the online lease expires** — how long an installation may
   keep running once it can no longer reach the validation service (per-customer policy).
6. **Installation deactivation and transfer protocol** — a supported server migration that deactivates
   the source installation instead of relying on the commercial agreement (see operations §4).
7. **Audit trail for vendor-side issuance, replacement, and revocation** — durable, queryable vendor
   records of who issued/replaced/revoked what and when.
8. **TPM-backed non-exportable installation-key support as a hardened deployment option** — today the
   installation private key is DPAPI machine scope; TPM makes it non-exportable.
9. **Privacy-preserving telemetry limited to license and installation identifiers** — enough to run
   validation and duplicate detection, and nothing more; no customer workload data.
10. **Migration from offline licenses without re-enrolling existing agents** — today a new server means
    a new installation ID, a newly signed license, and re-activation of every agent.

---

## Known offline-release gaps that feed this backlog

Carried over from the first release (recorded honestly rather than silently dropped):

- `POST /api/agent-auth/refresh-lease` from the spec's API surface is **not implemented**. The agent
  heartbeat is the lease feed instead. A dedicated endpoint becomes meaningful with item 4, where the
  lease is server-signed rather than locally computed.
- **Resume-after-reconnect** from the suspended node is not implemented: an expired lease suspends the
  run before the next node and reports it, but the job is not automatically continued when
  connectivity returns.
- The **agent does not fetch a rotated credential**; an operator carries it manually (operations §6).
- The offline release cannot **remotely disable an old installation** after a migration (items 2, 3, 6).
