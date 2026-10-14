---
name: glossary-deckle-transcription
description: "Transcription vocabulary. It names the two entry points (dictation, file transcription), the T1 fidelity criteria, and the segmentation devices and units (neural VAD and energy segmenter, utterance and segment). Read before touching the pipeline or judging output quality."
---

# Deckle.Transcription, Glossary

Vocabulary of the transcription pipeline. It covers how audio enters the pipeline, what fidelity means for its output, and the units that its segmentation devices produce.

## Entry points

Two ways into the same transcription pipeline, told apart by where the audio comes from and where the text lands. They share the engine, the model and the HUD states. Only the edges differ.

**Dictation** :
The entry point driven by a hotkey, in which Deckle captures the microphone live and delivers the text to the clipboard, with optional paste and rewrite. It is the historical and primary path, and the T1 fidelity contract below describes it.
_Avoid_ : recording (the capture phase, not the whole path).

**File transcription** :
The entry point that transcribes one pre-recorded audio file chosen from the tray. The file goes through the same pipeline as live dictation, but without the dictation prompt, because a file is an independent acoustic domain. Delivery is an explicit command. The text is written beside the audio, in a file with the same base name, and copied to the clipboard. Paste and rewrite never run. When one tray selection holds several files, each keeps its own prompt context and its own output beside it.
_Avoid_ : import (nothing enters a library).

**File transcription batch** :
A tray selection treated as one indivisible unit of work. Its file transcriptions keep the order of the selection and share one engine lifecycle. A later selection is another batch and never joins the active one.
_Avoid_ : file transcription (one file's run), import.

**Recording** :
The capture phase of dictation, while the microphone records, and also the HUD state of the same name. It is only one part of the path, not dictation as a whole.

**Import** :
In ordinary use, bringing a file into an application's library. File transcription keeps nothing in a library. It reads a file and writes its text beside it.

## Fidelity criteria

The T1 canonical mode, `apply_transcription_request`, is the production transcription path that Deckle exposes. Its output lands in the clipboard for immediate use. The usage criterion behind it is confidence at high volume. Louis must be able to dictate for twenty minutes and trust the output without re-reading every line.

The criteria below come from observed failure modes. Each names a class of deviation that separates an acceptable transcription from a dangerous one.

**Grammatical number fidelity** :
A class of deviation in which a singular becomes a plural, or the reverse. It can flip the scope of an instruction without looking like an error. On audio `701ce47a`, « le contexte minimal 8K » became « les contextes minimaux 8K », and a targeted instruction turned into a global one. On T1 it is a high-severity defect, even when the rest of the sentence is correct.

**Plausible reformulation** :
A class of deviation in which a transcription error hides behind a substitute that makes sense, so the original mishearing disappears. On audio `701ce47a`, « à côté » was misheard as « en côté », an expression that does not exist, then rewritten as « en même temps », a synonym that fits the context but not the audio. It is worse than a visible error, because re-reading does not catch it.

**Manifest vs invisible error** :
A distinction that follows from plausible reformulation. A manifest error, such as a typo, a non-word or broken syntax, shows itself to the reader. An invisible error, such as a swapped synonym, a flipped number, a shift of register or a swapped name, passes the eye and lands in production. Deckle's fidelity criteria put visibility before local polish, because a polished output that is invisibly wrong is the worst outcome.

## Segmentation, detection vs cutting

Two devices carry the word "VAD" and are constantly conflated, yet they differ in kind. One is a model that finds speech in a finished buffer. The other is a threshold that cuts a live stream. They also produce two different units of "cut", which must not be confused.

**Neural VAD** :
A device that uses a neural network to find the speech regions of a whole captured buffer and trim the silence before decoding. It is model-based and works after the fact, so it needs the full buffer in hand.
_Avoid_ : VAD (bare, ambiguous), Silero (vendor-specific, the term should outlive the model).

**Energy segmenter** :
A device that cuts the live capture into **utterances**, placing a boundary wherever the signal's RMS energy drops below a threshold. It is neither a model nor speech recognition. It reads energy dips to decide where to cut.
_Avoid_ : energy VAD (it does not detect voice, it cuts on silence), VAD.

**Utterance** :
A unit of cut that the energy segmenter emits, a span of speech bounded by detected silence or by the safety ceiling. It is the atomic audio chunk handed to the backend for one transcription call.
_Avoid_ : chunk, segment.

**Segment** :
A unit of cut that Whisper produces as *output*, a short timestamped span the model derives **inside** each utterance from its own decoding. An utterance is an input cut decided by energy, and a segment is an output cut decided by Whisper. One utterance contains one or more segments.
_Avoid_ : utterance (the input unit), window (the 30 s encoder input).

**VAD** :
Short for voice-activity detection, a generic name for anything that finds speech in audio. Used bare it is ambiguous in Deckle, which has both a neural VAD and an energy segmenter.

**Silero** :
A third-party voice-activity detection model. It is the model behind the neural VAD today, not the name of the concept.

**Energy VAD** :
A voice-activity detector based on signal energy. Deckle's energy-based device detects no voice and only cuts on silence, so it is the energy segmenter.

**Chunk** :
In ordinary use, any piece cut from a larger whole. It is too vague for the input unit of transcription, which is the utterance.

**Window** :
The fixed 30-second span of audio that Whisper's encoder takes as input. It is neither an utterance nor a segment.
