# Terraria Survival Coach Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a single-player Terraria companion that watches the game, converses continuously by voice, and remembers its evolving personality and experience separately for each world.

**Architecture:** A C# tModLoader mod sends viewport images and allowlisted telemetry to a local ASP.NET Core companion. A small companion browser page uses native WebRTC microphone/speaker handling for OpenAI Realtime; the backend keeps credentials, world state, persistence, and session ownership. Use standard libraries and plain JavaScript; no frontend framework, vector database, or custom audio codec.

**Tech Stack:** .NET 8 ASP.NET Core companion, C# tModLoader client mod targeting the verified installed stable build, System.Text.Json, browser WebRTC/getUserMedia, OpenAI Realtime, and a dependency-free executable C# check harness plus Node's built-in JavaScript tests.

**Spec:** `docs/superpowers/specs/2026-11-03-terraria-coach-design.md` (approved, including continuous listening and single-player amendments).

## Global Constraints

- The mod is single-player. The microphone listens continuously during active world play; no push-to-talk is required.
- The coach belongs to a world. Every new world starts with completely fresh memory.
- Leaving and rejoining the same world restores that world's personality development, context, memories, and confidence.
- Character changes do not create a separate coach for the same world.
- Health and the minimap are understood immediately.
- Available Cell Phone and component accessory information is understood immediately, including the fundamentals of those readings.
- Mana is unfamiliar initially. Its meaning and use must be learned through observation or conversation.
- Enemies can be identified only when visible in the player's main scene.
- The companion must never block the game update/render loop.
- Keep raw screenshot/audio retention off by default.
- Pause capture outside active world play or when the game loses focus; resume automatically on return when unmuted.
- No implementation, purchases, publishing, or writes into installed game/save directories during plan preparation.

## Review Focus

1. A late provider event after world switching must neither speak nor change the new world's state; exercise in Task 2 and Task 5.
2. A corrupt or newer-version world snapshot must report recovery status rather than reset the coach; exercise in Task 2.
3. An accessory removed or hidden while active must stop sending its readings; exercise in Task 3.
4. Coach speaker output and game audio must not create a conversation loop; exercise simulated events in Task 5 and physical audio in Task 6.
5. Microphone denial/unplugging, game focus loss, browser disconnect, and stale game heartbeat must visibly suspend listening without losing world state; exercise in Tasks 4–6.

## Workspace and prerequisite findings

The workspace contains only the design and this plan, with no Git repository or product code. .NET SDK 8.0.425, Git, and Node are available. Terraria is installed under `C:/Program Files (x86)/Steam/steamapps/common/Terraria`. Steam's three declared libraries were checked for a normal tModLoader directory without finding a current installation; old mod files and user data are present. Do not mistake those files for a current working client installation.

Before mod compilation, resolve an actual compatible stable tModLoader client installation and references. If unavailable, finish companion/offline checks and clearly identify the remaining mod build and in-game checks. Do not report a simulated bridge as a working installed mod. Use a workspace-local development copy or explicit install instructions; do not overwrite existing Terraria files or saves.

Choose the current compatible vision-and-audio Realtime model from fetched official docs at execution time and expose its ID and voice in configuration. Use `OPENAI_API_KEY` in the companion process, never browser source, transcripts, or mod config. No live API call is needed for offline checks.

## Files and ownership

- `src/Coach.Core/Coach.Core.csproj`, `Protocol.cs`: explicit bridge records shared by mod and companion; never general game objects.
- `src/Coach.Core/WorldStore.cs`: versioned snapshots, recovery copy, append-only world event log, replay.
- `src/Coach.Core/CoachSession.cs`: current world generation, active/focus state, latest frame, event idempotency, encounter episodes, context and confidence.
- `src/Coach.Core/Persona.txt`: exact role, discovery boundaries, humor, height fear, and learning instructions.
- `src/Coach.Host/Coach.Host.csproj`, `Program.cs`: loopback server, bridge endpoints, browser events, simulation mode, limits/authentication.
- `src/Coach.Host/Realtime.cs`: provider SDP creation/configuration and memory-tool schema; injected HttpClient for offline checks.
- `src/Coach.Host/wwwroot/index.html`, `app.js`, `realtime.js`, `style.css`: local connection controls/transcript and continuous WebRTC audio. `realtime.js` contains independently testable event/state functions.
- `mod/TerrariaVoice/{TerrariaVoice.csproj,build.txt,description.txt,TerrariaVoice.cs,CoachSystem.cs,CoachConfig.cs}`: tModLoader packaging, capture/telemetry, bounded bridge sending, mute/status controls.
- `tests/Coach.Checks/{Coach.Checks.csproj,Program.cs}` and `tests/realtime.test.mjs`: behavioral checks without provider credentials or a live game.
- `scripts/{check.ps1,start.ps1,build-mod.ps1}`, `.gitignore`, `README.md`, `docs/manual-validation.md`: reproducible commands, configuration, installation, and live verification evidence.

