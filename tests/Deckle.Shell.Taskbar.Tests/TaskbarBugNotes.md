# Taskbar bug notes

## The full-height Start menu lost its bottom row to a layout clip

- Trigger: « Full-height Start menu » on, on Windows 11 26100 with the
  redesigned Start menu, a left taskbar, 3840x2160 at 200 %.
- Symptom: the Start menu filled the screen but its account and power row was
  cut in half at the bottom. The engine's own log and a visual-tree dump both
  reported FrameRoot at the full 1080 DIP, so the first verification, made on
  the tree only, passed.
- Cause: FrameRoot's height was computed as window - 2 * top margin with the
  native bottom margin (36 DIP) left in place. Its layout slot was therefore
  the window minus 36 DIP, and XAML clips an element whose own Height exceeds
  its slot to that slot. Observed in the log (`frame_slot`, `margin_bottom`) and
  on a screen capture; the clipping rule is XAML's documented layout behavior.
- Violated invariant: an element held at a fixed Height must fit its layout slot
  with its margins, or the screen shows less than the tree reports. The hold
  now places FrameRoot top-aligned with no bottom margin, measures the live
  layout and reads its placement back (see the header comment of
  `native/Deckle.Shell.Taskbar.Native/xamldiag/behaviors/start_menu_height.h`).
- Recurrence cue: an element the tree reports at the wanted size while a screen
  capture shows it cut; a `start_menu_fit` warning with `clipped=yes` in the log.
  A second trap met on the way: measured on the closed menu, the panel's
  distance to the window edge reads far off screen (-906), because the panel is
  moved away for its opening animation; the shadow inset is the stable gap.
- Guarded by: no automated test. The behavior lives in the DLL injected into
  StartMenuExperienceHost and needs the live Start menu; the guard is the
  engine's `start_menu_fit` read-back at run time, and the check is a screen
  capture with the harness (`--target startmenu`). Verified on 2026-11-09 at
  200 % (12 DIP above and below, measured in pixels) and by the maintainer at
  150 %.

## A larger thumbnail was cut on the right

- Trigger: « Larger thumbnails » on, on Windows 11 26100 (Taskbar.View.dll
  2608.26001.200.0), with a box wider than Explorer's own 200 px; seen at
  650 x 330 on 2026-11-11 (v0.118.1).
- Symptom: the hover thumbnail grew to the box's height, but its frame stayed
  about as wide as Explorer's own and the window's picture was cut on the right.
- Cause: the size function Deckle leads to its own box
  (`ThumbnailHelpers::GetScaledThumbnailSize`) sizes the picture only. Each
  thumbnail sits in a `Taskbar.TaskItemThumbnailView`, whose `MaxWidth` Explorer
  sets when it applies the view's template. The cap was not read on the live
  tree: it is inferred from the Windhawk thumbnail mod, which lifts the same
  property in `OnApplyTemplate`, and confirmed by the outcome, the maintainer
  seeing whole thumbnails once the rule `thumbnails.view.maxWidth` held it open
  (v0.118.2). The cap's exact value is not known.
- Violated invariant: a size Explorer computes in a function is also bounded by
  the element that shows it; growing one without the other shows less than was
  asked. The rule and its reason are in `TaskbarDocumentBuilder.Rules`.
- Recurrence cue: a thumbnail that reaches one side of the box and is cut on
  the other, with `thumbnail_box_applied` in the log and no thumbnails problem
  on the page; after a Windows update, the rule reported `contested` or `error`.
- Guarded by: `ThumbnailBoxTests.TheViewsOwnWidthCapIsLiftedOnlyWhileTheThumbnailsGrow`
  and `TheWidthRuleFindingNoViewSaysNothingButItsFailingDoes`, which hold the
  rule in the document and its word on the page. That the rule lifts the cap in
  Explorer has no automated test: it needs the live hover flyout, and the check
  is a look at a wide window's thumbnail.
