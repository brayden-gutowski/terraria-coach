# Fast, responsive coaching correction

User requested meaningful answers instead of repetition and preferably very quick replies. Continue native implementation in the non-Git workspace. No changes to world identity/storage, enemy visibility, initial knowledge or continuous microphone requirements.

Evidence: read-only inspection of the reported world's saved dialogue confirms ASR captured actual questions, with no corrupt persona/summary/facts. Recent dialogue was reversed and injected as system text; each actual question was inside JSON rather than normal conversational turns. Pipeline waited for ASR, a long JSON decision and remote TTS serially, with no per-stage timing.

Implement:
- Chronological user/assistant messages with latest actual question as final user text; omit dialogue from system memory, bound working context while preserving persisted history and relevant learned facts. Strengthen direct-answer and anti-pep-talk instructions.
- Prevent long near-verbatim repeats; remain silent on redundant proactive output; permit one corrective answer attempt if a spoken question receives an old script. Never erase saved conversation to break the loop.
- Short speech-first decisions with one compact optional learning update and smaller output cap. Use Qwen/Qwen3.6-35B-A3B:deepinfra by default based on HF public probes (live image + structured output, ~293ms first token and118tokens/sec, not a latency guarantee). Disable thinking using the model's documented chat_template_kwargs.enable_thinking=false. All calls still go through HF billing; retain HF_VISION_MODEL override.
- Fast local system voice by default when available, with HF neural voice as a selectable alternative/fallback. This removes the remote synthesis request from the fast path; continuous mic and interruption remain active. Fast voice follows Windows default output. Lower endpoint hangover to450ms; preserve earlier pending ASR across pauses.
- Show per-stage measured timings, without logging speech, images or tokens. Publish separately as companion-huggingface-fast, preserve same memory and pairing folder.

Tests before fixes: wire messages/chronology/latest question, prompt/output limits, proactive/spoken duplicate handling, skip remote TTS in local-voice mode, native speech cancellation and VAD end timing. Run full suite + isolated published HTTP/browser checks, request one independent review, fix material findings. Live model latency/quality requires user token; no invented speed guarantee.
