# Molette vers touchpad virtuel — fiche de spécification

Date : 2026-06-15.
Statut : exploration produit/technique, pas décision d'architecture.

## Intention

Transformer la molette magnétique d'une souris en intention de scroll de touchpad, avec le ressenti d'un geste à deux doigts plutôt que celui d'une molette Windows classique.

Le but n'est pas de rendre la molette "plus smooth" par animation locale. Le but est que Windows et les applications reçoivent un signal qui ressemble à un vrai geste de Precision Touchpad, pour retrouver le comportement universel du trackpad : scroll pixel-level, accélération, inertie, cohérence entre Explorer, Notepad, navigateurs, WinUI et surfaces anciennes qui respectent le pipeline Windows.

## Résultat Utilisateur Cible

Quand l'utilisateur tourne lentement la molette, l'effet doit correspondre à deux doigts posés au centre du trackpad qui glissent doucement vers le haut ou le bas.

Quand les crans sont espacés, le scroll doit être lent, précis et contrôlé. Plus l'écart entre deux événements de molette est grand, plus la vitesse virtuelle des deux doigts est faible.

Quand les crans sont rapprochés, le scroll doit accélérer naturellement. La molette ne représente pas une distance brute ; elle représente une cadence et une énergie de geste.

Quand la molette part en roue libre, le système doit produire un geste de type fling : deux doigts donnent une impulsion plus forte, puis se lèvent pour laisser Windows produire l'inertie native.

Le comportement doit rester réglable par l'utilisateur, au minimum avec une sensibilité globale. Les réglages fins servent à calibrer, pas à exposer un tableau de bord de physique.

## Modèle Mental

La molette ne contrôle pas directement un offset de scroll.

Elle contrôle un duo de contacts virtuels :

```text
premier tick
  -> deux doigts virtuels apparaissent

ticks suivants
  -> les deux doigts se déplacent ensemble

pause courte
  -> les doigts se lèvent

rafale rapide
  -> les doigts effectuent un geste court, rapide, puis lift
```

Le geste doit ressembler à un vrai usage humain :

- deux doigts proches, pas parfaitement superposés ;
- léger décalage longitudinal entre index et majeur ;
- trajectoire globalement parallèle ;
- apparition simultanée des deux contacts ;
- mouvement vertical majoritaire ;
- bruit faible mais non forcément nul si les données réelles montrent que le trackpad humain n'est jamais parfaitement droit.

## Sortie Cible

La sortie synthétique visée est un flux de contacts virtuels à 120 Hz, aligné avec l'écran de l'utilisateur.

Chaque frame synthétique représente l'état complet du geste :

```text
timestamp
contact 1: id, x, y, tip=true/false, confidence=true
contact 2: id, x, y, tip=true/false, confidence=true
contact count
scan/frame counter
```

La cadence 120 Hz est la cadence de référence du modèle. Si Windows ou le driver impose une autre cadence interne, le comportement observable doit rester équivalent à 120 Hz : latence faible, mouvement continu, pas de paquets perceptibles.

## Modes De Geste

### Creep

Usage : crans isolés ou espacés.

Comportement :

- deux doigts down ;
- petit déplacement vertical ;
- vitesse faible ;
- lift après un délai court ;
- pas de fling.

Objectif perceptif : placement fin, lecture ligne par ligne sans le caractère rude de la molette.

### Glide

Usage : crans réguliers, modérément rapprochés.

Comportement :

- les deux doigts restent down tant que la cadence reste active ;
- vitesse proportionnelle à la cadence ;
- mouvement continu à 120 Hz ;
- lift quand la molette devient inactive.

Objectif perceptif : scroll fluide et contrôlé, équivalent à deux doigts qu'on déplace sans intention de lancer l'inertie.

### Fling

Usage : rafale rapide, typiquement roue libre.

Comportement :

- accumulation d'énergie sur la rafale ;
- mouvement court et plus rapide ;
- lift volontaire ;
- Windows prend le relais avec son inertie native.

Objectif perceptif : permettre de parcourir une longue distance sans que le doigt soit limité par l'amplitude physique de la molette ou du trackpad.

## Entrée Molette

Le POC Raw Input montre que la MX Master 3S via le périphérique Logitech `VID_046D&PID_C548` expose des deltas `+120/-120`, même en roue libre. La finesse exploitable n'est donc pas dans l'amplitude du delta mais dans la cadence temporelle.

Données à enregistrer :

```text
t
device
axis
delta
gap_ms
burst_id
```

Interprétation initiale :

- grand `gap_ms` : geste lent ;
- `gap_ms` stable et moyen : glide ;
- `gap_ms` très faible, autour de 7-15 ms dans les traces observées : burst/fling ;
- changement de signe : nouvelle intention ou correction immédiate, à traiter prudemment.

## Données Trackpad À Capturer

Deckle possède déjà `ContactFrameRecorder`, qui écrit des frames JSONL de contacts réels.

Ce recorder doit servir à capturer des gestes de référence :

