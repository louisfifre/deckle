# CLAUDE.md — Deckle.Vision

Module de capture écran et d'échantillonnage de frames pour le pipeline ambient lighting. Couvre `ScreenCaptureService` (boucle DXGI Output Duplication, threading worker, recovery sur tous les états transitoires que Windows envoie pendant une session), `FrameSampler` (mip chain + staging + readback GPU pour produire une grille de moyennes consommable par AmbientEngine), et les interop natifs sous `ScreenCaptureInterop`. Le module est seul propriétaire de l'objet `IDXGIOutputDuplication` ; il l'ouvre au `Start()`, le ré-ouvre silencieusement à chaque interruption transitoire, et ne le libère définitivement qu'au `Stop()` ou sur erreur device fatale.

## Pourquoi DXGI Output Duplication et pas Windows.Graphics.Capture

WGC est l'API moderne mais le système dessine une bordure jaune autour de la surface capturée. La seule façon de la désactiver est la capability MSIX `graphicsCaptureWithoutBorder`, qui n'est pas déclarable depuis une desktop app unpackaged. DXGI Output Duplication est l'API pré-WGC (Windows 8+), non soumise à la bordure, c'est ce qu'utilisent HyperHDR, OBS et NVIDIA ShadowPlay. Le rationale archi complet vit dans `docs/architecture--color-science-pipeline--0.1.md` axe 2 (le chantier de migration WGC → DXGI).

## Recovery — taxonomie HRESULT et doctrine retry

Toute session DXGI longue durée traverse des interruptions transitoires. Le projet aligne sur le pattern Hyperion.NG (`libsrc/grabber/dda/DDAGrabber.cpp`) : *retry forever on transient, surface Stopped only on truly fatal*. La distinction est portée par le HRESULT que renvoie `AcquireNextFrame` ou que lève `DuplicateOutput1`.

**Transitoires absorbés silencieusement.** `WAIT_TIMEOUT` (écran statique, normal — on continue). `ACCESS_LOST` (desktop switch, mode change, DWM on/off, fullscreen exclusive — on relâche la duplication et on recrée). `ACCESS_DENIED` et `SESSION_DISCONNECTED` (secure desktop : UAC, Win+L lock, screensaver password ; RDP disconnect ; switch user — même chemin que ACCESS_LOST, log `SecureDesktopRecovering` Verbose dédié pour distinguer la cause). `INVALID_CALL`, `NOT_CURRENTLY_AVAILABLE`, `UNSUPPORTED` (HDR toggle en transit, limite 4-duplications atteinte, mode 8bpp éphémère — tombent dans le bras générique avec backoff 500 ms et retry).

Le recreate de la duplication retient indéfiniment tant que le `CancellationToken` du `Stop()` n'a pas fire. `TryRecreateDuplication` tourne en boucle 2 s entre tentatives — quand `DuplicateOutput1` jette `COMException` (le secure desktop refuse l'accès à un process non-LOCAL_SYSTEM, le mode change n'a pas fini de propager, etc.), on log Warning et on retente. C'est ce qui permet à ambient de tenir sans intervention pendant un screensaver, un Win+L de plusieurs minutes, une commande Run as Administrator avec UAC ouvert. La précédente implémentation cassait au premier échec et faisait fire `Stopped` immédiatement — désormais `Stopped` ne fire que sur cancel ou sur erreur fatale.

**Fatales.** `DEVICE_REMOVED` et `DEVICE_HUNG` impliquent que le device D3D11 lui-même est mort (GPU débranché, driver crash, signal de mort du GPU). Le service log `DeviceLost` et break, ce qui fait fire `Stopped` à destination du consumer (`AmbientEngine.OnCaptureStopped`). Un rebuild complet du device exigerait de re-walker les adapters/outputs depuis zéro, ce qui sort du scope du service — c'est le consumer qui décide de reconstruire un nouveau `ScreenCaptureService` au prochain `StartAsync`.

## Threading

La boucle de capture tourne sur un Task dédié spinné dans `Start`. `FrameArrived` et `Stopped` sont raised depuis ce thread worker, jamais sur le thread du caller. Les consumers qui touchent à de l'UI marshallent eux-mêmes via `DispatcherQueue.TryEnqueue`. Le service ne sait rien du dispatcher de personne. La doctrine se prolonge dans `AmbientEngine.OnCaptureStopped` qui post `Stop()` sur le thread pool via `Task.Run` parce que `Stop()` raise `StateChanged` que les subscribers UI consomment.

## HDR et cadence

`DuplicateOutput1` négocie un format pixel depuis une priority list — FP16 scRGB préféré quand le display est en HDR, BGRA8 préféré sinon. Le format retenu est lu via `GetDuplicationDesc` et exposé comme `ActiveFormat` pour que `FrameSampler` choisisse sa passe tone-map. Peak luminance vient de `IDXGIOutput6::GetDesc1` à l'enumération adapter.

La cadence cible est ~15 Hz, alignée sur la cadence de push d'`AmbientEngine`. Plutôt que d'acquérir des frames que l'engine ne consommerait pas, on respecte la fenêtre 66 ms entre deux livraisons effectives — `AcquireNextFrame` continue de tourner avec timeout 200 ms pour rester responsive à la cancellation, mais on relâche les frames en GPU sans copier dans la grille consumer quand la fenêtre n'est pas écoulée.

## Observabilité

`DeckleVisionSource.Log` — provider `Deckle.Vision`, tag `VISION` en LogWindow. Cycle de vie session (`ScreenCaptureStarting` / `Started` / `Stopped` + détails Verbose). Anomalies de loop (`AccessLostRecovering`, `SecureDesktopRecovering`, `DeviceLost`, `AcquireFrameFailed`, `TextureQueryFailed`, `FrameConsumerThrew`, `ReleaseFrameNonZero`). Resilience recreate (`DuplicationRecreateAttemptFailed` Warning par tentative ratée, `DuplicationRecreated` Verbose au succès, `DuplicationResizeDetected` quand le mode display a changé pendant l'interruption). `FrameSampler` couvre `SamplerInitialized` + `SamplerMapFailed` + `SamplerProcessFailed`.

Les Verbose de la boucle sont gates par `AmbientCaptureGate` côté Diagnostics — quand l'utilisateur a `LogAmbientCaptureActivity` off, ces lignes sont filtrées avant insertion buffer LogWindow. Les Info et Warning passent toujours.
