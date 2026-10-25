# Kit de mesure de la molette

Écrit le 2026-10-23 pour la tâche Anytype « Sources de défilement plus fines pour Precision Scrolling » (projet Touchpad virtuel), perdu avec le dossier de notes de sa session, puis reconstitué le 2026-10-25 depuis le transcript de cette session et rangé ici pour qu'il ne disparaisse plus. Il ne touche pas au code de Deckle. Il se lance avec `pwsh -File` depuis n'importe quel dossier.

Ce kit sert à capturer des sessions de molette **étiquetées** (souris MX avec ou sans lissage Options+, avec ou sans Precision Scrolling, trackpad, manette DualSense) puis à les analyser, session par session, quand tu as le matériel sous la main. Tu peux faire les sessions dans n'importe quel ordre, sur plusieurs jours.

Principe : c'est l'enregistreur de Deckle qui capte les événements de molette. Ce kit note seulement **quand** chaque session a eu lieu (début et fin, heure locale avec décalage), puis l'analyse découpe les fichiers de l'enregistreur le long de ces fenêtres. Le kit n'envoie aucune entrée à Windows : il ne simule ni molette ni souris.

## Les trois étapes

1. **Allumer l'enregistrement** : Deckle, page Diagnostics, section Telemetry, interrupteur « Record wheel events ». Le fichier ne s'ouvre qu'au premier cran de molette après l'activation.
2. **Lancer le menu** : `pwsh -File docs\research\wheel-probes\wheel-session.ps1`. Choisis une session (S1 à S7), lis les consignes, réponds aux quelques questions, puis « Entrée pour commencer », fais les gestes, « Entrée quand tu as fini ». Une ligne est ajoutée à `sessions.jsonl`, dans `%LOCALAPPDATA%\Deckle\telemetry\mouse-wheel\sessions` : les étiquettes vivent à côté des captures brutes qu'elles découpent, hors du dépôt. Une session ratée se refait : rien n'est écrasé. Les gestes de chaque session et le matériel nécessaire sont rappelés à l'écran ; pendant une session, ne touche à aucun autre périphérique de pointage.
3. **Éteindre l'enregistrement, puis analyser** : l'extinction force l'écriture des derniers événements (sinon la fin du dernier fichier peut manquer). Relance le menu et choisis **A** : le rapport est écrit dans `sessions\report-<date>.txt`, avec un tableau comparatif en tête puis le détail de chaque session.

## Le menu

| Choix | Rôle |
|---|---|
| S1, S2, S3 | Souris MX : lissage Options+ dans l'état actuel, dans l'autre état, puis Precision Scrolling on |
| S4, S5 | Trackpad : Precision Scrolling off, puis on (avec essai du double défilement) |
| S6 | DualSense via Steam Input (config bureau) |
| S7 | Session libre, avec une étiquette que tu tapes |
| P | Sonde HID++ de la souris |
| A | Analyser les sessions enregistrées |

**P est en lecture seule** : `hidpp_probe.py` ne peut envoyer que des requêtes de lecture (liste blanche vérifiée avant chaque écriture), il ne modifie rien dans la souris ni dans Options+. Sa sortie est sauvegardée dans `sessions\probe-<date>.txt`. Son auto-test, sans matériel : `python -I hidpp_probe.py --self-test` (49 vérifications). Tu peux lui passer des arguments (par exemple `-v`, ou `--target bolt` / `--target bluetooth` ; par défaut elle essaie le Bolt puis le Bluetooth). **Bouge la souris juste avant** : endormie, elle ne répond pas et la sonde n'affiche que des délais dépassés. Le 23 octobre, sur le Bolt, seul le MX Keys Mini (index 3) a répondu ; la MX Master 3S est aussi appairée en Bluetooth.

## Bon à savoir

- Les heures absolues des événements = l'heure de début écrite dans l'en-tête du fichier + l'horloge monotone de l'événement. Décalage mesuré le 23 octobre : 1,5 ms sur un enregistrement de 3 h, négligeable. La fenêtre de chaque session est quand même élargie de 0,5 s de chaque côté.
- Si l'analyse annonce « NO EVENTS » pour une session, l'enregistrement était sans doute éteint à ce moment-là (ou pas encore vidé sur disque).
- Les fichiers : `wheel-session.ps1` (menu), `wheel_analysis.py` (analyse, `--sessions` pour ce mode), `hidpp_probe.py` (sonde). Tes données (`sessions.jsonl`, rapports, sorties de sonde) vont dans `%LOCALAPPDATA%\Deckle\telemetry\mouse-wheel\sessions`, ou ailleurs avec `-CapturesDir`.
