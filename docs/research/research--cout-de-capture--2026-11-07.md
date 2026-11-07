---
description: "Mesure (2026-11-07) : ce que coûte la capture Ambient à 60 Hz et au-delà sur l'écran 4K à 120 Hz de Louis, où l'image est copiée et réduite, les octets par image et par seconde par palier, et le palier que la mesure soutient aujourd'hui. Le réglage 15/30/60 Hz n'a pas bougé."
---

# Coût de la capture Ambient selon la fréquence

Question de Louis (dictée du 2026-11-07) : capturer à la fréquence de l'écran (120, 240, voire 500 Hz) coûterait-il trop, sachant que l'échantillonneur lit à peu près 240 pixels ? Le réglage 15/30/60 Hz pourrait-il disparaître au profit de « suivre l'écran » ? Consigne : mesurer, ne rien changer au réglage. Direction donnée ensuite par Louis : un plafond décidé dans le code, par paliers (240 Hz quand ça ne coûte rien de sensible, 120 quand ça coûte un peu, 60 pour la sûreté, la fréquence de l'écran quand il est plus lent), avec peut-être un « mode performance » global qui baisse ces valeurs ensemble.

Chaque point est marqué **établi** (lu dans le code ou mesuré), **interprétation** ou **non vérifié**.

## 1. Où l'image est copiée et où se fait la moyenne

**Établi (code).** L'image ne quitte pas le GPU en taille pleine.

1. `FrameSampler.Process.cs:30` : `CopySubresourceRegion` copie l'image entière du bureau (3840×2160) dans le niveau 0 d'une texture intermédiaire, **de GPU à GPU**, en mémoire vidéo.
2. `FrameSampler.Process.cs:34` : `GenerateMips` construit la pyramide de réductions sur le GPU. Chaque niveau fait la moyenne de blocs 2×2 du précédent.
3. `FrameSampler.Process.cs:36` : seul le niveau cible est copié vers une texture « staging », c'est-à-dire lisible par le CPU. En 4K, c'est le niveau 6 : 60×33 cellules, chacune moyenne d'un bloc de 64×64 pixels (`FrameSampler.cs:121`, cible 30×17, arrondie pour garder le format 16:9).
4. `FrameSampler.Process.cs:41` : `Map` attend que le GPU ait fini, puis le CPU lit ces 1 980 cellules (`FrameSampler.Readback.cs`). Il les convertit en lumière linéaire, et calcule la grille et la moyenne globale.
5. La moyenne par zone (`AmbientZoneSampler.SampleZone`) tourne **sur le CPU**, sur la grille de 60×33 déjà réduite, et non sur des pixels de l'écran. Elle est appelée par la boucle d'envoi (`AmbientEngine.MultiLightPush.cs:32-35`), au rythme des lampes, pas à chaque image capturée. Avec une bande de 15 %, une zone lit environ 300 cellules (haut et bas : 5 lignes × 60 ; gauche et droite : 9 colonnes × 33). Les « 240 pixels » de la question sont donc environ 300 cellules, et chacune résume 4 096 pixels.

## 2. Méthode

**Établi.** Test `AmbientCaptureCostInteractiveTests` (catégorie `interactive`, explicite, hors `dotnet test` ordinaire), dans `tests/Deckle.Lighting.Ambient.Tests`. Il pilote les vrais `ScreenCaptureService` et `FrameSampler`, le couple que lance le moteur, plus `AmbientZoneSampler` sur chaque grille. Chaque cible (60, 120, 240, 500 Hz) tourne 1 s pour chauffer puis 10 s mesurées. Le test relève les images livrées, le temps CPU du thread de capture par image (`GetThreadTimes` : acquisition DXGI, copie, réduction, lecture), le temps réel de `FrameSampler.Process` par image, et le temps des quatre zones.

La nuit, le bureau est statique et DXGI ne livre une image que quand l'écran change. Une fenêtre de 32×32 dans le coin bas-droit change donc d'opacité à chaque composition (`DwmFlush`), ce qui fait rafraîchir le bureau à 120 Hz (mesuré : 120 bascules/s). Le travail de Deckle par image ne dépend pas du contenu : la copie et la réduction portent toujours sur l'image entière.

