---
description: "Recherche vérifiée (deep-research, 2026-07-02) — d'où tirer le lexique globish qui amorce la restauration d'anglicismes du correcteur (phase 2) : FranceTerme confirmé de bout en bout, la chaîne de licence du dérivé SUBTLEX-US ISC non traitée, wordfreq comme alternative propre ; classement des combinaisons, choix au mainteneur."
type: research-report
---

# Lexique globish — d'où tirer les graines, et à quel prix de licence

Commande de Louis (grill du 2026-07-02) : vérifier, sans re-litiger, les sources du lexique globish qui amorce la restauration d'anglicismes du correcteur (phase 2) — le plan gravé est un dérivé JSON de SUBTLEX-US annoncé ISC, tronqué aux hautes fréquences, croisé avec FranceTerme comme inventaire d'anglicismes, filtré contre les formes qui collident avec un mot français ou une typo française plausible. Méthode : harnais deep-research, 5 angles parallèles, 20 sources primaires, 97 claims extraits, les 25 plus porteurs vérifiés par 3 votes adverses chacun — 24 confirmés, 1 réfuté, 8 findings après fusion. Chaque affirmation porte ses réserves ; un finding est une piste sourcée, pas un verdict — le choix final des sources reste au mainteneur.

## 1. FranceTerme — la prémisse confirmée de bout en bout

**La licence est bien la Licence Ouverte / Open Licence (Etalab), cohérente sur trois surfaces** : le jeu data.gouv.fr, le portail data.culture.gouv.fr (« Licence Ouverte (Etalab) ») et le site culture.gouv.fr (« tous les contenus de ce site sont sous licence etalab-2.0 »). Elle autorise reproduction, redistribution, adaptation et exploitation commerciale, avec pour seule obligation substantielle l'attribution (source + date de dernière mise à jour) — ni share-alike ni clause non commerciale. La prémisse licence du plan est confirmée pour alimenter un lexique construit localement. [data.gouv.fr, data.culture.gouv.fr, culture.gouv.fr, alliance.numerique.gouv.fr]

**Le dump intégral est un fichier XML unique d'environ 8,5 Mo** (8 933 165 octets vérifiés en direct), via `https://www.data.gouv.fr/api/1/datasets/r/4e1b0ebe-e40f-4ce2-beaa-ac5a60d20899` — une redirection 302 vers `franceterme.culture.gouv.fr/public/FranceTerme.xml` (une seule origine, pas deux miroirs), Last-Modified 13 mars 2026, rafraîchi après chaque publication au Journal officiel. **Piège identifié** : le jeu Opendatasoft sur data.culture.gouv.fr est VIDE (0 enregistrement, export CSV réduit à l'en-tête) — le pipeline d'artefacts doit consommer le XML, jamais l'API records de ce portail. [data.gouv.fr, data.culture.gouv.fr, culture.gouv.fr]

**FranceTerme convient comme inventaire d'anglicismes, avec une nuance structurelle** : c'est la base officielle d'État des termes recommandés au Journal officiel par la Commission d'enrichissement de la langue française, près de 10 000 termes. Les entrées de tête sont les termes FRANÇAIS ; les anglicismes vivent dans le champ « équivalent étranger », majoritairement anglais (big data, cold case, greenwashing, hater), avec une minorité d'équivalents non anglais et des entrées sans équivalent. L'inventaire dérivable est donc borné à ~10 k et en pratique plus petit ; l'extraction cible le champ équivalent, pas les têtes. [culture.gouv.fr, data.gouv.fr]

## 2. Le dérivé SUBTLEX-US ISC — filiation nette, chaîne de licence non traitée

**Le dérivé visé est identifié sans ambiguïté** : `words/subtlex-word-frequencies` (GitHub, publié sur npm sous le même nom, v2.0.0 de 2020), un tableau JSON de 74 286 objets `{word, count}` triés par fréquence décroissante, dérivé de SUBTLEXus (corpus de sous-titres américains ~51 M mots de Brysbaert & New). Le compte de 74 286 correspond exactement à la liste complète officielle distribuée par UGent — empreinte de filiation. [github.com/words, npmjs, ugent.be]

**L'ISC affiché est une assertion unilatérale du réempaqueteur** : copyright 2015 Zeke Sikelianos (pas les auteurs de SUBTLEX-US), texte ISC standard « for any purpose ». Ni le README, ni le fichier license, ni le paquet npm, ni l'historique du dépôt ne mentionnent les conditions de SUBTLEX-US, une restriction non commerciale, un devoir de citation envers Brysbaert & New, ni une permission de relicenciement — la chaîne de licence n'est tout simplement pas traitée par cette source. [github.com/words, npmjs]

**Les conditions amont sont cadrées recherche, pas ouvertes** : le papier canonique (Brysbaert & New 2009) distribue les normes « freely available for research purposes » et lie cette mise à disposition à des financements « educational, noncommercial ». La page UGent, elle, n'énonce strictement aucun terme (aucune occurrence de license/copyright/commercial/free/research purposes) — donc copyright par défaut faute de termes énoncés. Rien ne fonde un relicenciement ISC d'un dérivé ; rien ne formalise non plus une interdiction explicite. [ugent.be, brysbaertnew.pdf]

