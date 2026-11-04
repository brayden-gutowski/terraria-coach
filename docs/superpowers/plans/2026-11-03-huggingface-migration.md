# Hugging Face migration implementation plan

Authorized by user: “Update the mod to use huggingface instead.” Native execution, continuing the approved survival-coach design. Provider selection and transport change; personality, perception boundaries, continuous listening and world persistence stay required. No extra approval checkpoint for this explicitly requested provider replacement.

## Design and decisions

Use Hugging Face routed inference with server-side HF_TOKEN, not custom provider keys or an OpenAI account. Public model mappings and official SDK routes checked on 2026-11-03: Qwen/Qwen3-VL-8B-Instruct:featherless-ai for vision/conversation, openai/whisper-large-v3-turbo via HF's DeepInfra route for ASR, hexgrad/Kokoro-82M via that same HF route for TTS. These model defaults remain configurable. The word openai in Whisper's model namespace/API compatibility routes does not mean calls are billed by OpenAI.

Replace WebRTC-to-OpenAI with getUserMedia echo cancellation, an AudioWorklet feeding local voice detection, bounded PCM WAV speech segments sent to the companion, and browser playback of generated audio. No push-to-talk or microphone gating during coach speech. Speech starts cancel current playback and provider work. Long utterances are transcribed in bounded segments before requesting a reply; silence triggers the response. Expose microphone sensitivity since hardware/noise differs.

The companion retains the latest actual screenshot separately from null-image heartbeats, uses only current allowlisted readings, and asks the VLM for a bounded JSON decision with speech and memory updates. Existing persistent world-state format is retained. Every async stage checks world generation, activity revision and turn ownership; stale replies cannot speak or mutate another world. Proactive turns can return empty speech. No Hugging Face token or raw provider payload is sent to the browser/logs.

## Tasks

- [ ] Add failing backend tests for routed HF vision/ASR/TTS, credential containment, JSON decision validation, safe account/rate errors, and cancellation/session isolation. Implement HuggingFace.cs and HuggingFaceCoach.cs; preserve world memory.
- [ ] Add failing browser tests for VAD silence, bounded long speech, valid WAV encoding, continuous input during playback, natural interruption, stale-world/activity results, and quota stop/retry timing. Replace realtime.js with Hugging Face voice controller and speech.js/audio-worklet.js.
- [ ] Replace /api/call with claim/transcribe/turn/interrupt operations; keep single browser ownership, loopback security and selected mod data. Add activity revision/latest visual caching to CoachSession and test transitions.
- [ ] Update UI/setup/docs/scripts for HF_TOKEN and HF model/voice settings. Publish to artifacts/companion-huggingface to avoid changing the user's running old process. Rebuild the mod with updated description/version only if needed; bridge protocol remains compatible.
- [ ] Run all local checks and isolated-port HTTP smoke tests, inspect offline UI, request one independent review and fix material findings. No live inference without configured user credentials; document that limit and provide exact restart/setup instructions.

## Verification

dotnet build tests/Coach.Checks -m:1; execute checks; node --test tests/realtime.test.mjs. Real HTTP tests must use an isolated port to avoid contacting a running user companion. No test may overwrite real world data or stop a process it did not start.

References: huggingface.co/docs/inference-providers/tasks/chat-completion; huggingface.co/docs/inference-providers/pricing; huggingface_hub official inference/_providers/deepinfra.py routes /v1/openai/audio/transcriptions and /v1/openai/audio/speech under https://router.huggingface.co/deepinfra.
