# CLAUDE.md — Deckle.Lighting

Module driver pour les outputs lumineux externes. Couvre l'abstraction `ILightOutput` agnostique au driver, l'implémentation REST direct Hue (`HueRestLightOutput`) avec discovery, pairing, control et entertainment configurations, et la pile mathématique de conversion couleur RGB → Hue xy (`HueColorMath`) avec gamut mapping Gamut C client-side. Le module est consommé par `Deckle.Lighting.Ambient` (pilotage live depuis la capture écran) et par `Deckle.Playground` (test isolation bridge, calibration).

Le pipeline emprunte délibérément la voie REST CLIP v1 plafonnée à ~10-20 Hz, pas la voie Entertainment v2 DTLS-PSK. 100 % C#, zéro dépendance native, zéro NuGet tiers — la cadence est suffisante pour un mode ambient avec smoothing au-dessus. La voie Entertainment v2 reste archivée pour plus tard si la perception le justifie ; le swap se fera derrière l'abstraction `ILightOutput` sans toucher au reste du pipeline.

## Structure du module

Le dossier `Hue/` porte toute la pile Hue : `HueDiscovery.cs` (cloud lookup `discovery.meethue.com`), `HueBridgeClient.cs` (HTTPS bypass cert + pairing CLIP v1 + control), `HueRestLightOutput.cs` (implémentation `ILightOutput` au-dessus du client REST), `HueColorMath.cs` (math de conversion RGB sRGB → xy CIE 1931 + bri Hue + clip Gamut C), plus les DTOs `HueBridge`, `HueGroup`, `HueLight`, `HueEntertainmentArea`. Au root du module, `ILightOutput.cs`, `LightDescriptor.cs`, `LightColor.cs` constituent l'abstraction agnostique. Le code de bootstrap (paramètres de connexion bridge, validation IP, persistance du `username` CLIP API key) vit dans `Deckle.Lighting.Ambient/AmbientSettings.cs` puisque c'est le consumer qui orchestre. Le driver lui-même est stateless en dehors de son `HttpClient` et de son `username`.

## Pipeline color science

Doc canonique de la passe color science menée sur la pipeline ambient lighting. Couvre la cause du bug Night Owl `#011627 → turquoise`, les décisions math motivées, et les anti-patterns écartés.

### Contexte

Le pipeline ambient produit historiquement un rendu chromatique incorrect sur les bleus profonds. VS Code Night Owl `#011627` rend turquoise sur lampe Hue Play / Iris / E14 (Gamut C) au lieu de bleu. `HueColorMath.RgbToHueXyBri` convertit le RGB sRGB en xy CIE 1931 correctement (gamma decode, matrice Philips Wide Gamut D65, projection `X/(X+Y+Z)`) puis envoie le résultat au pont. Le pont reçoit une chromaticité brute, non clippée vers son triangle Gamut C, et applique son propre gamut mapping propriétaire qui projette les points hors triangle sur l'arête la plus proche. Pour `#011627` la math donne linear `(0.0003, 0.0071, 0.0179)` → `X=0.00420, Y=0.00569, Z=0.01816` → xy `(0.150, 0.203)`. Ce point est juste à gauche du blue corner Gamut C `(0.1532, 0.0475)` et le pont le projette sur l'arête B-G, où `x≈0.15` mappe sur un mix high-G low-B — rendu turquoise.

Deux biais latents secondaires apparaissent à la lecture, sans être responsables du bug Night Owl statique mais affectant les scènes complexes. L'averaging arithmétique en sRGB gamma-encoded dans `FrameSampler.ReadGridBGRA8` (`Deckle.Vision`) et `AmbientEngine.SampleZone` (`Deckle.Lighting.Ambient`) — deux étages cascadés qui amplifient les mid-tones. `ApplySaturationBoost` (`AmbientEngine.cs`) opère en HSV, ce qui souffre exactement de l'asymétrie luminance yellow/blue déjà documentée comme raison d'avoir migré la stroke conique HUD en OKLCh.

