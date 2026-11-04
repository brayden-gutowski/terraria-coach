# A person in a mutated world

User wants an entertaining YouTube companion, not a helpful generic chatbot. Keep the working HF pipeline and fast default local speech. Existing world memory and single-player boundaries stay intact.

## Behavior

Coach is a chill, funny, occasionally stubborn human watching a real, mutated Earth. First impossible sightings are shocking, not familiar game enemies. Escalate surprise by scale and threat: a moving blob is baffling; a huge flying eyeball can cause a short incoherent scream followed by useful advice. Expertise comes from this world's observed episodes. Do not endlessly rediscover familiar creatures or invent threats.

Fear is now darkness, replacing heights even if old saved personality mentions heights. React to visibly poor visibility or going underground, not routine sentence endings. Familiar tools/health/minimap/accessory information remain understood. Ordinary shelter/labor estimates use real-world time; fast construction/mining should surprise the coach when witnessed. No recipes, progression or game advice from pretrained knowledge.

Occasional natural unsolicited comments, scene reactions and callbacks; no constant narration. Preserve inside jokes and shared stories in existing world persona/summary/facts. Reuse jokes with variation and timing, not a repeating script. Ordinary replies stay short/fast; occasional calm tangents can run roughly 120–180 words. Tangents hold the floor, play a brief heckle at most once if spoken over, then continue and answer queued user speech after finishing.

Microphone should reject breath/noise, not just amplitude. Disable AGC; use local periodicity plus amplitude speech-onset gating, provide much higher thresholds and a live input meter. No new paid speech detector. Synthetic checks cannot guarantee all physical microphones; expose calibration rather than claim perfect separation.

## Approach and implementation calls

Prompt-only changes cannot prevent the browser immediately canceling speech or restore temporal evidence. Chosen approach: focused prompt overhaul plus lightweight local speech filtering, explicit normal/panic/tangent delivery metadata, and two recent screen images. Keep installed dependencies and HF routes unchanged; no game-entity hooks or extra inference loop. A separate real-time model/provider migration would threaten the working setup and isn't needed here.

Implementation calls (not user-specified timings): unsolicited conversational opportunities after ~25 seconds without coach speech; optional tangent opportunity after90 seconds, cooldown3 minutes. Tangents only eligible with current imagery and >60% health, never dead. Model still decides relevance and danger. Explicit mute/disconnect/focus/world loss always stop all speech. Ordinary detected speech does not immediately cancel output; confirmed words during generation replace stale pending replies. Spoken stop commands can stop ordinary speech; tangent heckling is intentional, but UI controls always win.

Temporal images are bounded in-memory only, same active world/activity, discarded on interruption/focus/world transition; previous image older than60 seconds is not sent. No recordings or screenshots saved. No world schema migration/reset. README remains ignored. Publish a new local artifact folder under original workspace so the launcher preserves current world data. Do not replace running host binaries or make paid test calls.

## Verification

Regression tests for high-energy breath/noise rejection versus voiced syllables; soft speech threshold calibration; no AGC; continuous mic and bounded ASR queues; ordinary speech/tangents survive raw sound; native and remote tangent heckle/resume; queued question handled once; mute/focus/world switch cancels all delayed callbacks; proactive delivery and cooldown; same-world image chronology but no cross-world images; parser accepts absent/new bounded metadata; callbacks persist through rejoin. Full suite, published isolated HTTP and native browser checks. One independent read-only review. Actual humor, fear/perception quality and physical microphone performance require a live player test.
