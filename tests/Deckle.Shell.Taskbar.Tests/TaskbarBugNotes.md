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