### Gamut mapping client-side, nearest-edge projection

Méthode `HueColorMath.ClipToGamutC(HueXy) → HueXy`. Si le point xy est in-triangle Gamut C, identité. Sinon, projeter sur le point le plus proche du triangle via clamp paramétrique `t ∈ [0, 1]` sur chacune des trois arêtes (Red↔Green, Green↔Blue, Blue↔Red), retenir la projection à plus petite distance euclidienne 2D dans le plan xy. Les corners Gamut C sont `R=(0.6915, 0.3083)`, `G=(0.17, 0.7)`, `B=(0.1532, 0.0475)` (référence Philips Hue developer docs). Appelée à la sortie de `RgbToHueXyBri`, avant retour au caller. `HueBridgeClient` continue d'envoyer xy brut au pont, qui continue son clip propriétaire mais maintenant sur un point déjà in-gamut, donc identité côté pont.

**Rejet des alternatives.** Projection vers white-point D65 `(0.3127, 0.3290)` déplace les points hors-gamut vers cyan ou violet selon l'arête traversée — pour Night Owl `#011627`, traverse l'arête B-G donc même rendu turquoise, ne résout pas le bug. Gamut hull compression sigmoïde impose une déformation globale sur toute la scène, sous-justifiée pour une lampe d'ambiance et coûteuse à dériver les paramètres. Nearest-edge minimise ΔE chromaticity par construction et laisse les blues profonds saturer sur le corner blue Hue au lieu de fuir le long de l'arête B-G.

**Trade-off.** Léger hue-shift sur les points significativement hors-gamut. Night Owl `#011627` sera rendu comme « blue Hue corner » — un peu plus violacé qu'un cobalt parfait, mais lisiblement bleu, pas turquoise. Coût CPU : trois produits scalaires et trois clamps par push, négligeable au regard du HTTP round-trip vers le pont qui domine la latence.

### Linear-light averaging via LUT 256-entry

Le sRGB encode la luminance via une courbe gamma ≈ 2.4. Sommer arithmétiquement des bytes sRGB amplifie les mid-tones par rapport à un averaging en linear-light qui respecte la photométrie. `ColorSpace.SrgbToLinear8Lut` (`float[256]` static readonly initialisée via `SrgbToLinear(i / 255f)`, ~1 KB mémoire). Les trois sites d'averaging (`FrameSampler.ReadGridBGRA8`, `FrameSampler.ReadGridFP16`, `AmbientEngine.SampleZone`) somment en `float`/`double`, divisent par count, ré-encodent via `LinearToSrgb`. LUT plutôt que `MathF.Pow` per-pixel (~30 k pow/s, mesurable mais inutile) et plutôt que l'approximation `x²` (gamma 2.0) qui biaise visiblement les mid-tones puisque la vraie gamma sRGB est piecewise avec exposant 2.4 hors du toe linéaire. LUT est plus simple et exact.

### `ApplyMinBrightness` reste en sRGB

Le scale multiplicatif `scale = minBri / max` appliqué sur les bytes sRGB pour relever la max-channel à `minBri` préserve la chromaticité par construction (les ratios R:G:B en sRGB-space sont conservés, la matrice Philips est linéaire). Le seul biais théorique est sur la perception de luminance, déjà géré par le fait que `bri` Hue est dérivé de `max(R,G,B)` et non de `Y` (decoupling chromaticité/brightness intentionnel, commenté en tête de `HueColorMath.cs`). Refonte non justifiée.

### `ApplySaturationBoost` en OKLCh

