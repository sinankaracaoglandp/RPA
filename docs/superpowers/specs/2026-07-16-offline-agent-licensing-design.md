# Offline Agent Licensing Design

## Purpose

RPA Platform must enforce a vendor-issued limit on the number of activated agents while supporting customers whose Orchestrator installations cannot access the internet. The first release provides an offline, cryptographically signed license bound to one Orchestrator installation. It also replaces anonymous agent SignalR connections with short-lived agent JWTs.

## Scope

This design covers the complete first-release flow:

- vendor-side offline license generation;
- customer installation request export and license import;
- installation-bound signature verification;
- activated-agent seat enforcement;
- one-time agent activation;
- agent credential storage and rotation;
- short-lived JWT issuance and renewal;
- authorization separation between Studio users and agents;
- 15-minute controlled offline execution;
- Studio license and agent-management screens.

Online license validation and revocation are explicitly deferred, but the contracts preserve identifiers and lease concepts required by the future hybrid model.

## Security Boundaries

Three cryptographic identities are distinct:

1. **Vendor signing identity:** The vendor private key exists only in the separate license-generator environment. The product contains only the corresponding public key.
2. **Orchestrator installation identity:** The Orchestrator generates an installation key pair on first startup. Its private key is non-exportable when TPM support is available and otherwise protected with Windows DPAPI machine scope.
3. **Agent identity:** Each activated agent receives a unique random credential. Only a salted, slow hash is stored server-side; the agent protects the plaintext credential with Windows DPAPI machine scope.

Secrets, activation codes, credentials, private keys, and complete tokens must never be logged.

## Offline License Lifecycle

### Installation request

The Studio license screen requests an installation request document from WebAPI. It contains:

- schema version;
- installation ID;
- installation public key;
- normalized installation public-key fingerprint;
- product identifier;
- customer-entered reference information;
- creation timestamp.

The request contains no private key. A customer exports it and sends it to the vendor.

### Vendor license generation

A separate `RPA.LicenseGenerator` project imports the installation request. An authorized vendor operator enters:

- license ID;
- customer ID and display name;
- edition;
- maximum activated agents;
- issue and expiry dates;
- enabled feature codes.

The generator produces a canonical license payload bound to the installation ID and installation public-key fingerprint, then signs that payload with the vendor private key. The generator never becomes a dependency of the shipped runtime.

### Customer import and validation

Studio uploads the signed license document to WebAPI. WebAPI, not Studio, performs all enforcement:

- schema and canonical payload validation;
- vendor signature verification;
- product identifier validation;
- installation ID and public-key fingerprint match;
- validity-period validation;
- monotonic replacement rules that prevent accidental downgrade to an older license revision.

Copying customer A's license to customer B fails with `LICENSE_INSTALLATION_MISMATCH`. Editing the seat count invalidates the vendor signature.

### Server migration

A legitimate server migration requires a new installation request and a newly signed license. The offline release cannot remotely disable the old installation. Vendor records and the commercial agreement track replacement licenses. The future hybrid model adds server-side revocation and duplicate-use detection.

## Activated Agent Seat Model

An agent identity has one of these states:

- `PendingActivation`: no seat consumed;
- `Activated`: one seat consumed;
- `Disabled`: one seat remains consumed, but authentication and new work are blocked;
- `Deactivated`: no seat consumed and all credentials are revoked.

Used seats equal the number of non-deleted `Activated` or `Disabled` agent identities for the licensed installation. Reconnecting the same identity does not consume another seat. Reactivating a deactivated identity requires a new activation and available capacity.

Activation performs the license validity check, seat count, identity transition, activation-code consumption, and credential issuance in one database transaction. The license/installation row is locked so concurrent activation requests cannot both claim the final seat.

## Agent Enrollment and Authentication

### Studio administrator flow

An authorized administrator creates a pending agent and requests a one-time activation code. The code:

- is generated with a cryptographically secure random source;
- is displayed only once;
- has a short configurable lifetime;
- is stored only as a salted slow hash;
- becomes unusable immediately after successful activation.

### Agent activation

The installed agent submits the activation code, installation identifier, machine identity, and agent metadata to WebAPI. WebAPI validates the license and capacity, binds the agent identity, and returns a unique long-lived agent credential once. The agent stores it through an `IAgentCredentialStore`; Windows uses DPAPI machine scope.

### Short-lived token

The agent exchanges its credential at `POST /api/agent-auth/token`. A successful response contains a roughly 10-minute access JWT with:

- `sub=agent:{agentId}`;
- `agent_id`;
- `installation_id`;
- `client_type=agent`;
- `token_use=access`;
- agent authorization roles/policies.

The agent token provider caches the token, refreshes before expiry, and serializes concurrent refreshes. Both `/hubs/robot` and `/hubs/studio` use the provider through SignalR's `AccessTokenProvider`.

Credential rotation invalidates the previous credential immediately. Disable and deactivate also prevent new token issuance.

## Authorization

Authorization is policy-based:

- `LicenseAdministrator`: import licenses and manage agent identities;
- `StudioSpyUser`: start and stop UI Spy sessions;
- `AgentClient`: receive assigned commands and report elements, cancellation, heartbeat, logs, and results for its own identity.

StudioHub methods are divided by policy. Studio users may start and stop spy sessions. Agent clients may report detected elements and cancellation. A client cannot invoke the opposite side's methods merely because it has any valid JWT.

Agent IDs must be derived from authenticated claims, not trusted from request bodies.

