---
description: "Recherche vérifiée (2026-10-08) — remplacer Windhawk par un module Deckle pour la barre des tâches : inventaire exact des sept mods installés et de leurs réglages, mécanique de Windhawk (injection, hooks MinHook, symboles PDB avec cache local), position native de la barre livrée par Microsoft en septembre 2026 mais probablement inactive sur cette machine, trois routes pour Deckle, et les tâches de recherche du chantier."
type: research-report
---

# Barre des tâches sans Windhawk — ce que Windhawk fait, ce que Windows fait désormais seul, par où commencer

Commande de Louis (dictée du 2026-10-08) : il veut se passer de Windhawk pour la barre des tâches, en reprenant dans Deckle ce que ses mods lui donnent (barre à gauche, plus petite, icônes seules, tray maîtrisé), avec un socle propre sur lequel ajouter des réglages plus tard. Il est sur Windows 11 IoT Enterprise LTSC 2024 et pense que ça limite l'obsolescence.

Méthode : inventaire de l'installation Windhawk locale (portable, scoop, `D:\bin\scoop\persist\windhawk`), lecture des sources des mods, calcul des clés du cache de symboles, lecture du registre Windows, puis trois passes de recherche déléguées (moteur Windhawk, paysage Windows 11 en 2026, mécanique des quatre mods de la barre). Chaque point est marqué **établi**, **interprétation** ou **non vérifié**.

## 1. Ce que tu as aujourd'hui, mod par mod

**Établi**, lu dans `Engine\Mods\*.ini`. Sept mods installés, tous de m417z, tous GPLv3, tous actifs. Windhawk 1.7.3, OS 10.0.26100.9457.

| Mod | Version | Ce qu'il fait chez toi |
|---|---|---|
| taskbar-vertical | 1.4.2 | Barre à gauche, largeur 44 px, conteneur horloge 80 px, jump lists et menu Démarrer alignés en haut, « natif si possible » activé |
| taskbar-icon-size | 1.3.10 | Hauteur 24, icônes 16, boutons 32 (défauts Windows 48 / 24 / 44) |
| taskbar-notification-icon-spacing | 1.4 | Icônes de tray 24 px (défaut 32), une rangée, overflow 32 px et 64 par rangée |
| taskbar-notification-icons-show-all | 1.1 | Toutes les icônes de tray visibles |
| taskbar-clock-customization | 1.8.1 | Jour en haut (Lun … Dim), heure « HH mm » en bas, 18 / 12 px, tooltip CPU, GPU, RAM, réseau, disque |
| windows-11-taskbar-styler | 1.10 | Thème SimplyTransparent plus un style perso |
| windows-11-start-menu-styler | 1.7 | Thème NoRecommendedSection plus un style perso |

Trois autres mods ont été installés puis retirés, leurs DLL compilées traînent encore dans `Engine\Mods\64` : taskbar-auto-hide-speed, taskbar-auto-hide-when-maximized, taskbar-tray-system-icon-tweaks. Le dernier est sans doute celui que tu cites pour les icônes système du tray.

Tes réglages Windows propres restent au défaut : barre en bas (`StuckRects3`), pas de masquage automatique, alignement à gauche, boutons jamais combinés avec étiquettes désactivées (`TaskbarGlomLevel=0`).

## 2. Comment Windhawk fait

**Injection (établi, blog de m417z).** Le moteur écrit sa DLL dans chaque processus par `CreateRemoteThread`, puis accroche `CreateProcessInternalW` pour attraper les nouveaux. Pas de pilote, pas de service, pas besoin d'être administrateur pour explorer.exe.

**Hooks (établi, blog).** MinHook, dans une variante maison. Les mods appellent `Wh_SetFunctionHook`.

**Symboles (établi).** Les fonctions visées ne sont pas exportées. Les mods les nomment par leur signature C++ et le moteur retrouve l'adresse dans le PDB de Microsoft, via le cache en ligne de Ramen Software d'abord, le serveur de symboles Microsoft ensuite. Windhawk garde ensuite un cache local, vérifié sur ta machine : `Engine\ModsWritable\<mod>.ini`, section `[SymbolCache]`, clé = GUID du PDB + module + `TimeDateStamp-SizeOfImage`, valeur = liste `symbole#offset`. Les trois binaires actuels (taskbar.dll, Taskbar.View.dll, SystemTray.dll) y sont couverts. Tes soucis ne viennent donc pas de symboles manquants.