HSV n'est pas perceptuellement uniforme : à `V=0.5`, un yellow `H=60°` a une luminance perçue ≈ 0.93, un blue `H=240°` ≈ 0.07. Un boost de saturation modifie la perception de luminance différemment selon le hue — un boost ×1.5 sur un yellow le rend plus brillant, sur un blue le rend plus sombre. Sur la lampe ambient, ce biais se traduit par des bleus qui paraissent affadis quand on monte le boost pour saisir les rouges. OKLCh est perceptuellement uniforme par design (Björn Ottosson 2020) : à `L` constant, modifier `C` (chroma) préserve la luminance perçue sur tout le wheel. Pipeline sRGB byte → linear via LUT → cone responses cube root → OKLab → OKLCh, via `ColorSpace.RgbToOklch` symétrique de `OklchToRgb`. `ApplySaturationBoost` opère donc en `RgbToOklch → C *= boost → OklchToRgb` avec early-out `boost == 1.0`. Cohérence cross-modules : le projet a déjà fait le choix OKLCh pour la stroke conique HUD pour exactement cette raison.

### Anti-patterns écartés

- **Projection vers white-point D65 pour gamut mapping.** Désature le hors-gamut au lieu de le clipper sur le corner le plus proche. Ne résout pas le bug Night Owl puisque la traversée passe par l'arête B-G, même rendu turquoise.
- **Gamma 2.0 (`x²`) pour économiser le `Pow`.** Biais visible sur les mid-tones puisque la vraie gamma sRGB est piecewise avec exposant 2.4 hors du toe linéaire. LUT est plus simple et exact.
- **Boost saturation en HSV avec correction luminance ad-hoc.** Tentation de compenser l'asymétrie HSV par un facteur correctif sur V. Réinvente OKLCh en pire, perd la symétrie wheel.
- **Refonte de la matrice Philips Wide Gamut → sRGB.** La matrice actuelle est correcte (référencée developer.meethue.com), pas la cause du bug. Toucher uniquement le gamut mapping.

### Doctrine Windows native

Aucune primitive Windows native ne couvre xy → Hue Gamut C. WCS (Windows Color System) et Direct2D Color Management sont ICC-profile based, orientés display calibration — pas le clip vers un triangle propriétaire Philips. Code maison justifié.

### Vérification empirique

L'évaluation perceptuelle se fait par photo iPhone fixe (manuel ISO/expo, distance et cadrage reproductibles) cadrée lampe + écran dans le même frame, sur trois scènes calibrées avant patch et après chaque étape mesurable. **Scène 1 — Night Owl `#011627` plein écran statique** : critère succès, bleu profond reste bleu sur lampe, pas turquoise. **Scène 2 — ciel HDR jour** (capture Forza Horizon menu plage sur display HDR1000) : teinte chaude préservée, pas de dérive cyan, exposure adaptative continue de mordre sans crush. **Scène 3 — scène jeu HDR sombre** (Cyberpunk 2077 night drive) : reste dark avec teinte fidèle, pas d'amplification noise, lampe n'allume pas sur des highlights spéculaires isolés. Validation math `ClipToGamutC` avant câblage runtime : 3-4 cas dans une méthode test inline (in-gamut central D65 identité, juste hors blue corner projection arête B-G, hors red corner projection arête R-G, central white identité).

## Discovery, pairing, control Hue

Trois phases distinctes au cycle de vie d'un bridge depuis le côté driver.

**Discovery** via cloud lookup `discovery.meethue.com` (HTTPS sans cert pinning). Retourne `0..N` bridges avec leur `bridge_id` (serial number hex16) et leur `bridge_ip` (LAN local IPv4). Fallback manuel IP si la découverte cloud échoue.

**Pairing** via CLIP v1 — l'utilisateur presse le bouton link sur le bridge, le driver `POST /api` avec un device-type identifiant l'app, et reçoit un `username` (application key REST) et un `clientkey` (PSK DTLS pour Entertainment v2, jamais affiché en clair). Timeout 30 s ; pendant l'attente, `error 101` est normal (link not pressed yet) et reste `Verbose`.