Use one core library for shared protocol/state; no additional abstraction projects. A test project can reference the host without invoking its entry point. If tModLoader packaging cannot include the shared library conventionally, link the protocol source into the mod rather than introducing a packaging framework.

## Shared contracts

Define these records in `Protocol.cs` before consumers. JSON uses camelCase and rejects unknown bridge properties. Validate all length/range limits before state changes.

- `SessionKey(Guid WorldId, Guid Generation)`: generation is newly minted on each entry, including reentry to the same world.
- `AccessoryReading(string Kind, string Value, string Units)`: kind must be one of the verified vanilla display allowlist; never arbitrary metadata or raw game objects.
- `Observation(SessionKey Session, long Sequence, DateTimeOffset CapturedAt, bool Focused, bool Active, int Health, int MaxHealth, bool Dead, int Width, int Height, string? ImageBase64, AccessoryReading[] Readings)`.
- `CoachEvent(SessionKey Session, string Id, string Kind, string Text, string? Subject, string? Evidence, string? EpisodeId)`: allowed kinds are transcript, learned_fact, persona_update, encounter_start, encounter_success, encounter_failure, encounter_end. Evidence is observation, player_report, or estimate. Reject unknown kinds/evidence, invalid subject lengths, and expired session ownership.
- `WorldState`: schema version, world GUID, last applied log sequence, persona development, bounded summary, learned facts, encounters keyed by learned visual subject, unresolved questions, and recent dialogue. Full transcript/events remain in the world log. No character-specific namespace.
- `WorldStore.LoadAsync(Guid worldId, CancellationToken ct) -> Task<WorldState>` and `AppendAsync(CoachEvent item, WorldState state, CancellationToken ct) -> Task`; append durably before acknowledging, then atomically checkpoint at bounded intervals.
- `CoachSession.EnterAsync(Guid worldId, CancellationToken ct) -> Task<SessionKey>`, `Observe(Observation frame) -> bool`, `ApplyAsync(CoachEvent item, CancellationToken ct) -> Task<bool>`, `ExitAsync(SessionKey session, CancellationToken ct) -> Task`, `BuildContext() -> string`. Serialize transitions and validate ownership again after awaits.
- `Realtime.CreateCallAsync(string offerSdp, string context, CancellationToken ct) -> Task<string>`: returns answer SDP; provider configuration supplied via host settings. Track calls by generation and dispose/cancel any superseded call.

## Task 1: Establish strict perception protocol and runnable checks

**Files:** core project/Protocol.cs, executable checks project/Program.cs, scripts/check.ps1, .gitignore.

**Interfaces:** Produces SessionKey, AccessoryReading, Observation, CoachEvent, validated JSON decode methods consumed by Tasks 2–4. Define `Protocol.ReadObservation(string json) -> Observation` and `Protocol.ReadEvent(string json) -> CoachEvent`.

- [ ] Write failing checks named `RejectsHiddenState`, `RejectsInvalidFrames`, `AllowsKnownReadings`. Assert extra `npcs`, `mana`, item statistics and unknown reading kinds are rejected; maxHealth must be positive; health is in [0,maxHealth]; dimensions positive; sequence nonnegative; image decoded bytes at most 4 MiB and text fields bounded; valid health and available display values round-trip.
- [ ] Run `dotnet run --project tests/Coach.Checks -- protocol`; confirm missing implementation/assertion failure.
- [ ] Implement the minimal protocol records/validation, projects and runner. Limit request bodies to 6 MiB; cap ordinary text at 8,192 characters and SDP at 64 KiB. Image payload is transient and never logged.
- [ ] Run the same command; require PASS for each named case and exit code 0.
- [ ] Review the diff and record a commit only if a Git repository exists. This condition applies to all tasks; creating history is not a prerequisite for working checks.

## Task 2: Implement world-owned durable memory and session isolation

**Files:** WorldStore.cs, CoachSession.cs, Persona.txt, tests/Coach.Checks/Program.cs.

