# Deckle — Contexte

Glossaire des termes du projet Deckle. Définit le vocabulaire partagé entre Louis et les agents LLM qui interviennent sur le code. Ce fichier capture les distinctions qui ont une réalité concrète chez Deckle ; les concepts génériques de programmation n'y figurent pas, sauf quand Deckle leur donne une nuance interne propre.

## Testing — strates et catégories

Quatre catégories sont dans le périmètre des tests automatiques, lançables par un agent ou par Louis via `dotnet test` sans interaction humaine. Deux catégories sont hors périmètre automatique : elles existent et sont utiles, mais s'exécutent à la main avec le skill `verify`.

### Dans le périmètre automatique

**unit** :
Test qui exerce un type ou une fonction en isolation, sans toucher au système de fichiers, au réseau, à un thread UI, ni à une dépendance native. Cible naturelle : les modules-feuilles purs comme `Deckle.Composition` (ColorSpace, easing, animateurs), `Deckle.Chrono` (ChronoFormatter), et la logique pure de `Deckle.Core`. C'est la strate la plus volumineuse et la plus rapide.

**integration** :
Test qui exerce une frontière avec un service local mockable. Le partenaire est simulé par un substitut léger contrôlé par le test (serveur HTTP de test pour Ollama, file system temporaire pour `JsonSettingsStore`, simulateur de source audio pour la fonction qui appelle le micro). Le seam d'isolation doit être *naturel* — déjà présent dans l'architecture ou évident sans contorsion. Un seam parasite créé uniquement pour le test relève de la dérive « code testable mais inutilisable » et n'est pas accepté.
_Avoid_ : end-to-end, e2e (recouvrent des choses différentes ailleurs).

**observability** :
Test qui exerce une séquence d'événements EventSource via un `TestEventListener` interne. Vérifie que le code émet les bons providers, les bons noms d'event, les bons niveaux et keywords, et porte les payloads attendus. Catégorie native à Deckle vu le poids de la pipeline EventSource (voir `src/Deckle.Diagnostics/CLAUDE.md`).
_Avoid_ : log assertion, telemetry test.

**regression** :
Test ajouté en réaction à un bug spécifique déjà corrigé. Reproduit les conditions du bug ; passe parce que la fix tient ; échouera si la fix saute. Sa raison d'être est la pin du fix dans le temps, pas la couverture d'un comportement nominal. Un test de régression est typiquement écrit en miroir d'un commit `fix(scope): …`.

### Hors périmètre automatique

**system** :
Test qui exerce un runtime natif lourd dans une condition réaliste — chargement d'un modèle Whisper de 1 Go, transcription d'un fichier audio de référence stocké dans le repo de test, lecture d'un payload Hue Entertainment sur un vrai bridge. Possible à automatiser localement, mais lent, gourmand, et conditionné à la disponibilité des artefacts natifs et matériels. Reste à la main de Louis ou d'un poste dédié.

**interactive** :
Test qui exige un poste Windows interactif et un humain ou un faux humain capable de présenter au système des conditions réelles — un vrai micro qui capte du son, un hotkey global qui ne rentre pas en conflit avec une autre app, une fenêtre cible UIAutomation pour valider le paste, un display physique pour DXGI Output Duplication. Hors automatisable par un agent. Validé via le skill `verify`.

### Distinction clé entre integration et system

La frontière entre `integration` et `system` se joue sur *le poids de la dépendance et son substituabilité*. La fonction `Deckle.Audio.MicrophoneCapture.Probe` qui interroge le device audio pour ses capacités relève d'`integration` si on substitue une fake source audio derrière le seam WASAPI. Un test qui enregistre 3 secondes de voix réelle dans une boucle complète relève d'`interactive`. Un test qui drive Whisper sur un wav stocké dans le repo de test relève de `system`.

## Exemple de conversation

> — Le bug d'hier sur le clipboard Win32, on le couvre comment ?
> — Test de regression. Le `OpenClipboard` retournait `false` quand un autre process tenait la session ; la fix retry trois fois ; le test simule trois échecs puis un succès et vérifie qu'on a bien copié.
> — D'accord. Et pour vérifier qu'on émet le bon `ClipboardCopied` à la fin ?
> — C'est de l'observability. Un `TestEventListener` accroché à `DeckleWhispSource`, on assert sur la séquence et sur le payload.
> — Et le micro maintenant ? Je voudrais tester qu'on ne plante pas quand il n'y en a pas.
> — Integration. On simule un device qui retourne « no input » et on vérifie le chemin d'erreur. Un test interactive prendrait un vrai micro débranché — utile mais à la main.