- scroll lent vers le bas ;
- scroll lent vers le haut ;
- scroll moyen continu ;
- fling court ;
- fling fort ;
- correction de direction ;
- usage réel libre pendant quelques minutes.

Mesures à extraire :

- distance moyenne entre les deux doigts ;
- décalage naturel index/majeur ;
- position de départ typique ;
- vitesse du centroid ;
- accélération initiale ;
- durée down avant mouvement utile ;
- durée totale du contact ;
- vitesse au lift ;
- forme de la trajectoire ;
- seuil empirique entre scroll contrôlé et fling.

Ces données doivent produire une cible de calibration, pas un modèle sur-appris. L'objectif est de reproduire l'intention humaine, pas de rejouer exactement une main.

## Paramètres À Explorer

Paramètres produit probables :

- sensibilité globale ;
- puissance de fling ;
- durée de maintien après dernier tick ;
- seuil de burst ;
- distance virtuelle par tick lent.

Paramètres internes probables :

- position initiale des deux contacts ;
- écart horizontal entre contacts ;
- décalage vertical entre contacts ;
- amplitude minimale pour engager le scroll Windows ;
- boost du premier mouvement ;
- courbe d'accumulation d'énergie ;
- décroissance de l'énergie ;
- délai avant lift ;
- distance maximale d'un geste avant repositionnement virtuel.

Tous ces paramètres sont ouverts. La spécification fixe le ressenti cible, pas leurs valeurs.

## Contraintes De Performance

Le système doit viser une latence perceptible minimale dès le premier tick.

Principe recommandé :

```text
Deckle user-mode
  -> observe la molette
  -> envoie des impulsions compactes

driver / couche synthétique
  -> maintient l'état du geste
  -> émet les contacts à 120 Hz
```

Deckle ne doit pas envoyer chaque frame de contact si une couche plus basse peut les générer de façon stable. Deckle doit envoyer l'intention : direction, timestamp, énergie, cadence.

La génération 120 Hz de deux contacts est triviale en volume. Le risque principal n'est pas CPU ; il est dans la reconnaissance du geste par Windows et dans la stabilité du périphérique virtuel.

## Architecture Cible, Indépendante De La Tech

```text
MouseWheelSource
  lit les événements de molette

WheelGestureModel
  transforme les ticks en intention Creep / Glide / Fling

VirtualTouchpadOutput
  transforme l'intention en deux contacts à 120 Hz

Windows Precision Touchpad stack
  interprète les contacts comme scroll natif
```

Implémentation possible :

- `MouseWheelSource` en C# Deckle, via Raw Input ou hook selon le besoin de suppression de l'événement original ;
- `WheelGestureModel` en C# tant que le driver reçoit des impulsions ;
- `VirtualTouchpadOutput` via driver VHF Precision Touchpad si l'objectif "fonctionne partout" est maintenu.

## Critères De Succès

Premier jalon : Windows accepte un faux périphérique Precision Touchpad minimal.

Deuxième jalon : deux contacts virtuels qui glissent produisent du scroll dans Explorer, Notepad, Chrome/Edge et une surface WinUI.

Troisième jalon : un geste virtuel court et rapide suivi d'un lift déclenche l'inertie Windows.

Quatrième jalon : les traces de molette Logitech pilotent les trois modes Creep, Glide et Fling sans sensation de cran.

Cinquième jalon : le comportement reste stable à 120 Hz pendant une session réelle.

## Non-Objectifs Initiaux

Ne pas gérer tous les gestes touchpad.

Ne pas émuler un pointeur complet.

Ne pas exposer d'interface de réglage avancée au début.

Ne pas distribuer le driver publiquement tant que la preuve locale n'est pas solide.

Ne pas remplacer le module three-finger drag existant.

## Questions Ouvertes

Windows accepte-t-il un périphérique VHF qui expose seulement le minimum crédible d'un Precision Touchpad ?

Le pipeline Windows déclenche-t-il l'inertie native sur un fling synthétique ?

Quelle amplitude minimale engage le scroll sans produire de latence ?

Le lift doit-il arriver immédiatement après une rafale ou après une courte phase de mouvement ?

Faut-il supprimer l'événement molette original, ou seulement l'ignorer quand le driver virtuel est actif ?

Le modèle doit-il être piloté uniquement par `gap_ms`, ou aussi par la longueur de burst et la direction récente ?

Combien de calibration vient des données de Louis, et combien doit rester générique ?

## Prochaine Sortie Attendue

La prochaine sortie concrète n'est pas le driver complet.

La prochaine sortie est un paquet de mesure :

1. une capture JSONL de gestes trackpad réels ;
2. une capture JSONL de la molette sur les mêmes intentions ;
3. un rapport comparant cadence, vitesse, durée, lift et énergie ;
4. une proposition de mapping initial `wheel ticks -> two-finger gesture`.

Après cette sortie, le spike driver peut être jugé sur des cibles mesurées plutôt que sur une intuition.