**Interfaces:** Consumes Task 1 contracts. Produces WorldStore/CoachSession signatures above and `CoachSession.Current -> SessionKey?`, `CoachSession.Latest -> Observation?` for host use. `BuildContext` emits base persona, only current-world memory, and permitted latest readings, at most 24,000 characters.

- [ ] Add failing checks `WorldResumeAndFreshStart`, `SameWorldDifferentCharacter`, `LateEventsRejected`, `RepeatedEpisodeNotCountedTwice`, `RecoverAndReplay`, `CorruptStateNeverResets`, `ContextBoundedAndWorldScoped`. Assert A learns an encounter/persona change, B starts empty, A restores after process restart; generation changes each entry; duplicate event IDs/outcomes cannot inflate experience; old generations rejected even after reentering A; interrupted writes recover without duplicating events; corrupt primary uses valid recovery/log, otherwise errors; unknown newer schema errors; names/seeds never choose storage paths.
- [ ] Run `dotnet run --project tests/Coach.Checks -- memory`; confirm the intended failures.
- [ ] Implement per-GUID files, flushed append log with sequence/event IDs, temporary-file replacement and recovery checkpoint. Replay only records after snapshot sequence; tolerate an incomplete final log record, report internal corruption. Save periodically and on exit/shutdown; no silent empty fallback for existing damaged data.
- [ ] Implement encounter episodes: model uses a learned visual subject and stable episode ID until a reported end; success/failure counts once per episode, separate from frame count and player claims. Confidence is clamped to [0,1], increases only with supported successful episode evidence, decreases with supported failure, and stays unchanged for repeated frames/player estimates. Treat it as a tuning heuristic, not a claim of calibrated probability.
- [ ] Write the persona from the approved spec, prohibiting preloaded Terraria mechanics, allowing ordinary material estimates, explicitly understanding health/map/accessory fundamentals, allowing silence and profane fictional combat advice, and separating guesses from observations. Summary/persona updates may not overwrite the base instructions.
- [ ] Run memory checks and protocol checks; require PASS. Review/commit if applicable.

## Task 3: Build single-player mod observation bridge

**Files:** all `mod/TerrariaVoice` files, scripts/build-mod.ps1, protocol checks.

**Interfaces:** Consumes Task 1 Observation and SessionKey. Produces loopback calls: `POST /bridge/enter` with world GUID -> SessionKey; `POST /bridge/observation` with Observation; `POST /bridge/exit` with SessionKey; `POST /bridge/control` with session and muted flag. Host implementation is Task 4.

- [ ] Resolve real tModLoader references/version and inspect its actual world GUID, active InfoDisplay/visibility logic, and post-interface capture hooks. Document the compatible installation path as an explicit build property, not a hardcoded developer path. Stop only mod-dependent build steps if unavailable; continue other tasks.
- [ ] Add failing core checks `AccessoryVisibilityChanges`, `LatestFrameWins`, `PausedFramesHaveNoImage`. Use pure projection helpers in Protocol.cs: `Protocol.SelectReadings(IEnumerable<(AccessoryReading Reading,bool Active,bool Hidden)> candidates)` and `Protocol.CanCapture(bool singlePlayer,bool focused,bool active,bool muted)`; assert removal/hidden/inactive values disappear immediately and multiplayer/menu/unfocused/muted states deny capture.
- [ ] Run protocol checks; confirm new assertions fail before implementation.
- [ ] Implement explicit health/allowed vanilla accessory reading projection without enemy/item enumeration. Check installed display APIs rather than reconstructing activation solely from inventory. Capture the completed game viewport including HUD/minimap, no desktop pixels, approximately every 2 seconds, hard maximum 1 frame/second after health/death changes. No sprite identification metadata is transmitted.
- [ ] Keep render-thread work to bounded capture/copy; encode/send off-thread using latest-only capacity-one buffering. Never wait for network on game threads. Stop capture and send active/focus heartbeat state immediately on transition, world unload, or mute. Restore state/enter response before transmitting any observation. Discard callbacks from prior generations.
- [ ] Build against actual tModLoader with `./scripts/build-mod.ps1 -TModLoaderPath <verified-path>` and rerun protocol checks. Verify receipt against the offline host in Task 4. Record unavailable checks honestly. Review/commit if applicable.

## Task 4: Add local companion host and offline simulator

**Files:** host project/Program.cs, scripts/start.ps1, tests/Coach.Checks/Program.cs, minimal index.html/app.js/style.css for status/transcript.

