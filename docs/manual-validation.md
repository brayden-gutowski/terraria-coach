# Validation record

Date: 2026-11-03. This is an implementation and offline validation record, not a claim that a live play session has passed.

## Automated evidence

- Core/host build: .NET SDK 8.0.425, .NET 8 target; zero warnings/errors.
- Protocol: unknown/hidden fields rejected, health/image bounds, supported accessory visibility, valid reading roundtrip, PNG header and decompression.
- Memory: world A/B separation, A restore across process recreation, evolving persona/facts, late generation rejection even on reentry, encounter outcome deduplication and evidence-sensitive confidence, corrupt-state preservation, snapshot/log recovery, bounded context, latest-only frame selection and mute.
- Voice controller: continuous microphone during coach responses, mute/focus suspension, stale generation rejection, no proactive response during user speech, bounded image window, idempotent tool events, visible microphone-permission failure.
- Provider proxy: fake HTTP verifies server-side key placement and sanitized failures; no paid API requests made.
- Independent correctness review completed. Regression tests reproduce and fix delayed tool/response sequencing, stale same-world transport callbacks, unsupported future memory schema preservation, durable-log/checkpoint failure coherence and personality/summary retention under context pressure. Pending voice handshakes are aborted when suspended.
- Mod compilation and `.tmod` packaging: passed using official stable **v2026.09.3.1**, downloaded into `.dev/tmodloader`; no installation or save writes outside the workspace.
- Real loopback HTTP smoke tests: five checks passed for startup/static assets, origin/pairing authentication, allowed readings and hidden-state rejection, world isolation/restore and stale-heartbeat suspension.
- Local companion UI rendered and visually inspected in headless Microsoft Edge at 1200 × 1100. Offline simulation showed the expected status, mock conversation, device controls and setup section; no microphone or provider calls used.
- Final automated suite: 20 C# core/host checks and 10 JavaScript voice-controller checks, all passing. Published framework-dependent companion generated under `artifacts/companion`; requires .NET 8 ASP.NET Core runtime.

## Live checks still required

No API key is configured in the implementation shell. No running tModLoader graphical play session was controlled. Therefore these checks remain **not run**:

- Load packaged mod in a real single-player client, verify viewport capture including health/minimap/tooltips and readable accessory readings.
- Enable/hide/remove each vanilla information display; verify exact visible values and no offscreen creature identity except permitted instrument reports.
- Confirm new world freshness and same-world character/rejoin behavior with actual world files; use disposable worlds.
- Microphone permission, device unplug/reconnect, speakers/headphones, echo/game-audio rejection, browser playback restrictions, automatic focus/pause resumption and natural interruptions.
- Live Realtime audio/image/tool-schema compatibility for the configured account/model, connection expiry/recovery, and actual response latency.
- First/familiar zombie, material-based weapon estimates, mana discovery, fear of heights, ordinary dialogue, personality variation and confidence after defeat.
- In-game performance of bounded GPU readback and background encoding; 4K/high-DPI readability and focus behavior.

Do not treat unit tests or successful compilation as substitutes for these checks. Record client build, model/voice, browser, audio devices, measured behavior and failures here during the first play session.

## User play-test: API credit exhausted

The user reported WORLD ACTIVE with a real world identifier, followed by HTTP 429 from the voice API, and subsequently confirmed having no API credits. The game bridge connects; live voice is blocked by account credit availability.

Diagnostic update is published separately under artifacts/companion-diagnostics, preserving the existing running companion. It exposes recognized safe provider codes, stops automatic retries on credit/quota/spend limits, and honors Retry-After for temporary rate limits. Regressions observed failing before the fix; final suite passes 21 C# checks and 12 JavaScript checks. No paid API calls were made. A requested review of this small follow-up change was interrupted by the user before completion; the initial implementation's independent review remains as recorded above.
