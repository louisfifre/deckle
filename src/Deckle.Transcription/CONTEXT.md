---
name: context-deckle-transcription
description: "Transcription vocabulary — the two entry points (dictation, file transcription), the T1 fidelity criteria, and the segmentation units (neural VAD vs energy segmenter, utterance vs segment). Read before touching the pipeline or judging output quality."
type: agent-instructions
---

# Deckle.Transcription — Context

Vocabulary of the transcription pipeline: how audio enters it, what fidelity means for its output, and the units its segmentation devices produce.

## Entry points

Two ways into the same transcription pipeline, told apart by where the audio comes from and where the text lands. The engine, the model, and the HUD states are shared; only the edges differ.

**Dictation** :
The hotkey-driven live path — mic capture, energy-segmented or monolithic decode, delivery to the clipboard (with optional paste and rewrite). The historical and primary path; the T1 fidelity contract below describes it.
_Avoid_ : recording (the capture phase, not the whole path).

**File transcription** :
The tray-initiated path over one pre-recorded audio file: picked through the system file dialog, decoded and resampled to the pipeline's native format, then emitted as capture frames into the same energy segmenter, utterance channel, and sole ASR consumer as live streaming dictation. It runs without the dictation prompt because a file is an independent acoustic domain. Delivery is an explicit command: the text is written beside the audio as a file with the same base name and copied to the clipboard; paste and rewrite never run. One tray selection may contain several files, but each keeps its own prompt context and adjacent output.
_Avoid_ : import (nothing enters a library).

**File transcription batch** :
A tray selection treated as one indivisible orchestration unit: its file transcriptions retain selection order and share one engine lifecycle. A later selection is another batch, never an extension that may interleave the active one.
_Avoid_ : file transcription (one file's run), import.

## Fidelity criteria

The T1 canonical mode (`apply_transcription_request`) is the production transcription path exposed by Deckle. Its output lands in the clipboard for immediate use. The implicit usage criterion is high-volume confidence — Louis must be able to dictate twenty minutes and trust the output without re-reading every line.

The criteria below come from observed failure modes. Each names a class of deviation that discriminates an acceptable transcription from a dangerous one.

**Grammatical number fidelity** :
A singular that becomes plural (or the inverse) can flip an instruction's scope without surfacing as an obvious error. Observed example on audio `701ce47a` : « le contexte minimal 8K » transcribed as « les contextes minimaux 8K » — a targeted instruction turned into a global one. High-severity defect on T1 even when the surrounding sentence is correct.

**Plausible reformulation** :
A transcription error masked by a semantically acceptable substitute, hiding the original mishearing. Observed example on audio `701ce47a` : « à côté » misheard as « en côté » (a non-existent expression), rewritten as « en même temps » — a synonym that fits the context but is unfaithful to the audio. Worse than a visible error because re-reading does not catch it. The hypothesis attached to this pattern is that the chat-mode pass mixes acoustic decoding with a semantic-coherence pressure that overwrites suspicious tokens with plausible neighbors; to be verified.

**Manifest vs invisible error** :
Distinction induced by the plausible-reformulation criterion. A manifest error (typo, non-word, syntactic break) signals itself to the reader. An invisible error (synonym swap, number flip, register shift, name swap) passes the eye and lands in production. Deckle's fidelity criteria prioritize visibility over local polish — a polished output that is invisibly wrong is the worst outcome.

## Segmentation — detection vs cutting

Two devices carry the word "VAD" and are constantly conflated, yet they are different in kind: one is a model that finds speech in a finished buffer, the other is a threshold that cuts a live stream. They also produce two different units of "cut", which must not be confused.

**Neural VAD** :
A neural-network voice-activity detector (today Silero) that runs over a whole captured buffer to find speech regions and trim silence before decoding. Model-based and post-hoc — it needs the full buffer in hand. Implemented by `Deckle.Vad` upstream of the ASR backend; Whisper's internal VAD stays disabled.
_Avoid_ : VAD (bare — ambiguous), Silero (vendor-specific; the term should outlive the model).

**Energy segmenter** :
A threshold-on-RMS state machine (`Streaming/EnergySegmenter`) that runs live on the capture's real-time energy stream to place **utterance** boundaries at silences. Not a model and not speech recognition — it reads energy dips to decide where to cut, feeding the streaming producer/consumer pipeline.
_Avoid_ : energy VAD (it does not detect voice, it cuts on silence), VAD.

**Utterance** :
The unit the energy segmenter emits — a speech span bounded by detected silence (or by the safety ceiling), and the atomic audio chunk handed to the backend for one transcription call.
_Avoid_ : chunk, segment.

**Segment** :
Whisper's *output* unit — a short timestamped span the model re-derives **inside** each utterance from its own decoding. An utterance is an input cut decided by energy; a segment is an output cut decided by Whisper. One utterance contains one or more segments.
_Avoid_ : utterance (the input unit), window (the 30 s encoder input).
