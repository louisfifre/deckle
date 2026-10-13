namespace Deckle.Composition;

// The HUD picks one per state. Transcribing ↔ Rewriting blends live on one
// stroke; crossing Recording ↔ Transcribing/Rewriting rebuilds the stroke,
// because the frozen-rotation and spinning pipelines cannot share a
// SpriteVisual (HudChrono.AttachProcessingVisual).
public enum ProcessingVariant { Recording, Transcribing, Rewriting }