Machine : Ryzen 7 5800X (16 threads), Radeon RX 7900 XT, écran 3840×2160 à 120 Hz (maximum annoncé : 120 Hz), en SDR (BGRA8). L'app Deckle ne tournait pas, et le verrou de build machine était tenu pendant la mesure, pour qu'aucune compilation d'une autre session ne la fausse. Deux passes, à 03:10 et 03:13.

## 3. Mesures

**Établi (mesuré, deux passes).**

| cible | images livrées/s | CPU thread de capture, ms/image | `Process` temps réel, ms/image (moyenne / p95) | 4 zones, µs/image |
|---|---|---|---|---|
| 60 Hz | 60,0 / 60,0 | 0,34 / 0,44 | 0,63 / 0,79 — 0,74 / 0,94 | 22 |
| 120 Hz | 119,8 / 120,0 | 0,33 / 0,29 | 0,61 / 0,78 — 0,74 / 0,96 | 21 |
| 240 Hz | 139,5 / 120,1 | 0,36 / 0,25 | 0,54 / 0,66 — 0,73 / 0,93 | 21 |
| 500 Hz | 135,8 / 130,9 | 0,21 / 0,17 | 0,59 / 0,76 — 0,70 / 0,91 | 21 |

Le CPU du processus entier, au-delà de la ligne de base (la fenêtre animée seule, 22 ms/s), va de 31 à 69 ms/s sans ordre lisible d'une cible à l'autre. Ce relevé est bruité : il compte le runtime et le GC, à la résolution de 15,6 ms du compteur. Le chiffre fiable est celui du thread de capture.

Au-dessus de 120 Hz, DXGI a livré jusqu'à 140 images/s sur un écran à 120 Hz. **Interprétation** : DXGI signale chaque mise à jour du bureau, et plusieurs présentations peuvent tomber dans un même intervalle d'affichage. « Suivre l'écran » doit donc plafonner à la fréquence annoncée par l'écran, pas à ce que DXGI livre. C'est ce que fait déjà `CapturePacer` quand on lui donne une cible.

Le relevé d'occupation GPU par compteur Windows (`\GPU Engine(pid_*)`) est resté à zéro : il n'est pas exploitable ici. Le coût GPU ci-dessous est donc calculé, pas mesuré.

## 4. Octets par image et par seconde, écran 4K

**Établi (calcul depuis les tailles du code), sauf la réduction, estimée.** En SDR BGRA8, 4 octets par pixel :

- image du bureau : 3840×2160×4 = 33 177 600 o (33,2 Mo) ;
- copie GPU→GPU : 33,2 Mo lus + 33,2 Mo écrits ;
- `GenerateMips` : environ 44,2 Mo lus et 11,1 Mo écrits (niveaux 0 à 5 lus, 1 à 6 écrits). **Interprétation** : le pilote peut procéder autrement ;
- total GPU : environ 122 Mo de trafic mémoire vidéo par image ;
- lecture par le CPU : 60×33×4 = 7 920 o par image.

En HDR (FP16, 8 octets par pixel), tout double : 66,4 Mo l'image, 15 840 o lus. Le HDR n'a pas été mesuré : l'écran était en SDR.