## Connectivity and Offline Lease

JWT lifetime and workflow lifetime are independent. A lost API or SignalR connection does not kill a running process immediately.

On disconnect:

- the agent stops accepting new jobs;
- the currently running node may reach its normal completion boundary;
- logs and results enter a bounded durable local outbox;
- no next node starts without a valid connectivity lease;
- the agent attempts reconnection and token renewal with bounded backoff.

The connectivity lease permits a maximum 15-minute offline interval from the last successful server validation. If it expires, the workflow is suspended before the next node and reported as a system-level interruption when connectivity returns. On reconnection, the agent renews its JWT and lease, flushes the outbox idempotently, and resumes only if the job remains authorized.

A disabled, deactivated, expired-license, or invalid-license response is terminal for new work and cannot be treated as a transient network failure.

## API Surface

### Customer runtime

- `GET /api/license` returns current license status and seat usage.
- `GET /api/license/installation-request` exports the public installation request.
- `POST /api/license/import` validates and installs a signed offline license.
- `GET /api/agents` lists identities and seat states.
- `POST /api/agents` creates a pending identity.
- `POST /api/agents/{id}/activation-code` generates a one-time code.
- `POST /api/agents/{id}/disable` blocks use while retaining the seat.
- `POST /api/agents/{id}/deactivate` revokes credentials and releases the seat.
- `POST /api/agents/{id}/rotate-credential` authorizes a controlled credential replacement flow.
- `POST /api/agent-auth/activate` consumes an activation code and returns the initial credential.
- `POST /api/agent-auth/token` exchanges a credential for a short-lived JWT.
- `POST /api/agent-auth/refresh-lease` renews the connectivity lease.

Responses use stable error codes including `LICENSE_MISSING`, `LICENSE_EXPIRED`, `LICENSE_SIGNATURE_INVALID`, `LICENSE_INSTALLATION_MISMATCH`, `AGENT_LICENSE_LIMIT_REACHED`, `ACTIVATION_CODE_INVALID`, `ACTIVATION_CODE_EXPIRED`, `AGENT_DISABLED`, and `AGENT_DEACTIVATED`.

## Studio Experience

The Studio administration area provides:

- license identity, edition, validity, features, and activated-seat usage;
- installation request download;
- signed license import with actionable validation errors;
- agent list with pending, activated, disabled, and deactivated states;
- create-agent and one-time activation-code display;
- disable, deactivate, and credential-rotation actions;
- last-seen, token/lease health, and offline state without displaying credentials.

License and agent actions require confirmation and authorization. Secret values are never persisted in browser storage.

## Persistence and Contract Impact

New domain entities, enums, repositories, database mappings, and migrations are required. Because the repository treats Domain entities and enums as a fixed contract package, implementation must add a dated Contract Change section to `AGENTS.md` before modifying those files. The entry must name all affected packages: Domain, Infrastructure persistence/authentication, WebAPI, Agent, Studio, and LicenseGenerator.

No existing credential-vault plaintext guarantees may be weakened. The license-generator signing key is external to the customer runtime and is not stored in the customer Vault.

## Testing Strategy

Every implementation task follows failing test, minimal implementation, passing test, and commit.

Required tests include:

- deterministic canonical payload and signature verification;
- altered payload, wrong vendor key, wrong installation, expired license, and older-revision rejection;
- installation private-key protection abstraction and Windows DPAPI implementation;
- activation-code single use, expiry, and non-disclosure;
- activated/disabled/deactivated seat accounting;
- concurrent final-seat activation with exactly one success;
- credential hashing and rotation invalidation;
- agent JWT claims, expiry, invalid credential, and disabled/deactivated rejection;
- SignalR authentication and per-method policy separation;
- token caching and renewal under concurrent callers;
- 15-minute lease boundary, no-new-node behavior, durable outbox, and reconnect recovery;
- Studio license import, seat display, activation workflow, and secret non-persistence;
- LicenseGenerator request import, validation, signing, and export.

## Operational Defaults

- Agent JWT lifetime: 10 minutes.
- Token proactive renewal window: 2 minutes before expiry.
- Offline connectivity lease: 15 minutes.
- Activation-code lifetime: 15 minutes.
- Installation and agent secrets: DPAPI machine scope on Windows; TPM-backed non-exportable installation keys are preferred when available.
- License enforcement authority: WebAPI only.

## Deferred Hybrid Licensing Roadmap

The following items are mandatory backlog work for the hybrid release and must remain visible in planning documentation:

- vendor-hosted license validation service;
- signed revocation list and emergency revocation;
- duplicate license/installation-use detection;
- periodically renewed signed online lease;
- configurable offline grace period after the online lease expires;
- installation deactivation and transfer protocol;
- audit trail for vendor-side issuance, replacement, and revocation;
- TPM-backed non-exportable installation-key support as a hardened deployment option;
- privacy-preserving telemetry limited to license and installation identifiers;
- migration from offline licenses without re-enrolling existing agents.

The offline payload includes stable `licenseId`, `installationId`, `revision`, and schema-version fields so the hybrid service can adopt existing licenses without changing their identity.

## Acceptance Criteria

The release is accepted when a vendor can generate a signed license for a customer's exported installation request, the customer can import it through Studio, only the licensed number of agents can become activated, copied or edited licenses are rejected, activated agents obtain and renew short-lived JWTs, unauthorized SignalR calls are rejected, and an agent safely stops at a node boundary after 15 minutes without server connectivity.
