---
description: "Note de conception du changement 7 de la revue de cohérence des pages audio (2026-11-07) : la courbe de coupe de la page Segmentation devient un composant qu'on édite par manipulation directe — extrémités tirables qui portent les quatre ancres, bande « une pause, au zoom » où la marge de coupe et la parole minimale se tirent, graphique pleine largeur dont la hauteur suit la fenêtre, page graphe d'abord — avec la piste de la ligne de temps à points partagée avec Ambient (changement 9), écrite comme piste et non comme code."
---

# Courbe de coupe en manipulation directe — note de conception

Source : document Anytype « Revue de cohérence : pages Recording, Decoding et Streaming (proposition du 2026-11-07) », section 4 (cible Segmentation) et changement 7 de la section 6. Branche `reverent-bohr-f5bf08`, construite sur le premier passage (`audio-pages-first-pass-66797d`, une seule grille de champs à 112 px).

Convention : **établi** (lu dans le code ou la doctrine), **interprétation** (mon choix, argumenté), **incertain** (non vérifié, en particulier tout ce qui touche à l'écran : la validation du dépôt est un build sans lancement).

## 1. Ce qui change, et sur quoi chaque choix s'appuie

**Les extrémités de la courbe deviennent des poignées, et ce sont elles qui portent les ancres.** Le point de départ du carré unité est (début de rampe, attente max), le point d'arrivée (fin de rampe, attente min) — **établi**, `CutCurveFrame`. Une poignée par extrémité, tirable dans les deux axes : à l'horizontale elle déplace la rampe, à la verticale le niveau. Les deux points de contrôle de la Bézier gardent leurs poignées rondes ; les extrémités prennent une poignée carrée.
- *Pourquoi deux poignées 2D plutôt que quatre lignes tirables* : quatre ancres pour deux points, c'est ce que le dessin montre déjà ; tirer le coin, c'est tirer ce qu'on voit (manipulation directe). Quatre lignes tirables en plus des deux poignées feraient six cibles qui se chevauchent dans les coins (loi de Hick, et des cibles qui se recouvrent trompent Fitts).
- *Carré pour l'ancre sur la courbe, rond pour la poignée hors de la courbe* : c'est la convention des outils de dessin vectoriel que les designers connaissent (loi de Jakob). Un rôle, une forme (similarité).
- *Clavier* : chaque poignée prend le focus ; flèches gauche/droite pour l'abscisse, haut/bas pour l'ordonnée, au pas de la valeur (le grain de `MagnitudeGrain`, 5 s pour la rampe), Maj pour dix pas. Écriture à chaque touche, comme les points de contrôle aujourd'hui (doctrine : un geste clavier est un geste fini).
- *Bornes pendant le geste* : la poignée reste dans les bornes de ses ancres et garde l'ordre (la fin de rampe ne passe pas avant le début, l'attente min ne passe pas au-dessus de la max). Le cadre du dessin (l'échelle) est figé pendant le geste et recalculé au relâcher : rien ne bouge sous le pointeur. Pour qu'on puisse tirer l'attente max vers le haut, le cadre garde une marge au-dessus du maximum (×1,25) — **c'est la seule valeur dérivée ajoutée**, dans `CutCurveFrame` ; `EnergySegmenterSettings` et la Bézier ne changent pas.

**La marge de coupe et la parole minimale entrent dans le composant, sur leur propre échelle.** Sur l'axe de la courbe (0 à ~140 s), 0,15 s fait moins d'un pixel — **établi** par la revue. Sous le graphique, une bande « une pause, au zoom » montre la fin d'un énoncé à l'échelle d'un mot : la voix, puis la pause, la part gardée (la marge), la part jetée, et la coupe. Ses deux bords tirables sont la parole minimale (un crochet sur le début de la voix : plus court, c'est un clic, jeté) et la marge (la part de pause gardée). La coupe y est placée à l'attente min, la plus courte pause qui coupe ; elle n'est pas tirable dans la bande (elle se tire sur la courbe), elle s'y lit.
- *Même composant, autre échelle* : région commune (les deux vues dans une carte), alignement des deux origines sur le même bord gauche, et une règle graduée en secondes sous la bande pour qu'on lise son zoom — elle ne prétend pas être sur l'axe de la courbe (la revue, section 2).
- *Le zoom* suit les valeurs : la bande couvre la voix plus la pause jusqu'à la coupe, arrondie à une graduation ronde. Il est figé pendant un geste, recalculé au relâcher.
- Les deux cartes « Cut margin » et « Minimum speech » disparaissent ; leurs valeurs, leurs bornes, leurs défauts et leurs descriptions passent dans la carte de la courbe, sous sa remise à zéro unique. **Rien ne quitte le produit** : chaque valeur reste réglable, au pointeur, au clavier et en champ.

**Les champs restent, sous le graphique.** La doctrine (deckle-settings-ux) veut qu'un dessin dont les points sont la valeur écrive par ses poignées, au relâcher, *avec ses valeurs en champs à côté* ; c'est aussi l'accès des lecteurs d'écran, qui ne lisent pas une position. Le graphique prend toute la largeur ; les champs passent dessous en trois blocs dans l'ordre des vues : le cadre (les quatre ancres), la forme (x1 y1 x2 y2 et la lecture `cubic-bezier`), la pause (parole minimale, marge). La remise à zéro de la carte termine la rangée des champs, à côté des valeurs qu'elle remet (proximité), au lieu d'une colonne à droite du graphique qui rendait la marge droite plus large que la gauche.

