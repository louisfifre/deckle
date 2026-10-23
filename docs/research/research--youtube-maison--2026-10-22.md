---
description: "Cadrage (2026-10-22) — un client YouTube maison comme module de Deckle : la nouvelle direction dictée par Louis, ce que le socle Deckle réutilise et ce qu'il n'a pas, l'état des options d'extraction et de lecture fin 2026, les questions de conception ouvertes, et ce qu'il reste à faire quand Louis en reparle."
---

# YouTube maison — un module de Deckle, à reprendre quand Louis en reparle

Commande de Louis (dictées du 2026-10-22) : refaire la « petite tâche » du YouTube maison, mais intégrée à Deckle au lieu d'un dépôt à part, pour réutiliser le cœur (composants, plomberie), sortable en stand-alone plus tard. Envie d'une interface originale : pas les panneaux latéraux ni le feed copié de YouTube, « je ne suis pas chez Google ». Prendre ce que Windows et WinUI offrent, bien intégré. Louis y réfléchit encore : cette note tient lieu de mémoire jusqu'à ce qu'il en reparle. Rien d'autre n'a été créé, ni code, ni chantier.

L'espace Anytype était injoignable à la clôture (le serveur MCP refuse toute requête, lecture comprise). L'idée existante « Client YouTube Windows en WinUI 3, open source, sans pub, à part de Deckle » dit encore « dépôt à part, rien de commun avec Deckle » : à corriger quand Louis confirme la nouvelle direction. Chaque point est marqué **établi**, **interprétation** ou **non vérifié**.

## 1. Ce que l'idée Anytype pose déjà

**Établi (lu dans l'idée).** Objectif : quitter YouTube, supprimer le compte, une app à soi sur Windows pour regarder sans pub. Abonnements locaux par flux RSS de chaîne (`youtube.com/feeds/videos.xml?channel_id=...`, quinze dernières vidéos, sans compte), playlists locales, pas d'historique, recommandations réglées à sa façon. Lecture directe sans pub par API non officielle, qui casse régulièrement : quand ça casse, lancer un agent de recherche, ne pas s'acharner à la main. WebView2 sur le lecteur YouTube exclu : les pubs restent. Découpage tenable : abonnements RSS, liste des dernières vidéos, lecteur, playlists locales ; recherche et recommandations ensuite.

## 2. Ce que Deckle apporte

**Non vérifié (inventaire par agent, à relire avant de coder).**

Réutilisable tel quel : le système de modules (`src/Deckle.Modules`, descripteur, présence, composition dans `App.Startup.Modules.cs`), les settings déclaratifs (`Deckle.Catalog/Composer`), le store JSON (`Deckle.Core/JsonSettingsStore.cs`), le téléchargeur avec reprise et vérification (`Deckle.Core/Io/Downloader.cs`), les hotkeys globales, le menu tray, les notifications, les cartes HUD, le journal EventSource. Gabarit de module complet : `src/Deckle.Input.PrecisionScroll`.

Utile pour un lecteur : le service d'avant-plan et plein écran (`Deckle.Shell.Foreground/ForegroundWatcher.cs`) pour savoir quand une autre app passe plein écran ; le scroll précision pour un feed qui glisse.

Absent, à construire : aucun lecteur vidéo, pas de WebView2, pas de contrôles média système, pas de parseur RSS, pas de cache HTTP, pas de base locale, pas de contrôle « carte vidéo / vignette ». Pas de jeton de fondu ou de durée nommé dans le design system, seulement `Spacing.cs`.

Piège de nom : `Deckle.Home` est l'espace maison Anytype, pas un hub d'interface. Il n'existe aucun lanceur de surfaces de modules : chaque module crée sa fenêtre et l'App la branche.

## 3. La stack, état fin 2026

**Non vérifié (recherche par agent, sources peu nombreuses, à confirmer au moment de faire).**

Extraction des flux. yt-dlp exige depuis fin 2025 un runtime JavaScript (Deno) plus une pièce « ejs », et YouTube impose des jetons PO sur la plupart de ses clients internes. YoutubeExplode, la bibliothèque .NET, contourne en changeant de client interne tous les quelques mois (android_vr, visionos), sans JS ni jeton ; elle casse donc périodiquement, et une mise à jour NuGet suffit. Limites connues : vidéos « pour enfants » injouables, parfois un seul format 360p. Piste retenue (interprétation) : YoutubeExplode d'abord, tout en .NET, yt-dlp embarqué en secours, les deux derrière une seule interface remplaçable.

Lecture. `MediaPlayerElement` lit un flux muxé (vidéo et audio ensemble), mais YouTube n'en donne plus qu'en 360p, parfois 720p. Pour mieux, il faut lire vidéo et audio séparés : `MediaPlayerElement` en DASH n'est pas prouvé ; libmpv (bindings .NET à vérifier) ou FlyleafLib (FFmpeg + Direct3D 11, publié 2025-12) le font nativement. AV1 et VP9 demandent les extensions du Store avec MediaPlayer ; FFmpeg et mpv embarquent leurs décodeurs.

Données. RSS par chaîne, établi. Instances publiques Invidious et Piped quasi mortes, à ne pas utiliser. SponsorBlock et DeArrow gratuits et ouverts, à mettre en cache. Return YouTube Dislike limité en débit et estimé.

Windows. Touches média et overlay de verrouillage viennent gratuitement avec `MediaPlayer.SystemMediaTransportControls` ; à câbler à la main avec mpv ou Flyleaf. Mini-fenêtre toujours visible via `AppWindow` en `CompactOverlay`, pour tout contenu.

Risque dominant. Si YouTube ferme les derniers clients sans jeton, toutes les voies sans compte et sans JS tombent ensemble. Quoi qu'on choisisse, l'extraction vit derrière une interface unique.

## 4. Questions de conception ouvertes

1. À l'ouverture : une liste de nouveautés, ou une file « à regarder » qu'on vide ? Les clients existants copient le feed YouTube ; une file qu'on termine est déjà autre chose.
2. Où vit la vidéo : fenêtre à part, overlay compact qui suit le travail, plein écran seul ? Ça décide la moitié de l'interface.
3. Quelle qualité suffit : 720p (MediaPlayerElement, simple) ou plus (FlyleafLib dès le départ) ?
4. Recommandations : aucune, ou seulement issues des chaînes et playlists ?
5. Stand-alone plus tard : un cœur sans dépendance à l'App de Deckle dès la conception, ou on se l'autorise après ?

## 5. Quand Louis en reparle

- Créer le chantier dans Anytype et corriger l'idée d'origine (« dépôt à part »).
- Prototyper la lecture d'un flux YouTube dans WinUI pour trancher la question 3 sur du concret.
- Relire l'inventaire du socle avant de poser le module (section 2 est un rapport d'agent).
