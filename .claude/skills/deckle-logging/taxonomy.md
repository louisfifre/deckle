# Taxonomie des observables — Deckle

Fichier compagnon de `deckle-logging`, chargé à la demande quand la procédure de décision atteint l'étape « quels observables ». Donne les cadres canoniques d'observabilité reconnus en industrie et leur application au profil Deckle (app desktop client mono-process avec des modules hétérogènes — pipeline batch de transcription, boucle temps réel de capture vidéo, drivers de matériel, surfaces UI).

## Trois cadres canoniques complémentaires

Ces cadres ne se concurrencent pas, ils couvrent des angles différents et se combinent selon ce qu'on instrumente.

### Four Golden Signals

Origine : Google SRE Book, chapitre *Monitoring Distributed Systems*. Quatre signaux à surveiller sur tout sous-système qui « sert » du travail à un appelant — qu'il soit interne à l'app ou externe.

**Latence.** Temps entre la demande et la réponse, en distinguant les succès des échecs (un échec rapide n'est pas un succès). Pour un pipeline interne, c'est la durée totale et la durée par étape clé.

**Trafic.** Volume de demande métier dans l'unité de temps. Pour Deckle, ce sont les déclenchements de transcription, les frames captures, les pushs lumineux, les actions utilisateur. La cardinalité reste faible.

**Erreurs.** Taux d'erreur, qu'il soit explicite (exception levée), implicite (code de retour anormal), ou par politique (résultat invalide selon une règle métier).

**Saturation.** Proximité de la contrainte la plus serrée du sous-système. Pour Deckle, ce sont les files internes (frames en attente, segments en cours), la mémoire GPU sur le modèle Whisper, le débit accepté par un bridge externe.

Philosophie sous-jacente : monitorer les **symptômes** que voit l'appelant, les causes ensuite. Cadre généraliste pensé pour les services mais transposable à tout sous-système Deckle qui produit un travail pour un autre.

### USE Method

Origine : Brendan Gregg, *Systems Performance*. Pour chaque **ressource** physique ou logicielle (CPU, mémoire, file d'attente, mutex, pool de buffers, descripteur de matériel), trois mesures.

**Utilisation.** Fraction du temps pendant laquelle la ressource est active à servir du travail. Pour Deckle : taux d'occupation du thread de capture, fraction du temps où le moteur Whisper calcule, fraction du temps où le bridge externe est en train de traiter une requête.

**Saturation.** Quantité de travail accumulé que la ressource ne peut pas encore servir — taille de file d'attente, profondeur du backlog. Pour Deckle : nombre de frames en attente d'analyse, segments accumulés non encore transcrits, requêtes externes en attente.

**Erreurs.** Erreurs spécifiquement liées à la ressource — handle invalide, allocation échouée, dépassement de capacité.

USE est orienté **ressource interne**. Il complète Four Golden Signals (qui est orienté service) en regardant la machine sous la surface.

### RED Method

Origine : Tom Wilkie, formalisée pour les architectures microservices. Trois mesures par **service** ou unité de traitement.

**Rate.** Taux d'opérations par unité de temps que l'unité traite.

**Errors.** Taux d'erreur sur ces opérations.

**Duration.** Distribution de durée des opérations, généralement en percentiles (médiane, p95, p99).

RED s'applique très bien aux **pipelines batch** internes de Deckle — un appel à transcription est une opération avec son taux, ses erreurs, sa durée. Aussi aux **intégrations externes** — un appel à un service LLM est une opération RED.

## Mapping catégories de code → cadres applicables

Quand on instrumente, identifier d'abord la catégorie du code que l'on touche, puis appliquer les cadres pertinents.

### Boucle temps réel haute fréquence

Capture d'écran à plusieurs dizaines de Hertz, push de lumière à 10-15 Hz, analyse audio en temps réel. Les opérations sont nombreuses, brèves, et l'enjeu est la stabilité du débit et l'absence de drops.

Cadres dominants : **USE** (utilisation du thread de capture, saturation de la file de frames, erreurs d'acquisition) et **Four Golden Signals** côté flux sortant (latence depuis l'acquisition jusqu'à la publication, taux effectif sortant, erreurs intra-boucle, saturation des consommateurs en aval).

Particularité : observer en agrégé sur une fenêtre glissante plutôt qu'événement par événement. Une boucle qui émet un log par tick à 60 Hz noie l'observation. Le bon pattern est un **rollup** périodique qui résume N ticks en une seule ligne (compte de ticks observés, drops, percentiles de latence intra-tick, etc.).

### Pipeline batch ou opération ponctuelle

Transcription d'un blob audio, réécriture d'un texte par un modèle, calibration d'un appareil. Une opération discrète avec un début, une fin, et un résultat.

Cadres dominants : **RED** (durée, taux, erreurs) et **Four Golden Signals** (latence end-to-end, erreurs détaillées, saturation si la ressource sous-jacente est partagée).

Particularité : le pattern dominant est la **ligne canonique de récapitulatif** par opération (*canonical log lines*), colocalisant les mesures clés — durée totale et par étape, métriques d'entrée et de sortie, outcome final. Son rôle vis-à-vis des jalons intermédiaires est défini dans le SKILL.

### Driver de matériel ou intégration externe

Pilote de microphone, client d'un service distant, intégration d'un appareil réseau. La frontière entre le code interne et un système externe sur lequel on a peu de contrôle.

Cadres dominants : **RED** sur les opérations externes (durée d'aller-retour, taux d'erreur, taux d'appel), **USE** sur les ressources internes consommées (pool de connexions, file d'attente côté driver), **Four Golden Signals** sur la santé globale de l'intégration.

Particularité : observer aussi les **événements de cycle de vie de la connexion** (découverte, appairage, ouverture de session, fermeture propre, perte de signal, reconnexion). Et les **codes de retour natifs** (code matériel, code HTTP, valeur HRESULT) avec une notation canonique stable. Les secrets et clés sensibles ne sont jamais observés en clair — tronqués ou masqués.

### Surface d'interface utilisateur

Fenêtre, page, contrôle interactif. L'enjeu est la fluidité perçue et la lisibilité des actions utilisateur.

Cadres dominants : **Four Golden Signals** adaptés (latence perçue depuis l'action utilisateur jusqu'au retour visuel, taux d'actions par session, erreurs visibles à l'utilisateur, saturation si la file de rendu déborde) et **RED** sur les opérations déclenchées par l'utilisateur.

Particularité : observer **les transitions d'état UI** (page chargée, dialogue ouvert, formulaire validé) en jalons concis. Les détails techniques (temps de rendu, nombre d'éléments, surface impactée) en verbose. Les UserFeedback adressés à l'utilisateur ont une famille propre — texte UX adressé à l'utilisateur final, pas mesure technique.

### Cycle de vie d'application

Démarrage, warm-up de ressources, première utilisation, mise en veille, fermeture. Les transitions globales de l'app.

Cadres dominants : **Four Golden Signals** dégradés (durée des étapes de boot, erreurs de démarrage, saturation si parallélisme). RED s'applique moins parce que les opérations sont uniques par cycle de vie.

Particularité : observer **chaque grand jalon en information** (chargement modèle, warm-up prêt, fermeture amorcée), avec un verbose détaillé qui capte les paramètres de la résolution (chemin du modèle, backend choisi, durée). Les erreurs au démarrage justifient un UserFeedback parce qu'elles bloquent l'utilisateur.

## Sous-natures d'observables fréquemment rencontrées

Les types de signaux qui reviennent dans le code Deckle. Le format canonique d'unité (suffixe, précision) est défini par la convention du projet et appliqué systématiquement.

**Temporelles.** Durée courte d'une opération en millisecondes, durée longue en secondes, timing de segment relatif, latence end-to-end, latence par étape, percentiles de distribution sur une fenêtre.

**Volumétriques.** Compte d'éléments traités (segments, frames, échantillons, tokens, caractères, mots), taille de buffer en octets, débit en éléments par seconde.

**Qualitatives.** Niveau audio en décibels relatifs à la pleine échelle, valeur RMS linéaire normalisée, confiance d'un segment, probabilité d'une classe (parole, silence), distribution percentile sur une session.

**États réseau et drivers externes.** Adresse IP, identifiant d'appareil, clé tronquée, code de retour HTTP, code natif matériel, valeur HRESULT, identifiant de session ou de groupe. Les secrets ne sont jamais observés en clair.

**États hardware et capabilités.** Nom de backend (GPU, CPU), nom de format pixel, identifiant de moniteur, identifiant de périphérique audio, dimensions de capture, nombre de buffers du pool.

**Outcomes et résultats.** Énumération d'issue (réussite, échec, abandon, ignoré), distinction explicite des succès et des échecs dans les mesures temporelles, classification par règle métier.

**Activité utilisateur.** Déclencheur (raccourci clavier, action de tray, toggle de paramètre), résultat de la requête (déclenché, ignoré pour cause occupée, ignoré pour cause non configuré), valeur avant et après une modification.

**Erreurs structurées.** Type d'exception, message court, contexte minimal de reproduction, mapping vers UserFeedback (rôle, sévérité) si l'erreur doit être signalée à l'utilisateur.

## Couverture maximale

Parcourir cette taxonomie **largement** : à chaque étape instrumentée, capter ce qui est observable, pas le strict minimum. La doctrine (couverture maximale, et le vocabulaire fermé qui la rend exploitable) vit dans le SKILL. Si une sous-nature manque parce qu'on observe une grandeur jamais vue jusqu'ici, l'ajouter au vocabulaire canonique avant usage.

## Pointeurs

- **`deckle-logging`** — skill parent qui appelle cette taxonomie à l'étape « quels observables » de sa procédure de décision.
- Les sources primaires des trois cadres (Google SRE Book, Brendan Gregg, Tom Wilkie) sont les références à consulter quand un cas dépasse le périmètre de cette taxonomie projet-locale.