**Licence (établi).** Moteur GPLv3 (dépôt ramensoftware/windhawk), les sept mods GPLv3. La licence de `windhawk_utils.h` n'a pas été trouvée.

**Sans interface (établi pour les drapeaux, non vérifié pour le reste).** `-tray-only`, `-exit`, `-restart`, `-safe-mode` existent. Aucune API ni CLI pour qu'une autre application installe ou règle un mod ; rien ne dit non plus que poser les fichiers `ModsSource\*.wh.cpp` et `Engine\Mods\*.ini` à la main suffise. Une DLL de mod compilée ne tourne pas sans le moteur, elle importe les fonctions `Wh_*`.

**Fragilité (établi, cache local).** Taskbar.View.dll a changé huit fois entre février et août 2026 sur ta machine, soit à peu près à chaque mise à jour cumulative. LTSC t'épargne les sauts de version annuels, pas les changements mensuels de la barre. L'hypothèse « LTSC donc moins d'obsolescence » tient pour les fonctions, pas pour les symboles.

**Ce que chaque mod touche (établi, lecture des sources).**

- taskbar-vertical : une quarantaine de hooks dans taskbar.dll, Taskbar.View.dll et SystemTray.dll, plus des hooks dans StartMenuExperienceHost, ShellExperienceHost et ShellHost pour déplacer le menu Démarrer et les panneaux rapides. Deux chemins : si Windows a la barre verticale native, le mod force seulement la position ; sinon il fait pivoter de 90° tout le contenu XAML de la barre puis contre-pivote chaque enfant un par un, et corrige à la main thumbnails, jump lists, flyouts et overflow du tray.
- taskbar-icon-size : une trentaine de hooks, dont plusieurs trouvés en désassemblant du code à la volée pour lire des offsets de champs. La hauteur passe par `TaskbarConfiguration::GetFrameSize`, la taille d'icône par `GetIconHeightInViewPixels`, la largeur de bouton par une réécriture de ressources XAML.
- taskbar-notification-icon-spacing : presque tout se fait en modifiant l'arbre XAML vivant (MinWidth, Padding, WrapGrid) ; les hooks ne servent que de déclencheurs.
- taskbar-notification-icons-show-all : aucun symbole. Il intercepte `RegGetValueW` et `RegSetValueExW` pour mentir sur la valeur `IsPromoted` des clés `HKCU\Control Panel\NotifyIconSettings\<id>`.

**Interprétation.** Tes symptômes (hauteur et tailles mal appliquées) collent au chemin « rotation » de taskbar-vertical combiné à taskbar-icon-size : les deux se disputent `GetFrameSize`, et le premier remesure la barre à chaque `MeasureOverride`. C'est la partie la plus hackée de tout l'ensemble.

## 3. Ce que Windows fait désormais seul

**Établi, notes Microsoft.** Microsoft a réintroduit une position native de la barre (bas, haut, gauche, droite) et une taille « Small » dans les canaux Insider en mai 2026 (Experimental 26300.8493), puis Beta en juillet (26220.9022) et 26H1 en septembre (KB5124006). Limites en position latérale : pas de masquage automatique, pas de barre tactile, pas de zone de recherche. La taille Small réduit les icônes partout mais la hauteur de la barre seulement en haut ou en bas.

**Établi par la presse seulement.** La mise à jour cumulative du 8 septembre 2026 (KB5124008, 26100.9445) l'apporte aux 24H2 et 25H2 stables, en déploiement progressif. La page Microsoft de la KB ne le dit pas.

**Établi sur ta machine.** Tu es en 26100.9457, mise à jour de septembre installée. Le code natif est présent : le cache de symboles du mod a résolu `wil::Feature<…59213768>::IsEnabled` et `TaskbarSettings::Location` dans ton taskbar.dll actuel. Mais ta position Windows reste « bas », aucune valeur `TaskbarLocation` n'existe dans `Explorer\Advanced` et aucun override de fonctionnalité n'est posé.

