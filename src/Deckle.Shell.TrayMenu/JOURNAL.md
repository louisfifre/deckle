---
name: journal-shell-traymenu
description: "Journal daté du module Deckle.Shell.TrayMenu — diagnostics en cours, observations marquées comme hypothèses, learnings du workstream tray menu WinUI 3. Complément réversible au CLAUDE.md du module."
type: module-journal
---

# Journal — Deckle.Shell.TrayMenu

Chronique datée du module, complément réversible au [CLAUDE.md](./CLAUDE.md) qui reste timeless. Accueille les observations factuelles, les hypothèses non validées, et les learnings du workstream tray menu. Quand une entrée stabilise — hypothèse confirmée par mesure, doctrine acquise, décision tranchée — elle monte vers le `CLAUDE.md` ou un ADR et l'entrée devient un pointeur. Entrées récentes en haut.

---

## 2026-05-27 (suite) — Bug résiduel : scroll au premier clic après changement de focus, et piste de refonte design

Fin de session — le fix `PaddingSizeStates` + cache `_primedSizes` posté plus tôt (entrée précédente) tient sur 90 % des ouvertures. Un cas marginal subsiste : au premier clic après un changement d'application active (perte de focus de la fenêtre porteuse pendant le `SW_HIDE`, puis bascule vers une autre app, puis retour au tray), le menu se rouvre **avec scroll** — items rendus en `DefaultPadding` (40 DIP) alors que la fenêtre porteuse est dimensionnée à 32 DIP/item, donc le contenu dépasse et le `MenuFlyoutPresenter` active son ScrollViewer interne. Aux clics suivants dans la même session, le scroll disparaît et tout s'aligne sur 32 DIP comme attendu.

Hypothèse de cause non confirmée : le handler `OnFlyoutOpened` qui force `VisualStateManager.GoToState(item, "NarrowPadding")` à chaque ouverture ne tient pas dans ce timing — possiblement parce que le framework attache les items au visual tree *après* l'event `Opened`, ou parce que le rendu initial du popup a déjà commencé en `DefaultPadding` avant que le `GoToState` ait le temps de prendre. Pas reproductible à la demande dans la session — Louis a observé le bug se produire une fois en début de session post-fix sans pouvoir le déclencher fiablement ensuite. Test outillé difficile (cf. réflexion TDD plus bas).

**Décision tranchée pour cette session.** On garde le fix actuel, on accepte le scroll occasionnel comme bug résiduel non bloquant, on commit/push pour libérer le worktree (16k lignes en attente côté benchmark + skills). Le scroll est moins dérangeant que le déséquilibre Ambient vs natifs qu'on a résolu — on n'est plus dans le bug visuel structurel, on est dans un cas limite de timing du framework.

