# Personality and conversational presence Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans. Steps track native implementation with one final independent review.

**Goal:** Turn the working coach into an expressive, occasionally stubborn companion without losing responsiveness, privacy or world continuity.
**Architecture:** Existing browser owns speech detection/playback; host owns model decisions and bounded scene/cadence state. Existing world persona/summary/facts store jokes and experiences. No new dependencies or inference routes.
**Tech Stack:** .NET8, browser JS/Web Audio/SpeechSynthesis, HF vision/Whisper/Kokoro.
**Spec:** docs/superpowers/specs/2026-11-04-personality-design.md

## Global constraints

Single-player, continuous mic, visible-only creatures, existing allowlisted telemetry. README ignored. Preserve actual world files and pairing. No paid automated calls. Immediate mute/disconnect/focus/world cancellation overrides stubbornness. Worktree feature/personality-and-conversation; original host stays running.

## Review focus

- Breath/noise should not cause uploads; real voiced phrases still pass.
- Delayed utterance/audio callbacks after world changes must do nothing.
- Interrupted tangent must not lose its continuation or queued player question.
- New delivery fields must be optional, bounded, and not cause repeated paid failures.
- Temporal screen context must not cross world, activity or stale-frame boundaries.

### Task1: Speech filtering and calibration

Files: wwwroot/speech.js, voice.js, app.js/index.html; tests/huggingface.test.mjs.
Interface: SpeechSegmenter retains push/reset, adds rms/effectiveThreshold diagnostics; starts only after sustained voiced audio. Browser disables AGC and offers thresholds .015–.16, default .04; saved old values migrated to new setting key.
- [x] Add realistic voiced/noise regression signals, assert breath/noise does not upload and speech with prefix/tail still does. Run RED.
- [x] Implement amplitude + normalized periodicity onset check with bounded CPU, preserve long-segment and silence safety. Add input meter and explicit threshold labels. Run browser suite GREEN.
- [x] Commit task with evidence in ledger.

### Task2: Human personality, cadence and scene continuity

Files: Persona.txt, HuggingFace.cs/HuggingFaceCoach.cs, tests/Coach.Checks/HfChecks.cs.
Interfaces: CoachDecision/TurnResult Delivery='normal'|'panic'|'tangent', Interruption string<=100; optional remote AsideAudioBase64. DecideAsync optional previousFrame, allowChatter, allowTangent. Host validates model metadata and gates tangent eligibility; no schema change.
- [x] Wire regressions for optional metadata, chronological two-image context, focus/world isolation, tangent cooldown and ordinary fast output. Run RED.
- [x] Rewrite persona/transport instructions, add bounded scene/cadence state, preserve fast default and existing errors. Up to2 compact memory updates permit joke+learning together. Local tangents return text; remote synthesizes brief interruption alongside main audio. Run C# suite GREEN.
- [x] Commit task with evidence in ledger.

### Task3: Floor-holding playback and delivery

Files: voice.js, browser tests, validation doc.
Interface: confirmed words queued while coach plays; tangent heckle once then continuation. Local playback sentence queue; remote playback pauses main, plays pre-generated aside, resumes. Lifecycle invalidates both. Completion dispatches queued text without waiting for proactive cooldown.
- [x] Regression tests for no raw cancellation, tangent local/remote rebuke+resume, pending text, no proactive overlap and lifecycle cleanup. Run RED.
- [x] Implement bounded playback state and protected tangents, retain explicit controls. Run whole suite GREEN.
- [x] Review whole branch independently, fix material findings with regressions. Publish artifacts/companion-huggingface-personality locally, run isolated published HTTP/native-browser checks, inspect UI. Commit and record live-test limitations. No public push required by current request.
