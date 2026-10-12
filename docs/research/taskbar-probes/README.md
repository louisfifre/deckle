# Sondes du chantier « Barre des tâches sans Windhawk »

Scripts d'exploration écrits le 2026-10-08 pour la fiche `../research--taskbar-sans-windhawk--2026-10-08.md`. Ils lisent et mesurent la barre native ; aucun ne touche au code de Deckle. Ils se lancent avec `pwsh -File <script>` depuis n'importe quel dossier.

| Script | Ce qu'il fait | Écrit quelque chose ? |
|---|---|---|
| `feature-flags.ps1` | Lit (`-Query`) ou active (`-Enable`) les drapeaux de fonctionnalité Windows par `RtlQueryFeatureConfiguration` et `RtlSetFeatureConfigurations`, structure et énumérations reprises de ViVe (GPL-3). `-Enable` et `-Reset` n'écrivent que le store Runtime, un redémarrage de Windows efface tout. | Oui avec `-Enable` ou `-Reset`, sans doute en administrateur |
| `taskbar-probe.ps1` | Rectangle de `Shell_TrayWnd`, valeurs `Taskbar*` de `Explorer\Advanced`, bord de `StuckRects3`, modules Windhawk chargés dans explorer. La capture d'écran échoue hors session interactive, c'est attendu. | Non |
| `tray-uia.ps1` | Liste par UI Automation les boutons de la barre (tâches, icônes de tray, horloge) avec leur position et leur taille. | Non |
| `taskbar-accent.ps1` | Pose un accent DWM sur la fenêtre de la barre (`clear`, `blur`, `acrylic`, `normal`) comme TranslucentTB, sans injection. Disparaît au redémarrage d'explorer. | Oui, cosmétique |
| `native-test.ps1` | Le test de la route A une fois les drapeaux actifs : pose `TaskbarLocation=0` et `TaskbarSize=0`, arme les sondes tray et horloge, redémarre explorer, mesure, puis restaure les sondes. | Oui, dans HKCU |
| `explorer-restart.ps1` | Redémarre explorer et mesure. | Non, hors redémarrage |
| `taskbar-strings.ps1` | Extrait des binaires de la barre les noms de valeurs de registre `Taskbar*`. | Non |
| `logon-capture.ps1` | À lancer juste après l'ouverture de session, avant de toucher à quoi que ce soit : ajoute dans `%LOCALAPPDATA%\deckle-taskbar-logon.log` des instantanés UIA des boutons de la barre à 0, 10, 20, 40, 60 et 120 s, avec l'heure de démarrage d'explorer et de Windhawk. Sert à dater le moment où les épinglés décrochent des lancés. | Non, hors le fichier journal |
| `late-inject-repro.ps1` | Reproduit l'injection tardive sans fermer la session : arrête Windhawk, mesure la barre d'origine, relance Windhawk en `-tray-only` dans l'explorer déjà construit, mesure. Le 2026-10-12 ce scénario n'a pas reproduit le défaut des épinglés. | Oui, arrête et relance Windhawk |
| `pe-keys2.ps1` | Calcule la clé `TimeDateStamp-SizeOfImage` des binaires et vérifie leur présence dans le cache de symboles de Windhawk. | Non |

Les identifiants de drapeaux utilisés : 59213768 (celui que teste le mod taskbar-vertical), 61161244, 61754985, 62353331, 62762248 (cités par la presse pour la position native), 29785184 (icônes dynamiques, déjà actif ici).