**Le précédent wordfreq confirme le caractère restrictif du régime par défaut** : Robyn Speer a dû obtenir une permission e-mail explicite de Marc Brysbaert pour redistribuer les listes SUBTLEX « to be used for any purpose, not just for academic use », sous deux conditions — créditer les auteurs SUBTLEX et maintenir clair que SUBTLEX est librement disponible. Cela rend implausible qu'un dérivé indépendant soit valablement ISC sans grant équivalent, et fournit du même coup la voie de régularisation. [github.com/rspeer/wordfreq]

**ATTENTION — claim réfuté 0-3** : « la restriction amont s'écoule nécessairement vers le dérivé ISC, quelle que soit la licence déclarée » ne survit pas à la vérification. Ni la validité ni l'invalidité de l'ISC n'est tranchée : le verdict est documentaire, pas juridique. Deux théories non testées tirent en sens inverse — des comptes de fréquence bruts pourraient être des faits non protégeables aux États-Unis (Feist), mais le droit sui generis des bases de données UE/France, contexte de Deckle, coupe dans l'autre sens.

## 3. wordfreq — l'alternative à chaîne de licence propre

**wordfreq est l'alternative anglaise documentée** : code Apache-2.0, données embarquées redistribuables sous CC BY-SA 4.0 (attribution + share-alike, aucune clause non commerciale), la part SUBTLEX couverte par la permission Brysbaert attestée. Les listes anglaises s'extraient hors-ligne par une commande mainteneur via `top_n_list(lang, n)` et `get_frequency_dict(lang)` (données msgpack ; `small_en` et `large_en` présents) — un JSON tronqué haute fréquence se génère directement. Limite : projet en sunset, données figées vers 2021. [github.com/rspeer/wordfreq]

## 4. Classement des combinaisons — options, choix au mainteneur

**(A) wordfreq × FranceTerme** — chaîne de licence propre et documentée de bout en bout, extraction hors-ligne triviale. Coût : obligations attribution + share-alike côté wordfreq, et données figées ~2021.

**(B) subtlex-word-frequencies × FranceTerme** — format JSON prêt à l'emploi, mais chaîne de licence non traitée par le paquet ; à n'assumer qu'en connaissance de cause.

**(C) sécuriser SUBTLEX-US par une permission directe** à Brysbaert, à la manière de Speer — rendrait le débat sur le dérivé ISC sans objet.

## 5. Trous restants — ce que cette passe n'a PAS tranché

- **Autres listes candidates** (Google Books ngrams, listes de fréquence Wiktionary, dérivés OpenSubtitles bruts type `hermitdave/FrequencyWords`, COCA) : sources identifiées mais aucun claim vérifié — une passe dédiée est nécessaire avant un classement définitif, notamment sur la couverture « globish technique / noms de marque ».
- **Prior art anti-collision homographes FR/EN** (chat, pain, coin ; typos françaises plausibles) : rien n'a survécu à la vérification ; pistes probables côté littérature correcteurs (hunspell, autocorrect clavier mobile) et CLEARPOND (base de voisins orthographiques cross-langue), identifiés mais non exploités.
- **Propagation du share-alike CC BY-SA 4.0** : se propage-t-il à l'artefact lexical compilé embarqué dans Deckle, et quelle surface d'attribution une app Windows hors-ligne doit-elle exposer (à-propos, fichier NOTICE) ? Non analysé.
- **Signal de chaîne brouillée** : le miroir openlexicon (co-maintenu par Boris New, co-auteur SUBTLEX) étiquette SUBTLEX-US « CC-BY-SA » — étiquette tierce non canonique, à ne pas prendre pour la licence d'origine.
- **Sensibilité temporelle** : toutes les URL vérifiées en direct le 2026-07-02 ; le XML FranceTerme bouge après chaque JO ; la version exacte de la Licence Ouverte du jeu data.gouv.fr (1.0 vs 2.0) n'est pas épinglée, les deux étant attribution-seule.

## Sources principales

- Dérivé JSON — github.com/words/subtlex-word-frequencies : https://github.com/words/subtlex-word-frequencies ; npm : https://www.npmjs.com/package/subtlex-word-frequencies
- SUBTLEX-US amont — UGent : https://www.ugent.be/pp/experimentele-psychologie/en/research/documents/subtlexus ; Brysbaert & New 2009 : https://www.ugent.be/pp/experimentele-psychologie/en/research/documents/subtlexus/brysbaertnew.pdf
- wordfreq — Speer : https://github.com/rspeer/wordfreq
- FranceTerme — data.gouv.fr : https://www.data.gouv.fr/datasets/base-franceterme-termes-scientifiques-et-techniques-1 ; data.culture.gouv.fr : https://data.culture.gouv.fr/explore/dataset/base-franceterme-termes-scientifiques-et-techniques/ ; culture.gouv.fr : https://www.culture.gouv.fr/Sites-thematiques/Langue-francaise-et-langues-de-France/Politiques-de-la-langue/Developper-et-enrichir-la-langue-francaise/FranceTerme-trouver-l-equivalent-francais-d-un-terme-etranger
- Licence Ouverte (Etalab) — DINUM : https://alliance.numerique.gouv.fr/licence-ouverte-open-licence/
- Étiquette tierce non canonique — openlexicon : http://openlexicon.fr/datasets-info/SUBTLEX-US/README-SUBTLEXus.html
- Non exploitées (à couvrir en passe dédiée) : OpenSubtitles bruts https://github.com/hermitdave/FrequencyWords ; Wiktionary frequency lists https://en.wiktionary.org/wiki/Wiktionary:Frequency_lists ; CLEARPOND https://clearpond.northwestern.edu/
