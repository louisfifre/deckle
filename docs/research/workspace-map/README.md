# Carte de l'espace de travail — prototype

Prototype écrit le 2026-11-09 pour la tâche « Enquêter sur un mode graphe (tableau blanc) pour voir l'espace de travail et ses liens », dans le chantier de l'atelier. Il teste l'idée avant d'en faire un mode de l'atelier WinUI 3. Il lit seulement : rien n'est modifié dans les dossiers parcourus.

L'espace se lit comme une carte qu'on zoome et qu'on parcourt, vue sous trois prismes. Un prisme est une question posée aux mêmes éléments :

- **Harnais** : ce que Claude Code charge, et d'où ça vient (liens symboliques, imports `@`, hooks, serveurs MCP, skills).
- **Scripts** : chaque script, rangé par dépôt puis par dossier, avec les appels de l'un à l'autre.
- **Copies** : les scripts et les skills qui existent à plusieurs endroits ; même lettre, même contenu.

| Fichier | Rôle |
|---|---|
| `inventory.ps1` | Parcourt `~/.claude`, `~/.agents`, `D:\skills` et `D:\projects` (les fichiers suivis par git), et écrit `data.js`. |
| `index.html` | Dessine `data.js` avec Cytoscape.js et sa disposition fcose, chargés depuis jsDelivr. |
| `data.js` | Généré, hors de git : il contient les chemins de la machine. |

Pour rafraîchir la carte, depuis n'importe quel dossier :

```powershell
pwsh -NoProfile -File D:\projects\deckle\docs\research\workspace-map\inventory.ps1
```

Puis ouvrir `index.html` dans un navigateur, ou le servir avec `python -m http.server --directory <ce dossier>` si le navigateur refuse les fichiers locaux.

Limites connues : un lien « appelle » est déduit du nom d'un script écrit dans un autre, donc un nom cité en commentaire crée un faux lien. `D:\bin`, `D:\worktrees` et les tests sous `tests/fixtures` ne sont pas parcourus.