**Le graphique prend la largeur et l'essentiel de la hauteur.** La largeur est celle de la colonne de contenu de la page (le plafond de 1000 px des pages Settings reste, c'est celui de Windows Settings). La hauteur suit la fenêtre : la moitié de la hauteur visible du `ScrollViewer` de la page, entre 240 et 560 px, au lieu des 320 px fixes. Marges égales : le dessin va d'un bord de texte de la carte à l'autre, sans colonne de remise à droite.

**La page devient graphe d'abord.** Ordre : l'en-tête, la bascule, la courbe, puis le filtrage de la parole (Silero) — replié par défaut, comme le pli « Phantom-text guards » de la cible. Il n'est plus ouvert de force à chaque visite. **Interprétation** : c'est la seule façon que le graphique soit ce qu'on voit d'abord ; le pli reste à un clic (divulgation progressive). Le regroupement Silero + no-speech (changement 6) et « When to transcribe » (changement 8) ne sont pas faits ici.

## 2. La forme du composant

- `CurveAnchor` gagne un `Role` (`OriginX`, `OriginY`, `EndX`, `EndY`) : l'ancre *est* cette coordonnée du cadre, dans la même unité. Le composeur en déduit les bornes et le pas de chaque axe des poignées d'extrémité (`CurveEndpointLimits`), et écrit les ancres concernées au relâcher.
- `CurveArgs` gagne `Pause` (`PauseStripArgs` : la parole minimale et la marge comme deux `CurveAnchor`, la coupe comme une valeur lue).
- `CurveEditor` gagne : les deux poignées d'extrémité (`EndpointLimits`, événement `FrameCommitted`), un emplacement sous le dessin pour une vue à une autre échelle (`PlotFooter`, où le composeur pose la `PauseStrip`), un emplacement après les points de contrôle (`LegendAfter`), un emplacement de fin de rangée (`LegendTrailing`, la remise), et `ViewportShare` pour la hauteur.
- L'arithmétique (bornes, pas, ordre, zoom de la bande) est pure et testée sans contrôle (`CurveEndpointLimits`, `PauseScale`).
- La forme carrée Ambient (sans axes) ne change pas.

## 3. Piste : la ligne de temps à points partagée avec Ambient (changement 9)

Pas de code ici. Ce qu'il faudrait, et ce que ce travail prépare :

1. **Un modèle de points** à la place de `CurvePoints` : une liste ordonnée de 2 à 6 points `(x, y)` sur le carré unité, avec par point ses bornes et son pas — c'est la forme de `CurveEndpointLimits`, généralisée d'une paire d'extrémités à une liste. Les extrémités actuelles sont les deux premiers points de cette liste.
2. **Une interpolation qui ne remonte jamais** entre les points (monotone par morceaux, type Fritsch–Carlson ou Hermite monotone), parce que le découpeur exige un délai qui ne fait que baisser ; Ambient voudra peut-être la même, montante.
3. **Une migration** des réglages enregistrés : les quatre nombres de la Bézier deviennent des points (échantillonner la Bézier en 4 à 6 points), lue une fois au chargement, sans perte visible.
4. **Le moteur** : `UnitBezier` et `AmbientBrightnessCurve` remplacés par l'interpolation partagée, avec des tests de non-régression sur les courbes par défaut.
5. **L'édition** : ajouter un point au survol de la courbe, le retirer au clavier (Suppr) ou en le tirant hors du dessin, 6 au plus ; les poignées carrées de ce travail sont déjà la forme d'un point sur la courbe. Les poignées rondes hors courbe disparaissent avec la Bézier.
6. **L'emplacement `PlotFooter`** sert à Ambient pour une vue zoomée du même genre (une transition, à l'échelle d'une seconde).

## 4. Ce que la construction a ajouté

- **Défaut probable corrigé en passant** : le glisser d'un point de contrôle repartait à chaque pas de la valeur arrondie au centième. `DragDeltaEventArgs.HorizontalChange` est le déplacement depuis le pas précédent (**établi**, doc UWP de la même classe), donc un glisser lent, à pas plus petits qu'un demi-centième, revenait sans cesse à sa valeur et restait sur place. Le pointeur s'accumule maintenant sans arrondi ; seule la valeur est arrondie. Les poignées d'extrémité et de la bande suivent la même règle. **Incertain** : le défaut n'a pas été vu à l'écran, il est déduit du code.
- **Recherche** : les mots qui trouvaient les deux cartes retirées (marge, parole minimale) trouvent maintenant la carte de la courbe.
- Les légendes des deux champs prennent l'unité, comme celles des ancres (« Cut margin, s », « Minimum speech, s ») ; la bande a ses propres mots (« minimum speech », « kept », « dropped », « cut »).

## 5. Ce qui n'est pas vérifié

Tout le rendu : le dépôt valide par un build sans lancement, et l'application résidente n'est ni arrêtée ni relancée. Les tailles (hauteur de la bande, lisibilité de 0,15 s, place des libellés dans la bande quand une valeur est petite), le comportement du glisser (finesse, figement du cadre), la lecture par Narrateur des poignées, et l'enchaînement des focus au clavier sont à regarder à l'écran.