**Interprétation.** Le drapeau est éteint chez toi, donc taskbar-vertical tourne sur son propre chemin « rotation ». Si le drapeau s'allume, le mod bascule tout seul sur la barre native (réglage « natif si possible » déjà actif), et les deux mods les plus lourds deviennent presque inutiles.

**Non vérifié.** Que l'activation du drapeau marche sur IoT Enterprise LTSC. Le mod teste la fonctionnalité 59213768 ; la presse cite pour ViVeTool les identifiants 61161244, 61754985, puis 62353331, 62762248. Le seul juge est un essai sur ta machine, réversible, Windhawk arrêté.

**PowerToys (établi).** Le « dock » est une fonction de Command Palette (0.98, mars 2026 ; 0.101 ajoute masquage automatique et horloge). Il porte des commandes épinglées, pas les fenêtres ouvertes ni le tray. Ton impression est juste, ce n'est pas une barre des tâches.

**Autres voies (établi).** ExplorerPatcher (GPL-2) rend la barre Windows 10 et suit 26100 dans ses notes de version ; RetroBar (Apache-2) remplace la barre par la sienne sur n'importe quel bord. Aucune des deux ne garde la barre Windows 11 que tu veux. Microsoft ne documente ni API ni engagement sur Taskbar.View.dll : tout ce qui précède est non supporté.

## 4. Trois routes pour Deckle

Postures : product manager pour l'ordre, engineer pour la faisabilité.

**Route A, natif d'abord.** Allumer la fonctionnalité native, régler position gauche et taille Small, et voir ce qui reste. Si ça marche, position et taille deviennent des réglages Windows que Deckle n'a qu'à exposer. Ce qui n'est pas natif se traite sans injection : tray « toutes les icônes » par écriture directe de `IsPromoted` plus une veille des nouvelles icônes (non vérifié, le mod bloque aussi les réécritures de Windows), transparence par `SetWindowCompositionAttribute` sur la fenêtre de la barre comme le fait TranslucentTB (non vérifié sur 26100.9457), format de l'horloge par les formats régionaux courts de Windows (à tester, ça touche toutes les applications). Effort faible, zéro code natif, rien à maintenir à chaque mise à jour. Perte probable : la grille du tray, le tooltip de l'horloge, la largeur à 44 px, le style du menu Démarrer.

**Route B, Deckle pilote Windhawk.** Garder le moteur comme dépendance locale (portable, lancé en `-tray-only`), Deckle possède les sources des mods et leurs INI, et propose une page de réglages de niveau Microsoft. Tout ce que tu as aujourd'hui reste disponible. Mais Windhawk reste là, sans API connue, avec sa dépendance aux symboles Microsoft à chaque mise à jour. C'est une émancipation de l'interface, pas de l'outil.

**Route C, moteur maison.** Une DLL C++ injectée dans explorer.exe, un hooker (MinHook en BSD-2 ou Detours en MIT), une résolution de symboles (DbgHelp plus serveur Microsoft, ou table d'offsets par build grâce à LTSC), et un portage de la logique des mods. Les portages de code GPLv3 rendent ce composant GPLv3 alors que Deckle est MIT ; il faudrait l'isoler sous sa propre licence ou tout réécrire. L'effort est lourd : taskbar-vertical seul fait plus de 7 000 lignes, quatre processus et une quarantaine de hooks, et chaque mise à jour mensuelle peut en casser un. Fort apprentissage (PE, hooks, internes XAML), mais loin de la ligne WinUI 3 et C# du projet.

**Recommandation.** Commencer par la route A, en une heure, parce qu'elle peut faire disparaître les deux mods les plus fragiles sans écrire une ligne native. Décider ensuite, sur ce qui reste réellement manquant, entre B (pragmatique) et C (ambitieuse). Emplacement dans Deckle quand on code : un module domaine `Deckle.Shell.Taskbar`, frère de taskbar cover, avec sa page de réglages et sa persistance.

Point d'honnêteté : « me passer de Windhawk tout de suite » n'est vrai que par la route A, et seulement si le drapeau natif marche chez toi et si tu acceptes de perdre un temps la grille du tray, l'horloge personnalisée et le style du menu Démarrer, ou de garder Windhawk pour ces trois-là seulement.

## 5. Tâches de recherche du chantier

À créer dans l'espace Anytype (hors ligne pendant cette session, Deckle n'était pas lancé). Chaque tâche dit ce qu'elle doit trancher.