**Control** via HTTPS bypass cert — le bridge utilise un certificat auto-signé avec le serial number comme common name, `HttpClientHandler.ServerCertificateCustomValidationCallback` bypass configuré en dur. Endpoints CLIP v1 utilisés : `PUT /groups/{id}/action` (single-colour group push), `PUT /lights/{id}/state` (per-light push), `GET /groups` (listing), `GET /lights` (listing). Endpoints CLIP v2 utilisés : `GET /resource/entertainment_configuration` (récupère les positions XYZ stockées par light, alimente le `LightZoneSuggester` côté Ambient), `GET /resource/light` et `GET /resource/grouped_light` (récupèrent la carte v2_uuid → v1_id nécessaire à la résolution des events EventStream — voir la section dédiée plus bas). `tt_ds` (Hue `transitiontime` en décisecondes, 1 = 100 ms) est forcé à 1 par le driver ambient pour override le default factory 4 (= 400 ms) qui laggerait la lampe.

## EventStream v2 — détection des commandes externes

Le bridge expose un flux SSE `GET /eventstream/clip/v2` (header `hue-application-key`, payload `text/event-stream`) qui pousse les changements d'état de toutes les ressources en quasi-temps réel. Le driver consomme ce flux via `HueBridgeClient.StreamEventsAsync(onUpdate, ct)` — long-running task lancée par `AmbientEngine` au démarrage de l'engine, reconnect 2 s sur toute fermeture (clean ou erreur réseau), termine sur cancel. `System.Net.ServerSentEvents.SseParser<T>` natif .NET 10 fait le parsing — zéro NuGet tiers.

