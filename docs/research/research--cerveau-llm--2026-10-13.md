---
description: "Cadrage vérifié (2026-10-13) — un cerveau LLM dans Deckle.Llm : les crédits API mensuels des plans Max, ce que le code a déjà (la couture moteur, le coffre, les deux modules), ce qui est tranché sur ONNX, la forme proposée, les décisions prises à la place de Louis, et les objets Anytype à créer quand l'espace sera joignable."
---

# Cerveau LLM — brancher Claude derrière la couture, centraliser les moteurs

Commande de Louis (dictées du 2026-10-13) : brancher l'API Claude sur la réécriture tout de suite, pour tester vite avec les crédits API mensuels des plans Max, et garder les modèles locaux pour plus tard. Centraliser l'inférence dans « les services des LLM » pour choisir à la fin vers qui on envoie, par consommateur : par exemple la réécriture de transcription avec Claude, le reste avec autre chose, ONNX ou Mistral quand on veut. Profiter de la passe Settings pour séparer les réglages propres à Ollama des autres. Il nomme cette partie « cerveau » : modèles, inférence, utilisable par les autres modules.

Session de cadrage seulement : aucun code touché, l'espace Anytype était injoignable à la clôture (ECONNRESET sur le serveur MCP anytype). Ce brief tient lieu de note jusqu'à la création du chantier. Chaque point est marqué **établi**, **interprétation** ou **non vérifié**.

## 1. Les crédits API

**Établi, sources tierces plus une page de support Anthropic.** Depuis le 7 octobre 2026, les plans Max incluent des crédits API mensuels : $100 pour Max 5x, $200 pour Max 20x. Ils servent sur l'API Messages, les Managed Agents et l'Agent SDK. Ils ne servent ni dans Claude Code, ni dans l'app de chat, ni sur Bedrock, Vertex ou Foundry. Ils expirent chaque mois. La page de support confirme seulement la séparation entre crédits d'usage et crédits API ; le reste vient de couvertures tierces.

Pour Deckle, c'est le bon usage : une réécriture est un appel à l'API Messages. Le plan Max de Louis n'est pas connu, donc $100 ou $200.

**Coût par dictée, ordre de grandeur (interprétation).** Dix minutes de parole font environ 2 500 tokens dans chaque sens.

| Modèle | Coût par dictée | Dictées pour $200 |
|---|---|---|
| Opus 5.5 | ~$0,06 | ~3 300 |
| Sonnet 5.5 | ~$0,03 | ~6 600 |
| Haiku 5.5 | ~$0,002 | ~100 000 |

Sources : support.claude.com/en/articles/12429409, mixed-news.com/en/claude-max-team-monthly-api-credits-not-claude-code, justinmckelvey.com/blog/claude-api-credits.

## 2. Ce que le code a déjà

**Établi.** Deux modules portent déjà la séparation que Louis décrit.

