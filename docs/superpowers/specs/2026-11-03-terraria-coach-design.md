# Terraria survival coach design

Status: approved by the user on 2026-11-03 with the single-player and continuous-listening amendments incorporated below. Initial implementation and packaged artifacts produced; see docs/manual-validation.md for verified behavior and remaining live checks.

Provider amendment: the user subsequently explicitly requested Hugging Face instead of OpenAI. The active implementation uses Hugging Face routed vision, ASR and TTS with HF_TOKEN; continuous listening, perception limits and world persistence remain binding. Current setup and validation are in README.md and docs/huggingface-validation.md. Earlier OpenAI implementation notes are historical.

## Intent

Create a Terraria mod with a spoken AI companion who watches the player's screen, responds to the player's voice, and coaches their survival. The character experiences the feed as real events on a strange Earth. The player is one of the last humans in their land and must survive to save the world.

The experience should feel like sharing an adventure with a charismatic, anxious friend whose expertise grows through shared experience. The model plays this fictional role; presentation and dialogue maintain the premise without requiring the underlying model to literally believe it.

## Confirmed requirements

- Perception combines screenshots with selected mod data, as chosen by the user.
- Health and the minimap are understood immediately.
- Available Cell Phone and component accessory information is understood immediately, including the fundamentals of those readings.
- Mana is unfamiliar initially. Its meaning and use must be learned through observation or conversation.
- Weapons and equipment receive ordinary visual/material estimates initially, not encyclopedic knowledge of their mechanics. A gold sword can look better than copper without knowing its damage statistics.
- Enemies can be identified only when visible in the player's main scene. No hidden nearby-enemy feed is permitted. Available accessory readings may report their normal information, such as an enemy count, without granting additional identities or locations.
- The player can talk to the coach and receive spoken replies and proactive coaching.
- The mod is single-player. The microphone listens continuously during active world play; no push-to-talk is required. Use speech detection, natural interruption, and echo cancellation while retaining a mute control.
- The coach is charismatic, conversational, emotionally invested in survival, familiar in tone with contemporary culture and memes, sometimes profane under stress, sometimes loud or speechless, and intensely afraid of heights.
- Ordinary Terraria combat and disturbing creatures are expected parts of this fictional adventure. The coach can encourage fighting onscreen creatures and react naturally to combat.
- Confidence grows with experience. First encounters can provoke fear and uncertainty; repeated successful encounters can produce confident, profane encouragement. Confidence is not a fixed tenth-encounter script.
- **The coach belongs to a world. Every new world starts with completely fresh memory. Leaving and rejoining the same world restores that world's personality development, context, memories, and confidence.** Character changes do not create a separate coach for the same world.

## Approaches considered

1. Screen-only perception: best matches a literal video observer, but HUD text and rapid changes can be misread and requires more visual processing.
2. Screenshots plus selected mod data: reliable allowed readings while preserving visual discovery. Requires a strict data boundary to avoid accidental omniscience. **Selected by the user.**

## Proposed implementation defaults

These implementation defaults were accepted by the user, with continuous listening replacing the original push-to-talk proposal.

- Windows first, using a C# tModLoader client mod and a local companion process for AI, audio, and persistence. The companion must never block the game update/render loop.
- Target the installed supported stable tModLoader build; verify actual installation and API compatibility before choosing framework versions. No assumption of compatibility with vanilla Terraria or all mods.
- Use a cloud vision-capable language model, speech recognition, and speech synthesis for the initial version, configured through the companion. Choose supported provider/model settings during implementation and keep credentials outside mod files and world saves. Cloud use requires the user's own credentials and incurs provider usage costs; no purchases or account creation are part of this design.
- Continuous microphone listening during active play, with voice activity detection, echo cancellation, a mute control, and text transcript. Speaking interrupts current coach playback and obsolete queued responses. Do not silence microphone input merely because the coach is speaking. Show permission/device failures explicitly instead of silently falling back to push-to-talk.
- Watch the Terraria game viewport, not the entire desktop. Begin with approximately one screenshot every two seconds while active, with a bounded increase for significant events. These are tuning targets, not latency guarantees.
- Single-player, one active local player. Multiplayer/shared coach coordination is outside this project's scope.