1. **Position native sur cette machine.** Windhawk arrêté, poser l'override de la fonctionnalité (59213768 d'abord, puis les identifiants de la presse), redémarrer explorer, vérifier que Paramètres propose gauche et Small. Noter ce qui casse (masquage auto, recherche) et revenir en arrière. Action système : Louis la fait ou donne son feu vert.
2. **Écarts entre natif et tes réglages.** Mesurer largeur, hauteur, taille d'icône et de bouton en natif gauche plus Small, les comparer à 44 / 24 / 16 / 32, et dire lesquels comptent vraiment.
3. **Tray « toutes les icônes » sans hook.** Écrire `IsPromoted=1` sur chaque sous-clé de `NotifyIconSettings`, vérifier la prise en compte, puis vérifier si Windows réécrit la valeur et comment attraper une nouvelle icône.
4. **Transparence sans injection.** Essayer `SetWindowCompositionAttribute` sur `Shell_TrayWnd` sur 26100.9457, comparer à SimplyTransparent, vérifier le comportement au survol et avec Mica.
5. **Horloge par formats régionaux.** Tester « HH mm » en format court et « ddd » en date courte, voir ce que la barre affiche et ce que ça change ailleurs.
6. **Menu Démarrer sans section Recommandé.** Vérifier si un réglage natif de 26100 fait disparaître la section, pas seulement la vider.
7. **Piloter Windhawk sans son interface.** Dans le source GPL (`storage_manager`, `mods_manager`), établir si poser sources et INI puis lancer `-tray-only` suffit, et comment le moteur détecte un changement de réglage.
8. **Coût réel d'un moteur maison.** Lire `src/windhawk/engine` (injection, `mods_api`), chiffrer un portage minimal de taskbar-notification-icon-spacing seul, et poser la question de licence du composant natif avant toute ligne.

## 6. Sources

- Moteur Windhawk : https://m417z.com/Implementing-Global-Injection-and-Hooking-in-Windows/ ; https://github.com/ramensoftware/windhawk ; https://github.com/ramensoftware/windhawk/wiki/Creating-a-new-mod ; https://github.com/ramensoftware/windhawk/wiki/Command-line-flags ; https://github.com/ramensoftware/windhawk/wiki/Troubleshooting ; cache en ligne https://ramensoftware.com/?p=2974 ; https://github.com/ramensoftware/windhawk-mod-symbol-cache ; incident symboles mai 2024 https://ramensoftware.com/?p=2912
- Mods (sources locales lues) : `D:\bin\scoop\persist\windhawk\AppData\ModsSource\*.wh.cpp` ; dépôt https://github.com/ramensoftware/windhawk-mods
- Barre native : https://learn.microsoft.com/en-us/windows-insider/release-notes/experimental/preview-build-26300-8493 ; https://blogs.windows.com/windows-insider/2026/05/15/improving-windows-quality-making-taskbar-and-start-more-personal/ ; https://support.microsoft.com/en-us/servicing/os/windows-11/2026/09/kb5124006-windows-11-26h1-update ; https://support.microsoft.com/en-us/servicing/os/windows-11/2026/09/kb5124008-windows-11-24h2-25h2-security-update ; presse https://www.windowslatest.com/2026/09/09/windows-11-finally-restores-a-windows-10-feature-after-five-years-the-movable-taskbar/ ; LTSC https://learn.microsoft.com/en-us/windows/whats-new/ltsc/whats-new-windows-11-2024
- PowerToys : https://devblogs.microsoft.com/commandline/powertoys-0-98-is-here-new-keyboard-manager-ux-the-command-palette-dock-and-better-cursorwrap/ ; https://github.com/microsoft/PowerToys/releases
- Autres voies : https://github.com/valinet/ExplorerPatcher ; https://github.com/dremin/RetroBar