- `Deckle.Llm` : sans WinUI, dépend de Core et Diagnostics seulement. Contient `OllamaService` (administration et santé d'Ollama) et la source de logs. Son csproj dit : les futurs consommateurs dépendent de ce module sans tirer l'UI de réécriture.
- `Deckle.Llm.Rewrite` : au-dessus, avec `RewriteService`, les profils, les prompts, la porte de diff, la page Settings LLM et la persistance `modules/llm/settings.json`.
- Dépendances à sens unique : Transcription dépend de Rewrite, qui dépend de Llm. App dépend des deux.

**Établi.** La couture moteur existe : `IRewriteEngine` dans `src/Deckle.Llm.Rewrite/Engine/RewriteEngine.cs`, une seule implémentation `OllamaEngine`, et `RewriteService(IRewriteEngine)` accepte n'importe quel moteur. Elle est synchrone exprès ; les appelants sont sur un thread de fond et OllamaEngine bloque déjà avec `GetAwaiter().GetResult()`.

**Établi.** Le coffre `ISecretVault` existe dans `Deckle.Security`, et son commentaire prévoit les clés tierces de réécriture. Deux consommateurs montrent le motif : `AnytypeCredentials.Load(ISecretVault)` et `HueCredentialVault`.

**Établi.** Trois chemins passent par le service : la finalisation de transcription (profil actif), la reprise de paragraphe et l'escalade de phrase de l'autocorrect. Les deux derniers prennent du texte tapé dans n'importe quelle app enrôlée et utilisent un modèle fixe, `ParagraphRewrite.Model = "ministral-3:14b"`, hors profils.

**Établi, ce qui manque.**

- Aucun réglage ne choisit le moteur ; le constructeur par défaut crée Ollama en dur.
- Les champs Temperature, NumCtx, TopP, RepeatPenalty de la requête sont des hints Ollama. Opus 5.5 rejette temperature et top_p. Le moteur Claude les ignore ; le record le dit déjà.
- Les trois prompts par défaut ont été optimisés pour Ministral 14B Q4, avec majuscules et répétitions. Claude lit moins bien un prompt sur-prescrit. Un profil Claude veut son propre prompt, plus court.
- `RewriteResult.OllamaLoadMs` porte le nom d'un moteur.

## 3. ONNX, ce qui est tranché

**Établi.** Les juges ONNX d'autocorrect (`Deckle.Autocorrect.Onnx`, `Deckle.Autocorrect.Mlm`) sont insérés sur place. Ils notent des candidats fermés : une Correction au sens du CONTEXT racine, pas une génération. Ils ne bougent pas.

**Établi, mais seulement dans un glossaire.** `src/Deckle.Llm.Rewrite/CONTEXT.md` dit : cible décidée ONNX in-process pour le moteur de réécriture, Ollama jusqu'à cette migration. Aucun ADR dans `docs/adr` ne le fige. Le souvenir de Louis (« pas de truc pour ONNX, l'insérer à droite à gauche ») est juste pour les juges, pas pour la génération.

**Interprétation.** Un moteur ONNX génératif sera un troisième moteur derrière la même couture, ni plus ni moins.

## 4. La forme proposée

Posture : ingénieur et product manager.

- Dans `Deckle.Llm` : une interface de génération, prompt et texte en entrée, texte et métriques en sortie. Derrière, les moteurs : Ollama aujourd'hui, Claude ensuite, ONNX ou Mistral plus tard. Un registre les nomme par clé (« ollama », « claude »). Le cerveau possède les réglages de fournisseurs : endpoint Ollama, présence de la clé Claude dans le coffre.
- Dans `Deckle.Llm.Rewrite` : chaque profil nomme son moteur en plus de son modèle et de son prompt. Le paragraphe et la phrase gardent une constante de moteur à côté de `ParagraphRewrite.Model`. Changer où part un chemin, c'est une ligne.
- Renommages : la requête perd « Rewrite » dans son nom ; `OllamaLoadMs` perd son préfixe.
- Claude : SDK C# officiel (`dotnet add package Anthropic`), `client.Messages.Create` non streamé, bloqué sur le thread de fond comme OllamaEngine, clé lue dans le coffre, `claude-opus-5-5` par défaut, les autres modèles au choix du profil. Vérifier la version du paquet sur NuGet avant d'écrire.
- Justification face au cap du projet (AGENTS.md, émancipation des services payants) : moteur optionnel, derrière la couture, clé dans le coffre, le local reste le défaut. Louis l'a posé lui-même comme temporaire.

**Réglages.** La doctrine deckle-modularite veut que chaque module porte sa page et sa persistance. Si le cerveau possède les fournisseurs, il possède leur page, mais Deckle.Llm est sans WinUI exprès. Non tranché dans la première passe : la clé Claude et le choix de moteur par profil s'ajoutent sur la page LLM actuelle ; la séparation Ollama / autres rejoint « Settings — finition », où « Migrations vers le composer » est encore ouverte.

