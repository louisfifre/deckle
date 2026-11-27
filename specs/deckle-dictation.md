# Speak as long as you want, over everything, and get back a well-framed text

## Need

### Context of use

1. A person thinks aloud, sometimes for more than an hour. They talk about what passes before their eyes, one subject then another, often while doing something else. They are at their computer, or elsewhere with their phone. What they say belongs to no application.

### User need

1. The user needs to start the listening and stop attending to it, in order to get back everything they said in a well-framed text that they put wherever they want.

### Intended outcome

1. One gesture starts the listening from anywhere, and the same one stops it.
2. While they speak, nothing asks for their attention and nothing rushes them.
3. They are never cut off: the listening goes on through a hesitation or a silence, however long.
4. Only their voice counts: a rub or a noise around them does not pass for speech.
5. An hour holds up as well as two minutes, and nothing of what they said is lost.
6. They change subject, pause, resume: the change of subject is marked automatically or by one gesture of theirs, and the text keeps these separations.
7. To find their thread again, their last words read at a glance, without hiding what they are looking at.
8. The final text says what they said, and nothing they did not say.
9. The experience is the same on the computer and on the phone.

## Journey

| The user wants to | The product must |
| --- | --- |
| start speaking from wherever they are, without leaving what they do | start the dictation on one gesture, the same across the system, and leave them where they were |
| think aloud for as long as it takes, often doing something else | keep listening without asking for anything, without cutting them on a pause or a hesitation, and take only their voice |
| find their thread again | show their last words at a glance, out of the way of what they are looking at |
| change subject, pause, resume | mark the change of subject automatically or on one gesture of theirs, and keep these separations in the text |
| stop and use what they said | stop on the same gesture, and have the whole text ready, framed and saying what they said, for them to put where they want |
| choose once how their dictations are rewritten | apply that choice to every dictation, without asking again |

Thinking aloud, finding the thread and changing subject happen together, while the dictation runs.

## Rules

1. Whatever stops a dictation, what was said in it is found again, and the product says so when it gives it back, and when only part of it came back.
2. The text says only what the user said: a rewrite or a translation may change the words, and the intention passes whole. A notion said twice may come into one place; none is dropped, and none is added.
3. What the product shows of the dictation is its real state: it says it listens only while it listens.
4. Everything the product tells the user reaches them with animations off as well.

## Workflow

### Starting

When it goes well:

1. From wherever they are, one gesture starts a dictation.
2. Nothing is taken from what they were doing: they go on with it.
3. The first words they say after the gesture are in the text: they wait for no signal to speak.
4. The product shows them that it listens.

Otherwise:

5. Nothing on the machine can transcribe speech: no dictation starts, and the product tells them what is missing and where to get it.
6. No microphone can be used: no dictation starts, and the product tells them so.

### Speaking

When it goes well:

1. The dictation goes on until they stop it, however long they are silent: nothing counts down, and they never make a sound to keep it going.
2. A hesitation, a stammer or a pause in the middle of a sentence never splits the sentence in the text.
3. Only their voice adds text: no other sound around them does.
4. Nothing the product shows presses them or asks for a gesture.
5. An hour goes as two minutes: the same gestures, nothing slows down.

Otherwise:

6. The product hears no voice from them in the first seconds of the dictation: it tells them once, and the dictation goes on.

### Finding the thread

When it goes well:

1. A glance is enough to read the last words they said: the newest words are always in view, and the older ones make room for them.
2. What shows their words never hides what they are looking at: as they move toward it, what lies under it can be read and used.
3. When their text is rewritten while they speak, what they are reading does not move.

Otherwise:

4. A rewrite fails: they keep the text they were reading.

### Changing subject

When it goes well:

1. They change subject, pause and resume without leaving what they are doing.
2. The change of subject is marked automatically, or by one gesture of theirs.
3. The text keeps each subject apart.

### Stopping

When it goes well:

1. The same gesture stops the dictation at once: the last words said before it are in the text, and nothing said after it is.
2. The whole text, rewritten when they chose it, is ready for them to put where they want, and the product tells them so. The product puts it nowhere by itself.

Otherwise:

3. Nothing was said: nothing is made ready, and whatever they had ready to put elsewhere before stays ready.
4. Part of what they said was recorded but could not be transcribed: the rest is ready, and the product tells them a part is missing.
5. A rewrite fails: they still get their whole text, without that rewrite, and the product tells them.
6. The text cannot be made ready: the product tells them, and the text is not lost.

### Choosing the rewriting

1. The user chooses once whether their dictations are rewritten, and how, by their length. The choice holds for every dictation until they change it.

## How Deckle answers, a sample on two lines

### 1.3.3 They are never cut off

1. The listening has no end of its own: `CaptureSettings.MaxRecordingDurationSeconds` is zero by default, which means no limit, and the capture runs until the gesture stops it.
2. The `EnergySegmenter` cuts the audio into utterances (`Utterance`) on the silences, to transcribe it piece by piece: a cut ends an utterance, never the listening.
3. A pause inside a sentence does not cut it: the segmenter ends an utterance only after a silence longer than its hangover, and the hangover shrinks as the utterance grows, so a long utterance ends on a short pause, never in the middle of a word.
4. Where Deckle does not answer yet: the user can set a maximum length on the Dictation page (`RecordingLimit`), and the capture ends when it is reached (`CaptureOutcome.CapHit`).
5. To check against 4.2.1: the HUD shows a gauge of the silence before each cut (`UtteranceCountdownVisual`); it counts toward the cut of an utterance, not toward the end of the listening.

### 1.3.5 Nothing of what they said is lost

1. While they speak, the `TakeAudioRecorder` writes the audio of the dictation to disk, sealed, block by block, through the `TakeAudioRecoveryStore`, and the capture never waits for the disk.
2. At every utterance, the `DictationRecoveryStore` rewrites the text transcribed so far, sealed for the user's account.
3. Both files are deleted once the text is delivered or the dictation is discarded, so they remain only when a dictation ended before its text was delivered.
4. At the next start, `App.Startup.DictationRecovery` finds them: the audio is transcribed again through `TakeAudioRecovery`, the text copy stands in when the audio cannot give its text, and the product tells the user what came back.