| palier | trafic GPU estimé | lecture CPU | CPU thread de capture | statut |
|---|---|---|---|---|
| 60 Hz | 7,3 Go/s | 475 Ko/s | 20 à 27 ms/s (2 à 3 % d'un cœur) | mesuré |
| 120 Hz | 14,6 Go/s | 950 Ko/s | 35 à 40 ms/s (≈ 4 % d'un cœur) | mesuré, maximum de cet écran |
| 240 Hz | 29,2 Go/s | 1,9 Mo/s | ≈ 60 à 85 ms/s (6 à 9 % d'un cœur) | extrapolé |
| 500 Hz | 61 Go/s | 4,0 Mo/s | ≈ 125 à 175 ms/s | extrapolé |
| 700 Hz | 85 Go/s | 5,5 Mo/s | ≈ 175 à 250 ms/s | extrapolé |

Sur la 7900 XT, la bande passante mémoire annoncée est de 800 Go/s (**non vérifié**, chiffre constructeur) : 120 Hz en prend environ 2 %, 240 Hz environ 4 %. Le coût par image est à peu près constant entre 60 et 140 images/s, ce qui fonde l'extrapolation linéaire. Elle reste une extrapolation : rien n'a été mesuré sur un écran plus rapide que 120 Hz.

## 5. Ce que coûte quoi

**Interprétation.** Le poste principal n'est pas la lecture par le CPU (8 Ko par image, négligeable). Ce sont deux choses :

- **côté GPU**, la copie de l'image entière (`FrameSampler.Process.cs:30`) puis sa réduction : environ 122 Mo par image, qui croissent linéairement avec la fréquence ;
- **côté thread de capture**, l'attente synchrone de `Map` (`FrameSampler.Process.cs:41`), environ 0,6 à 0,7 ms par image, que le thread passe à attendre le GPU. À 500 Hz, cela fait 35 % du budget de 2 ms par image ; à 700 Hz, la moitié de 1,4 ms.

La valeur d'une capture rapide est bornée par les lampes. Le flux Entertainment pousse à 50 Hz, et la boucle d'envoi prend le dernier échantillon disponible. Passer de 120 à 240 Hz rend l'échantillon plus frais de 4 ms au plus, quand une lampe reçoit une couleur toutes les 20 ms.

## 6. Recommandation

1. **Le palier que la mesure soutient aujourd'hui : 120 Hz.** Sur cette machine, suivre l'écran jusqu'à 120 Hz coûte environ 4 % d'un cœur et 2 % de la bande passante GPU, sans effet sensible. C'est la fréquence de l'écran de Louis.
2. **240 Hz** est plausible par extrapolation (6 à 9 % d'un cœur), mais ni mesuré ni utile pour des lampes servies à 50 Hz. Le palier « ne coûte rien de sensible » ne devrait pas monter à 240 avant une mesure sur un écran à 240 Hz et la preuve d'un gain visible.
3. **Un plafond est nécessaire.** Le coût est linéaire, DXGI livre plus que la fréquence de l'écran, et les écrans à 480 ou 700 Hz existent.
4. **60 Hz reste le palier sûr** pour le « mode performance » : la mesure ne dit rien des machines à GPU intégré, où la copie de 33 Mo par image pèse sur la mémoire partagée (**non vérifié**).
5. **Le réglage 15/30/60 Hz reste tel quel dans le code**, comme demandé.

## 7. Pistes, non faites ce soir

- **Ne plus copier l'image entière.** La copie de `FrameSampler.Process.cs:30` existe parce que la texture du bureau n'a qu'un niveau de mip et ne peut pas recevoir `GenerateMips`. Une réduction faite directement depuis la texture du bureau vers une petite cible (un shader de calcul, ou un rendu vers une cible de 60×33) supprimerait les 66 Mo de copie par image.
- **Ne plus attendre le GPU.** Un anneau de deux ou trois textures staging à `FrameSampler.Process.cs:36-41` lirait l'image précédente pendant que le GPU traite la suivante. Le thread de capture n'attendrait plus ses 0,6 ms par image, au prix d'une image de latence.
- **Mesurer le HDR et un écran plus rapide**, avec le même test, avant de fixer les paliers au-dessus de 120 Hz.

## Pour relancer la mesure

Compiler `tests/Deckle.Lighting.Ambient.Tests` (`scripts/build-locked.ps1 -Project ...`), fermer Deckle si Ambient tourne, puis :

```
artifacts\bin\Deckle.Lighting.Ambient.Tests\debug\Deckle.Lighting.Ambient.Tests.exe -class Deckle.Lighting.Ambient.Tests.AmbientCaptureCostInteractiveTests -explicit only
```

Le rapport s'écrit dans `artifacts/capture-cost/`.
