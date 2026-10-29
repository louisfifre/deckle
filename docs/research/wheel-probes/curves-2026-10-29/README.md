# Courbes de la molette du 29 octobre 2026

La page de courbes publiée sur https://claude.ai/artifact/BcpQDCTkdNthYSm8StMdam, gardée ici pour qu'elle survive à l'artifact. Elle compare les sessions étiquetées S1 (MX, Precision Scrolling off), S3 (MX, on), S4 (touchpad, off) et S6 (Steam, off) : position message après message, vitesse en crans par seconde, rythme entre deux messages, chiffres par source.

| Fichier | Rôle |
|---|---|
| `build_curves_data.py` | Construit les deux CSV depuis la capture `wheel-events-20261029-131052.jsonl` et `sessions\sessions.jsonl` du dossier de télémétrie. Les étiquettes de session et la règle de source (multiple de 120 ou fraction, injecté ou non) y sont écrites en dur pour cette étude. |
| `wheel_events_20261029.csv` | Un message de molette par ligne : session, source, temps, delta, position cumulée en crans, vitesse sur 100 ms, écart avec le précédent. |
| `wheel_summary_20261029.csv` | Une ligne par session et par source. |
| `curves-page.html` | Le code de la page. Il tourne dans le type Dashboard des artifacts (API `dash`, d3 v7), pas seul dans un navigateur. |

Ce que ces courbes ont établi, et la décision prise, sont dans la tâche Anytype « Sources de défilement plus fines pour Precision Scrolling » (projet Touchpad virtuel).