Le but est de détecter quand une commande externe (app Philips Hue, Home Assistant, bouton physique Dimmer Switch, voice assistant, activation de scène) modifie une lampe gérée, et de **stopper proprement l'engine** plutôt que d'essayer de reprendre la main. Tenter le reclaim (re-push immédiat pour écraser le changement externe) avait été essayé en V0 et écarté : trop fragile (certaines transitions de scène ne firent pas d'event individuel reclaimable, le bridge écrase notre push), et surtout l'expérience désirée n'est pas une guerre de pushes — c'est que l'utilisateur sache quand son ambient s'est arrêté pour une raison externe. La logique vit côté `AmbientEngine.OnResourceUpdate` : log `ExternalChangeStopped`, puis `Task.Run(Stop)` pour marshal hors du thread SSE. La gestion utilisateur de cette notification (toast, dialog, banner) appartient à une passe gestion d'erreurs future ; pour l'instant le toggle se rabat off et la LogWindow porte la raison.

**Discrimination self vs externe.** Le bridge re-emette nos propres `PUT` vers le flux SSE — sans discrimination on aurait un faux positif qui stopperait l'engine sur chacun de nos propres pushes. Le pattern retenu est *timestamp local* : `AmbientEngine` track `DateTimeOffset.UtcNow` au moment où chaque push réussit, namespaced par `group:<v1_id>` ou `light:<v1_id>`. À la réception d'un event, l'engine compare `UtcNow` à ce dernier push timestamp pour la ressource concernée — si l'écart est < 300 ms (`EchoWindow`), c'est notre propre echo, on ignore. Au-delà, c'est un changement externe et on stoppe. Comparer à `UtcNow` plutôt qu'à `event.creationtime` évite tous les problèmes de skew horloge bridge/host : les deux timestamps sont dans la même horloge.

**Carte v2 ↔ v1.** Les events EventStream portent les ids v2 (UUIDs), alors que le push REST utilise les ids v1 (integers). `HueBridgeClient.FetchV2IdMapsAsync` fait un fetch unique au démarrage de l'engine et retourne deux dicts (`Lights` v2_uuid → v1_id, `GroupedLights` v2_uuid → v1_group_id) que l'engine cache pour toute la session. Si une lampe est ajoutée au bridge mid-session, on rate son event — acceptable, le mapping refresh au prochain `StartAsync`. Si le fetch échoue à l'init (rare, vieux firmware ou réseau bizarre), l'engine log `ReclaimSetupFailed` Warning et continue sans détection externe — le push normal fonctionne quand même, juste sans stop automatique sur commande externe jusqu'à la prochaine session.

**Filtrage côté consumer.** L'engine ignore les events qui ne touchent pas une ressource gérée. En group mode, seuls les events `grouped_light` du `_managedGroupId` actuel comptent — les events `light` individuels sont du bruit (on ne pousse pas par lampe). En multi-light mode, c'est l'inverse : seuls les events `light` pour une lampe présente dans `_multiLights` comptent — les events `grouped_light` sont du bruit. La séparation évite les doubles-déclenchements lors d'un `PUT /groups/{id}/action` qui génère naturellement un event group plus N events lights.

**Pas de force-push, pas de reclaim.** Deux alternatives écartées. (1) Force-push périodique toutes les 2 s même quand le dedup aurait filtré, pour écraser n'importe quelle modification externe sans signal explicite — rejeté : guerre de pushes contre l'utilisateur qui touche son app Hue, surcharge inutile du bridge en régime statique, hack là où le bridge expose un signal event-driven natif. (2) Reclaim event-driven via le SSE — armer un flag et forcer le prochain push à passer le dedup pour écraser la commande externe — rejeté en test : les scènes complexes ne firent pas toujours un event individuel par lampe, et l'expérience perceptuelle d'une lampe qui clignote 100 ms vers l'état externe avant de revenir était pire que la lampe qui reste sur l'état externe. La doctrine projet est *corriger à la base, pas patcher périodiquement* — et la base ici c'est *honorer le choix de l'utilisateur, ne pas se battre avec lui*.

## Sécurité — secrets et données sensibles

Le `clientkey` retourné par le bridge au pairing est une PSK qui servira au tunnel DTLS Entertainment v2 si jamais activé. Il est traité comme un secret : jamais émis en clair dans un event EventSource, jamais persisté en JSON non chiffré sans avertissement. Le `username` (application key REST) est moins sensible mais reste tronqué à 8 chars + `...` dans les emissions pour minimiser l'exposition dans des screenshots de support. L'IP du bridge est validée par `IsAcceptableBridgeIp` (RFC1918 + APIPA) pour prévenir le SSRF avant le `PUT` runtime.

## Observabilité

Toutes les émissions passent par `DeckleLightingSource.Log` — provider `Deckle.Lighting` exposé en singleton statique, tag LIGHTING dans la LogWindow. Le module a vocation à abstraire à terme plusieurs drivers (WLED, DMX, HomeAssist) ; le provider est unique pour le module entier et les futurs drivers ajouteront leurs events sous le même provider plutôt que de créer un `Deckle.Lighting.*` enfant.

## Threading et lifetime

`HueBridgeClient` expose des méthodes `async Task<...>`. Discovery, pairing et control sont async sur le pool d'I/O .NET — pas de marshalling UI nécessaire côté driver. Le consommateur (Playground ou `AmbientEngine`) appelle ces méthodes depuis n'importe quel thread, en attendant le résultat ; la couche UI marshale avec `DispatcherQueue.TryEnqueue` quand elle reflète l'état dans des controls XAML. `HueRestLightOutput` est `IAsyncDisposable` — la fermeture libère l'`HttpClient` interne ; le `username` reste valide côté bridge tant que l'utilisateur ne le révoque pas manuellement via l'app Hue. La rétention du `username` entre sessions est une décision du consommateur (Playground transient, AmbientSettings persistant).

## Pointeurs

- [src/Deckle.Lighting.Ambient/](../Deckle.Lighting.Ambient/) — consumer principal du driver, pilote le push loop et le sampling écran.
- [Philips Hue Developer — Color Conversion Formulas](https://developers.meethue.com/develop/application-design-guidance/color-conversion-formulas-rgb-to-xy-and-back/) — matrice Wide Gamut + Gamut C corners.
- [Björn Ottosson — A Perceptual Color Space for Image Processing](https://bottosson.github.io/posts/oklab/) — OKLab / OKLCh.