## Perception boundary

Build an explicit allowlist rather than exporting general game state and relying on prompts to ignore it.

Allowed structured information:

- Current and maximum health, death/respawn state, world session identity, capture timestamp, and screenshot dimensions.
- Available and enabled accessory readings, using their actual displayed meaning and units. A reading is not supplied merely because an accessory exists somewhere in an inventory; use the game's actual activation/display rules, including normal sharing where supported.
- Visible minimap imagery/context. Screenshots must not reveal unexplored terrain or provide raw world tiles or unseen entities as structured map data.

Not supplied to the coach:

- Raw NPC/enemy registries, IDs, hidden positions, internal names, stats, or unseen spawn events.
- Internal mana semantics, spell classifications, item damage/defense values, recipes, progression guides, boss schedules, undiscovered terrain, or a Terraria encyclopedia.
- Enemy labels inferred solely from bounding-box intersection when the sprite is actually hidden by terrain, darkness, menus, or another overlay.

The mod may use private local identifiers for bookkeeping and world selection; these do not become semantic facts in model input. Screenshots may show HUD mana, item tooltips, labels, and accessory text naturally visible to the player. The coach may learn from those observations and what the player says. A permitted detector reading that names a creature can be understood as an instrument report; it does not mean the coach has visually encountered or located that creature.

The model may already have Terraria knowledge in its pretrained weights. Prompting and constrained context can discourage its use but cannot guarantee total ignorance. Evaluate this explicitly rather than claiming that hidden model knowledge has been removed.

## Runtime flow

1. Entering a world selects its persisted coach using a stable world identifier, not its display name or the character name.
2. Restore that world's state before allowing any model input, speech, or memory updates. A new world initializes the same base persona with an empty experience store.
3. Capture timestamped viewport images and allowlisted telemetry. Retain only a small recent window for immediate reasoning.
4. Merge recent observations, player speech, relevant world memories, and the role instructions into a bounded model context.
5. Decide whether to speak. Prioritize the player's question and meaningful threats, discoveries, and changes over repetitive narration. Routine observations normally stay silent.
6. Play a short spoken response with transcript and record supported observations and experience updates.
7. Save incrementally and flush on world exit. Cancel pending requests/playback on world exit or change, and reject late responses by session/world token so one world's state cannot enter another.

Network/model failures must leave Terraria responsive. Drop stale screenshots and obsolete advice instead of accumulating a queue. After recovery, reason from fresh observations. These are conversational reactions with variable latency, not guaranteed frame-level warnings.

## Memory, learning, and confidence

Persist each world's evolving persona state, relationships and preferences expressed in that world, encounter records, learned mechanics, unresolved questions, recent dialogue, and longer-term conversation history/context. Keep an append-only conversation/event record alongside periodic snapshots and summaries; context limits require selecting and summarizing history rather than submitting every past utterance on every request.

Persist the complete application-owned state; do not promise to restore opaque internal model activations. The same world should resume its established character and knowledge even if the player character changes. No world-specific information is retrieved from another world. Account credentials and audio/device preferences are application settings, not coach memories.

Experience records distinguish player claims, visual observations, estimates, and stronger repeated evidence. Confidence is per creature/mechanic, not a single global number. Count separate encounter episodes rather than repeated frames of the same creature. Learned visual descriptions must support matching recurring creatures without importing game NPC identifiers. When identification is uncertain, avoid merging distinct creatures or awarding expertise.

Repeated successful encounters build confidence; defeat, unexpected behavior, or a stronger-looking variant can reduce it. Ten sightings alone do not establish combat mastery. The coach can be boastful in delivery while grounding its practical advice in observed outcomes.

Examples of intended tone:

- First apparent zombie: "Is that a zombie? I've never seen anything like that. I'd back up, but that sword might give you a chance."
- Familiar successful matchup: "Another zombie. You've handled these before. Kill that fucker."
- Height: "The ledge looks wide enough. I still need you to step away from the edge before I lose my shit."
- Unexplained mana change: "Those blue stars dropped when you did that. Is that what powers it?"

Profanity and panic vary with context instead of appearing in every line. Voice delivery depends on synthesis capability; punctuation alone cannot guarantee convincing shouting or silence. Calm moments allow normal conversation. Familiarity with memes is a tone goal, not a promise of verified live news; no automatic web/news integration in the first version.

## Persistence and controls

- Store versioned local coach state under a configurable companion data directory, keyed by stable world identity, with atomic snapshots and a recovery copy.
- Save periodically, after material learning/conversation changes, and on normal exit. A crash may lose only the last unsaved updates; recovery must not silently load another world's state.
- Preserve existing world state on reconnect or missing/corrupt data. Report recovery problems rather than silently wiping an established coach.
- A deliberately generated new world gets a fresh coach even if its display name or seed matches another. A copied save retaining its world identity shares that identity unless the user explicitly resets it. Moving a world to another computer also requires moving its companion state in this initial local-storage design.
- Controls: connect/status, listening/speaking indicator, microphone/output selection, mute, transcript, verbosity/cooldown, and an explicit reset for the current world's coach. Reset requires confirmation because it deletes the world's learned state.
- Send game images and continuous microphone audio only while connected and active. Keep raw screenshot/audio retention off by default. Pause capture outside active world play or when the game loses focus; resume automatically on return when unmuted. Keep provider credentials and raw captures out of logs. Initial connection and operating-system/browser microphone permission are setup actions, not repeated speaking gestures.

## First playable scope

Deliver the mod bridge, local companion, viewport vision, health and available accessory telemetry, spoken interaction, personality instructions, world-scoped persistent learning, basic controls, and installation/setup instructions. Include a simulated-input mode for evaluating behavior without a running game or paid API calls.

Defer custom voice training, model fine-tuning, automatic current-events browsing, and deployment/Workshop publishing. Multiplayer coordination is out of scope. Continuous listening is required in the first playable version.

## Acceptance checks

- A new world knows health/minimap and available accessory fundamentals but begins without learned mana, creature, or weapon-specific knowledge.
- A hidden/offscreen creature does not appear in structured enemy data. A permitted enemy count can prompt concern without invented identity/location.
- Gold-versus-copper advice is framed as an initial estimate; learning from visible tooltips and observed fights is allowed.
- Repeated screenshots of one encounter do not produce ten encounters. Successful repeated encounters increase confidence; uncertainty or losses can change it.
- Speech reaches the coach without a keypress, receives audible replies, and can interrupt old playback while its microphone remains active. Test speaker echo, background game audio, silence, microphone permission denial, unplugged devices, mute, focus loss, and automatic resumption. Quiet periods do not produce constant narration.
- Exiting and reopening world A restores A's persona/context. A new world B starts fresh. Returning to A restores A again. Changing characters in A retains A's coach.
- A pending response from A cannot speak or update memory after switching to B, even if its network request completes late.
- Process restart, disconnect, and simulated provider failure preserve saved state and game responsiveness.
- Manually validate in the actual installed tModLoader game, including capture occlusion, focus handling, accessory availability, audio feedback, latency, and the model's tendency to use unearned Terraria knowledge. Simulations alone do not establish in-game compatibility or personality quality.

## Builder decisions and prerequisites

Verify the user's Terraria/tModLoader installation and local toolchains before implementation. Select companion runtime, IPC framing, storage implementation, AI/audio provider adapters, capture hook, screenshot sizing, and model context budget using the simplest compatible options. Expose supported model configuration rather than hardcoding a supposedly current model. Paid credentials are needed only for live AI validation; continue offline validation without them and clearly report the remaining live checks.

## Reference checked

Official tModLoader InfoDisplay documentation describes active display behavior and the player's visibility controls; actual per-accessory behavior must be verified against the installed build: https://docs.tmodloader.net/docs/stable/class_info_display.html