**Piste de refonte design à reconsidérer.** Observation Win11 de Louis sur les tray menus natifs (Sound, Defender, Date/Time, Network, Volume) : tous réservent une **colonne icône à gauche** pour chaque item, avec icône explicite (gear, shield, wrench) ou slot vide. Le décalage du texte vers la droite est le pattern natif, pas un défaut à éviter. Le raisonnement initial du CLAUDE.md (« éviter `ToggleMenuFlyoutItem` parce qu'il décalerait les autres items ») allait à l'inverse du pattern Win11.

Refonte possible — entièrement à reconsidérer avant engagement :

- Remplacer `ToggleSwitchMenuItemStyle` (template custom) par un `ToggleMenuFlyoutItem` natif pour Ambient. Hérite gratuitement du `PaddingSizeStates` natif, le bug de scroll devient impossible.
- Donner une `Icon` (`SymbolIcon` ou `FontIcon`) à chaque item du menu — Logs (DocumentApprove ?), Settings (Setting), Playground (Play), Restart (Refresh), Quit (Cancel). Aligné avec Defender/Network/Sound.
- Le `MenuFlyoutPresenter` réserverait alors la colonne icône partout (parce qu'au moins un item togglable + au moins une `Icon` présente), sans décalage erratique.

Réserve UX sur cette voie : la case à cocher Win11 par défaut est invisible quand non cochée — aucun feedback visuel « il y a un toggle ici, vous pouvez le changer ». C'est la raison historique de la pillule custom (Ambient = item visité fréquemment, intérêt à voir l'état d'un coup d'œil). Possibles compensations : accent visuel sur l'item togglable via theme resource (background distinct, icône spécifique on/off), ou réintégration partielle d'un visuel switch dans la colonne icône native (au prix d'une slot icône non standard). À designer si la voie est engagée.

Pas engagé maintenant — Louis ne veut pas relancer un cycle de design en fin de session. Inscrit ici pour reprise éventuelle. Si la refonte est retenue, elle remplacerait l'entier du paragraphe « Ambient Light — pillule custom » du `CLAUDE.md` du module et probablement aussi le `ToggleSwitchMenuItemStyle` + `TraySwitchMenuItem`.

**Note méthodologique sur le testing.** Discussion TDD ouverte en cours de session (skill `tdd` invoquée). Constat : ce bug-ci n'est pas un candidat TDD valable — la cause est dans le framework WinUI (timing d'application du `VisualState` selon le focus de la fenêtre porteuse), pas dans notre code-behind. Un test xUnit classique ne peut pas simuler un changement de focus OS qui déclenche le bug interne au framework. Le bon outil pour ce type de bug intermittent serait un test d'intégration UI (WinAppDriver ou équivalent), infrastructure très lourde non justifiée pour un menu tray. TDD reste pertinent et à introduire progressivement sur les modules métier purs (`Deckle.Transcription` state machine, `Deckle.Lighting` color science, `Deckle.Core` paths), pas sur le périmètre UI du tray menu.

---

## 2026-05-27 — Tray menu : déséquilibre visuel Ambient vs natifs, cause `PaddingSizeStates` ignoré par le template custom, fix en alignement

Bug observé en fin de workstream sur le menu tray, branche `fix/tray-menu-measure-flyout` (worktree `tray-menu-winui3`). Premier diagnostic posé sur une fausse piste — corrigé après vérification par instrumentation et lecture du `generic.xaml` du WindowsAppSDK. Itinéraire conservé dans cette entrée pour valeur méthodologique.

**Observation initiale.** Les `MenuFlyoutItem` natifs (Logs, Settings, Playground, Restart, Quit) avaient une `DesiredSize.Height` qui basculait entre `40` et `32` DIP au fil des ouvertures du menu, alors que l'item Ambient Light (template custom `ToggleSwitchMenuItemStyle`) restait figé à `40.8`. Visuellement, deux captures successives montraient le même menu rendu à deux densités différentes : aérée à la première ouverture, compacte aux suivantes, avec l'Ambient toujours à 40 → déséquilibre avec les natifs passés à 32.

**Première hypothèse (fausse piste).** Le `Measure()` appelé dans [`MeasureFlyout()`](./TrayContextMenuHost.cs#L446) tournait sur des items détachés du visual tree (`has_visual_parent = false` confirmé sur 9 shows successifs par l'event `ItemAttachmentChecked`), et la `DesiredSize` cachée par WinUI tombait à la `MinHeight` native après un seuil de cache aléatoire. Fix posé : capturer les `DesiredSize` items attachés pendant le prime cycle dans un dictionnaire `_primedSizes`, et lire ce cache dans `MeasureFlyout()` au lieu d'appeler `item.Measure()`. Mesure code-behind effectivement stabilisée à 40 sur 14 ouvertures, fenêtre porteuse calculée à 237×318 px constants. **Mais le bug visuel persistait.**

**Lecture du source canonique qui dégage la vraie cause.** Inspection du `generic.xaml` du WindowsAppSDK 1.8.260224000 (chemin `~/.nuget/packages/microsoft.windowsappsdk.winui/.../Microsoft.UI/Themes/generic.xaml`, lignes 24058-24069). Le `DefaultMenuFlyoutItemStyle` natif porte un `VisualStateGroup x:Name="PaddingSizeStates"` avec deux states :

- `DefaultPadding` (vide) → padding `MenuFlyoutItemThemePadding = 11,9,11,10`, vertical 19 DIP, cellule ≈ 40 DIP.
- `NarrowPadding` → padding `MenuFlyoutItemThemePaddingNarrow = 11,4,11,7`, vertical 11 DIP, cellule ≈ 32 DIP.

Commentaire explicite dans le source : `Narrow padding is only applied when flyout was invoked with pen, mouse or keyboard. Default padding is applied for all other cases including touch.` Le framework pilote le state automatiquement — la première ouverture est sur `DefaultPadding` (état initial), dès qu'un pointer mouse/keyboard interagit avec le menu le state bascule à `NarrowPadding`, et le menu étant réutilisé entre ouvertures (instance unique), le state ne se réinitialise plus jamais.

**Vraie cause du déséquilibre visuel.** Le template `ToggleSwitchMenuItemStyle` introduit par les commits WIP `a697fa0` + `a6193c4` (post-rebase `003d4e7` + `8fa649c`) — initiative LLM des sessions précédentes, jamais validée explicitement par Louis — a réécrit complètement le `ControlTemplate` du `MenuFlyoutItem` mais **n'a pas reproduit le `VisualStateGroup PaddingSizeStates`** du natif. L'Ambient reste donc figé sur le `Padding` initial (équivalent `DefaultPadding`) pendant que les natifs basculent vers `NarrowPadding`. Mon premier fix (cache `_primedSizes`) résolvait l'instabilité de la mesure code-behind mais n'avait aucune incidence sur le rendu visuel — la fenêtre porteuse layered alpha=0 n'influe pas sur le popup interne du `MenuFlyout`, qui se rend selon ses propres VisualStates.

**Voie du fix corrigée.** Ajouter le `VisualStateGroup PaddingSizeStates` dans le template `ToggleSwitchMenuItemStyle` (identique au natif, mêmes setters sur `LayoutRoot.Padding` via le theme resource `MenuFlyoutItemThemePaddingNarrow`). Le framework pilotera le state automatiquement sur l'Ambient comme sur les natifs. Conséquence : l'Ambient bascule lui aussi à 32 DIP en mode mouse, aligné sur les natifs Win11 — cohérent avec la doctrine projet « primitive native d'abord ». Densité narrow (32 DIP) retenue comme cible parce que c'est le rendu Win11 natif en interaction mouse — Settings, Explorer et les context menus système suivent ce pattern.

**Ajustement de la pillule.** Cellule narrow totale 32 DIP = margin 4 + padding 4+7 + zone utile 17 DIP. La pillule custom actuelle (wrapper Grid `Width=40 Height=20`, rail `Border 40×20`, knob `Ellipse 12×12`) fait dépasser de 3 DIP, et la cellule s'agrandirait à ~35 pour l'Ambient seul → déséquilibre. Pillule réduite à wrapper `Width=36 Height=16`, rail `Border 36×16` (CornerRadius=8), knob `Ellipse 10×10`, margins on/off ajustées de 4 à 3. Tient dans 17 DIP utiles, reste reconnaissable comme switch, légèrement plus serré que le `ToggleSwitch` natif (40×20/knob 12×12) ce qui est cohérent avec la densité narrow.

**Statut du fix `_primedSizes` du premier diagnostic.** Maintenu en place — il stabilise effectivement la mesure code-behind utilisée pour dimensionner la fenêtre porteuse, ce qui rend la position du popup déterministe. Avec le fix `PaddingSizeStates` posé en parallèle, le cache capture à 40 DIP/item (état `DefaultPadding` au prime cycle), mais le popup interne se rend à 32 DIP dès la première interaction mouse. La fenêtre porteuse (invisible) sera légèrement surdimensionnée d'environ 60 px verticaux, sans impact visuel. Si le décalage de position calculée par `CalculatePopupWindowPosition` s'avère visible à l'œil, on capturera le state `NarrowPadding` post-prime via `VisualStateManager.GoToState` programmé avant la capture.

**Leçons méthodologiques.**

- Lire la source canonique avant d'instrumenter : si j'avais ouvert `generic.xaml` du WindowsAppSDK au début, j'aurais vu le `VisualStateGroup PaddingSizeStates` du natif et identifié le déséquilibre du template custom en quelques minutes, sans poser deux ronds d'instrumentation. C'est exactement la doctrine « Official sources first on a moving tech » du `CLAUDE.md` racine, appliquée trop tard.
- L'instrumentation `ItemAttachmentChecked` a confirmé un fait vrai (items détachés) mais déconnecté de la cause du bug visuel. Un fait confirmé n'est pas un diagnostic — il faut aussi vérifier que le fait explique le symptôme observé.
- Une initiative LLM ancienne (template custom écrit sans reproduire le `PaddingSizeStates` natif) peut survivre plusieurs sessions sans être interrogée tant que le symptôme n'oblige pas à lire le source. Vigilance accrue quand un template natif est réécrit — vérifier exhaustivement les `VisualStateGroups` reproduits.

Promotion prévue après validation visuelle par Louis. Le paragraphe « Ambient Light — pillule custom dessinée à la main » du [CLAUDE.md du module](./CLAUDE.md) sera mis à jour pour expliciter le `PaddingSizeStates` et l'alignement Win11 densité. L'event `ItemAttachmentChecked` peut soit rester actif comme garde-fou (suivi de l'attachement post-fix), soit être retiré.