**Plus tard.** Le CONTEXT dit que le service de réécriture est l'endroit naturel pour sérialiser le calcul local, parce que ses consommateurs partagent un GPU. Si les moteurs descendent dans le cerveau, cette sérialisation descend avec eux.

**Question ouverte pour Louis.** Le seul consommateur génératif dit aujourd'hui « réécris ce texte avec ce prompt ». Si un module futur dit « réponds à cette question », la même requête suffit-elle, ou faut-il une conversation ? La réponse décide si la couture reste un aller simple.

## 5. Décisions prises à sa place

Louis a demandé de « trouver tout seul tout le reste » à la clôture.

- **Nom du chantier :** « Cerveau LLM », son mot.
- **Périmètre vie privée :** la transcription part vers Claude quand son profil le dit ; le paragraphe et la phrase restent en local. Lecture de « réécriture du texte avec Claude et le reste avec autre chose ». À confirmer par lui au premier retour.
- **Plan Max :** inconnu, sans effet sur le travail.

## 6. Objets Anytype à créer

L'espace était injoignable. À créer en ouverture de la prochaine session, avant tout code.

**Projet « Cerveau LLM »**, état ouvert, priorité 4.
Description : Louis (2026-10-13) veut brancher l'API Claude sur la réécriture tout de suite, avec les crédits API mensuels Max, et centraliser l'inférence dans Deckle.Llm pour choisir le moteur par consommateur ; les modèles locaux reviennent une fois testés. Brief : `docs/research/research--cerveau-llm--2026-10-13.md`.
Définition de fini : la couture moteur vit dans Deckle.Llm avec un registre par clé ; un moteur Claude sur le SDK officiel lit sa clé dans le coffre ; chaque profil de réécriture nomme son moteur et la transcription se réécrit par Claude en usage réel ; le paragraphe et la phrase restent en local ; la séparation des réglages Ollama / autres est tranchée avec « Settings — finition ».
Date cible : 2026-10-31. Budget estimé : 3 sessions. Charge estimée : 2 jours.

**Tâches**, dans l'ordre, chacune un commit qui compile :

1. « Descendre la couture moteur dans Deckle.Llm » · Produire. Fini : IRewriteEngine, OllamaEngine, PromptTemplates et les records vivent dans Deckle.Llm sous un nom de génération, Rewrite les consomme, aucun comportement ne change, build x64 propre.
2. « Moteur Claude sur le SDK officiel » · Produire. Fini : ClaudeEngine implémente la couture, clé lue dans le coffre, erreurs typées du SDK traduites en RewriteUnavailable, usage tokens dans le résultat.
3. « Registre de moteurs et moteur par profil » · Produire. Fini : un registre nomme les moteurs par clé ; RewriteProfile porte Engine ; le paragraphe et la phrase ont leur constante de moteur ; migration des settings existants vers « ollama ».
4. « Clé Claude et sélecteur de moteur sur la page LLM » · Produire. Fini : un champ clé écrit dans le coffre sans l'afficher, un sélecteur de moteur par carte de profil, doctrine deckle-settings-ux respectée.
5. « Prompt allégé pour un profil Claude » · Produire. Fini : un brouillon de profil Claude livré, réglé par Louis sur ses dictées.
6. « Séparer les réglages Ollama des autres » · Échanger, liée à « Settings — finition ». Fini : la place de la page des fournisseurs est tranchée, Deckle.Llm avec ou sans WinUI.

**Rapport de cette session (2026-10-13)** à accrocher à la tâche 1 : cadrage seul, aucun code ; vérification des crédits, lecture de la couture, des deux modules, du coffre et des trois chemins ; ONNX clarifié ; forme proposée et décisions prises à la place de Louis ; Anytype injoignable à la clôture.

## 7. Prochaine session

Créer les objets Anytype ci-dessus, ouvrir le worktree `D:\worktrees\deckle\cerveau-llm`, puis la tâche 1.