**Interfaces:** Consumes CoachSession and bridge calls above. Produces authenticated same-origin `/api/state`, `POST /api/events` with CoachEvent, `POST /api/reset` with current session plus explicit confirmation, `POST /api/call` with session/offer SDP, and `/api/stream` SSE for latest observations and generation/focus/mute changes. Reset invalidates generation and creates a fresh current-world state after confirmation; rejected confirmation is nondestructive.

- [ ] Add failing integration checks `LoopbackOnly`, `BadOriginRejected`, `BodiesBounded`, `StaleHeartbeatSuspends`, `SwitchCancelsOldClient`, `ResetNeedsConfirmation`. Assert no API key in responses/logs, unknown generation events rejected, absent game heartbeat for 5 seconds suspends media/call activity, and a new generation invalidates active browser transport.
- [ ] Run `dotnet run --project tests/Coach.Checks -- host`; confirm intended failures.
- [ ] Implement ASP.NET Core on `127.0.0.1:17841`, strict Host/Origin checks for browser APIs, no permissive CORS. Bridge requires a random startup pairing token supplied by explicit mod config; never reuse API credentials. Browser gets same-origin session/CSRF protection. Single active game/browser owner; reject a second owner instead of silently mixing sessions.
- [ ] Add `--simulate` using synthetic GUIDs, health/readings and scripted mock replies; no provider calls and no automatic real-world data access. Simulation uses a separate data directory and clearly marked status. Browser never obtains the standard provider key. Start script launches only the companion, printing its local URL; do not automatically open a visible window.
- [ ] Implement current-world status, transcript, connect, microphone/output selectors, mute, verbosity/cooldown and reset confirmation in the local page. Escape transcript text using textContent. Persist device preferences separately from world memory.
- [ ] Run host/protocol/memory checks, then exercise simulator with `./scripts/start.ps1 -Simulate`. Verify clean startup/shutdown and readable status. Review/commit if applicable.

## Task 5: Connect continuous voice, screenshots and learning

**Files:** Realtime.cs, wwwroot/realtime.js, app.js, tests/realtime.test.mjs, core/host checks.

**Interfaces:** Consumes `/api/call`, `/api/events`, `/api/stream`, context from Task 2. Produces exported browser functions `connectVoice(state, devices)`, `setActivity(active, muted)`, `handleProviderEvent(event, generation)`, `disposeVoice()` and explicit learning-tool CoachEvent batches validated by the backend. Store/restore is always backend-owned, never provider-session-only.

- [ ] Read current official Realtime WebRTC, conversation image input, VAD, and tool-event schemas. Pin exercised event formats in fixtures; no guesswork from older beta examples. Use a configured model supporting audio, images, and function calls; unsupported settings produce an actionable connection error.
- [ ] Write failing C# `SdpNeverLeaksKey`/`ProviderErrorsRecoverable` checks using fake HttpClient, and JavaScript tests `alwaysListeningDuringCoachSpeech`, `muteAndFocusSuspendTracks`, `permissionFailureVisible`, `lateGenerationIgnored`, `noProactiveSpeechDuringUserTurn`, `boundedImagesAndContext`, `toolReplayIdempotent`. Assert stale events cannot submit transcript/learning or reopen playback; suspended input buffers never replay on resume.
- [ ] Run `dotnet run --project tests/Coach.Checks -- realtime` and `node --test tests/realtime.test.mjs`; confirm intended failures.
- [ ] Implement unified WebRTC SDP proxy through `/api/call`, with credentials server-side and active session context loaded before creating the call. Browser uses getUserMedia with echo cancellation/noise suppression, adds audio tracks once, receives remote voice, and uses the provider data channel for image/text/tool events. Initial user connection gesture satisfies browser permissions/autoplay; every subsequent utterance requires no keypress or click.
- [ ] Configure speech detection with automatic responses and interruption enabled. Microphone remains enabled while coach speaks; never substitute half-duplex gating for echo cancellation. Muting/focus loss stops transmission, cancels output and clears pending audio; resume automatically only if still connected, same world, focused and unmuted. Handle device-ended events and permission/autoplay errors visibly.
- [ ] Keep only latest pending screenshot and a small sliding image window (maximum 3). Remove older image items from provider context; bound dialogue context and periodically checkpoint persona/summary/facts via a validated learning tool. Tool updates reference current generation and evidence, not hidden game IDs. Persist completed transcript/updates before local acknowledgment. Natural silence is allowed; image arrival alone does not force spoken response.
- [ ] Schedule at most one proactive observation response every 8 seconds when idle, configurable 3–60 seconds. Permit a text-only observation/learning response and speak only for meaningful danger/discovery; no speech while user is speaking. Prevent image flood from starving audio or overlapping responses. Gracefully reconnect on expiry/network failure using world state and a new transport, with bounded backoff and no stale audio replay.
- [ ] Run all offline checks and simulated provider-event sequences. With configured user credentials only, exercise real audio/image/tool flow and document measured behavior; do not claim live success without it. Review/commit if applicable.

## Task 6: Verify first playable experience and document delivery

**Files:** README.md, docs/manual-validation.md, scripts/check.ps1, existing code only for fixes discovered during checks.

**Interfaces:** Uses all prior components; produces reproducible setup/run/build instructions and an evidence record distinguishing passed, failed and not-run checks.

- [ ] Add full offline scenario `AtoBtoAWithPendingSpeechAndRestart`: A accumulates an encounter/persona change, delayed A response arrives after B entry and is rejected, B starts fresh, process restarts, A restores; corrupt snapshot recovery and stale heartbeat also exercised. Run via `dotnet run --project tests/Coach.Checks -- acceptance`.
- [ ] Run `./scripts/check.ps1`: build core/host, all C# checks, browser event tests; require exit 0 and named check results. Build mod separately against the actual client when available.
- [ ] In actual single-player play, verify health/map, each supported active/hidden/removed accessory reading, unexplored terrain and offscreen enemy boundary, screenshot readability, menu/focus transitions, and capture overhead. Confirm client/mod versions in evidence.
- [ ] Verify continuous speaking without keypresses, interruption while coach is talking, silence, game audio/music, speakers and headphones, permission denial, unplugged mic, mute/resume, device selection and browser closure. A speaker echo loop is a failure, not a completed audio feature. Record latency and any hardware-specific limitations.
- [ ] Run dialogue scenarios for first/familiar zombie, gold/copper estimates, mana discovery, fear of heights, defeat reducing confidence, and ordinary conversation. Verify the model does not jump to unearned boss/recipe knowledge; revise instructions if needed and disclose remaining limits.
- [ ] Verify world leave/rejoin, same-world character change, new same-named world, process restart, reset confirmation, provider outage and game shutdown. No tests touch real save data destructively; use disposable test worlds.
- [ ] Write installation steps for supported tModLoader, companion, API credential setup without pasting secrets into chat, pairing, browser microphone permission, model/voice configuration, launch, mute, world-state backup/transfer/reset, costs and troubleshooting. Do not publish or modify game installation automatically.
- [ ] Self-review final diff and request one independent correctness review if supported by the chosen execution workflow. Fix consequential findings, rerun affected checks, and hand over files plus honest remaining live prerequisites. Commit only where applicable.

## Source references verified during planning

- [Official OpenAI WebRTC guide](https://developers.openai.com/api/docs/guides/voice-webrtc): native browser audio and backend-created SDP calls.
- [Official OpenAI Realtime conversations](https://developers.openai.com/api/docs/guides/realtime-conversations): image inputs and conversation events.
- [Official OpenAI VAD guide](https://developers.openai.com/api/docs/guides/realtime-vad): automatic speech detection and interruptions.
- [Official tModLoader InfoDisplay reference](https://docs.tmodloader.net/docs/stable/class_info_display.html): display activation and visibility; installed-build checks remain required.

## Self-review

Spec coverage: Tasks 1/3 implement perception boundaries; Task 2 implements persona/world memory/confidence; Tasks 4/5 implement controls and continuous speech; Task 6 covers installation and actual game/audio behavior. Five review risks each have an owning test/check. Shared types and signatures are defined before their consumers. Remaining physical-device, provider-access and actual tModLoader compatibility checks are explicit prerequisites, not simulated success claims.

## Execution evidence

Implementation produced under src/, mod/, scripts/ and artifacts/. Native implementation received one independent correctness review; all four actionable findings were fixed with regressions. Final validation passes 20 C# checks, 10 JavaScript checks, five real HTTP checks against the published companion, and actual stable tModLoader compilation/packaging. Manual gameplay, paid provider and hardware audio checks remain open because no graphical game session or API credentials were available. See README.md for setup and docs/manual-validation.md for the evidence record; unchecked live checklist items above remain outstanding rather than assumed passed.
