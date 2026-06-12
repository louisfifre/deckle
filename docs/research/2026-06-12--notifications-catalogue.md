# Notes de recherche — module catalogue de notifications

Date : 2026-06-12. Source : session de recherche + grilling partiel (deux passes de recherche externe, une cartographie code, trois échanges de design). Statut : **rien n'est tranché** — tout ce qui suit est matériau et suggestions, sauf la section « Pièges » qui rapporte des faits vérifiés dans la doc officielle et qu'il est inutile de re-chercher.

## Le besoin (formulation de Louis)

Un module centralisé de gestion des messages utilisateur. Quand un module métier se construit et qu'on identifie ses cas d'alerte (« il faut alerter pour ça, ça, ça »), le module ne formule pas son message à la volée : il invoque une forme de message bien définie dans le catalogue central. Cohérence de ton, de sévérité, de canal sur toute l'app. À appliquer rétroactivement sur les modules existants. Cas d'usage immédiat : le chantier autocorrect a besoin de toasts Windows 11 natifs interactifs (boutons + champ de réponse texte, comme Codex) pour son enrollment prompt — et ces toasts seront peut-être la seule interface de ce sous-système.

## Pièges — recherches faites, ne pas refaire

### Plateforme (vérifié sur Microsoft Learn)

- **`AppNotificationManager` (Windows App SDK) fonctionne pour les apps unpackaged.** `Register()` provisionne le COM activator sans MSIX, sans AUMID manuel ([doc](https://learn.microsoft.com/windows/apps/develop/notifications/app-notifications/app-notifications-dotnet)). Trois limites : app élevée (admin) = toasts morts ; l'icône du toast est extraite des ressources de l'exe (pas d'icône embarquée = icône générique Windows) ; un AUMID propre via shortcut Start améliore le groupement Notification Center sans être bloquant.
- **L'ancienne API `Windows.UI.Notifications` / `ToastNotificationManagerCompat` est maintenance-only.** Ne pas l'utiliser, ne pas la re-évaluer.
- **Les balloons tray (`Shell_NotifyIcon NIIF_INFO`) sont morts sur Windows 11** — le banner expire et ne va plus dans le Notification Center (Remarks de la doc Shell_NotifyIcon). Le tray icon lui-même reste supporté ; seule la fonction balloon est à proscrire.
- **`ContentDialog` : un seul ouvert par thread XAML** — la seconde ouverture jette une exception, même visant une autre fenêtre. Tout usage centralisé impose un sérialiseur/file. Et le `XamlRoot` se prend via `window.Content.XamlRoot`, pas sur la `Window`.
- **`InfoBar` n'est pas un overlay** — inline, pousse le layout, persiste jusqu'à dismiss. Microsoft déconseille explicitement l'apparition/disparition rapide (logique anti-flash à coder côté app). Stacking via `StackedNotificationsBehavior` du Community Toolkit (single-window par construction, pas de routage cross-canal).
- **`TeachingTip` est explicitement déconseillé pour les erreurs et changements d'état** (doc officielle) — réservé au pédagogique transient.
- **Pas de primitive native « HUD overlay desktop »** dans WinUI 3 — PowerToys et Deckle réinventent la fenêtre layered topmost parce que rien ne le fournit. Ce n'est pas un manque de recherche.
- **Toasts progress** : `AppNotificationProgressBar` + `AppNotificationProgressData(sequenceNumber)` mis à jour via `UpdateAsync(data, tag, group)` sans re-créer la notification.

### Architecture (anti-patterns documentés, croisés sur toutes les sources)

- **Action handler en delegate stocké dans le message** — non-sérialisable, non-loguable, non-rejouable. Toutes les références matures (VS Code, IntelliJ) modélisent l'action comme un descripteur (id + label), le handler résolu ailleurs.
- **Identité instable** — renommer/supprimer un id de message casse les opt-outs utilisateur (« ne plus afficher »), les traces, les stats. L'id est un contrat public dès le premier jour (leçon RFC 7807 / Roslyn).
- **Localisation différée** — strings en dur dans les descripteurs « en attendant » : l'anti-pattern le plus unanimement dénoncé. Roslyn (`LocalizableResourceString`), `IStringLocalizer`, x:Uid découplent dès le départ.
- **Mélange canal et message** — « toast » n'est pas une propriété intrinsèque d'un message ; le même message se rend différemment selon contexte. Le descripteur porte une préférence, le dispatcher décide.
- **Catalogue sur-modélisé** — Roslyn tient en ~9 champs (4 obligatoires). Au-delà, le bruit décourage l'usage.
- **Over-notification** — VS Code « one at a time », Windows max 4 stacked, banner blindness NN/g. Throttling et coalescence sont à prévoir dans le design, pas à rattraper.
- **`MessageBox.Show()` et équivalents statiques** dans un module métier = défaut d'architecture, point final.

## Références les plus fertiles (où re-piocher)

Trois modèles ont dominé la recherche, par ordre de pertinence pour le besoin exact :

1. **IntelliJ NotificationGroup** ([doc](https://plugins.jetbrains.com/docs/intellij/notifications.html)) — le seul framework qui impose le catalogue déclaré avant émission. Descripteur de groupe (id, displayType par défaut, clé de bundle) + instance émise. L'utilisateur peut rétrograder le displayType de chaque groupe dans les préférences sans toucher le code. C'est le plus proche du besoin Deckle.
2. **Roslyn `DiagnosticDescriptor`** — la structure de descripteur la plus pesée côté .NET : id stable, title/messageFormat localisables, category (vocabulaire fermé), defaultSeverity surchargeable par le consommateur, isEnabledByDefault, helpLinkUri.
3. **VS Code `INotificationService`** ([notification.ts](https://github.com/microsoft/vscode/blob/main/src/vs/platform/notification/common/notification.ts)) — la maturité du contrat d'invocation : `Priority` (Default/Optional/Silent/Urgent) **orthogonale** à la sévérité ; `neverShowAgain { id, scope }` ; `INotificationHandle` retourné (muter/fermer/observer la notif après émission) ; `prompt(severity, message, choices)` pour les questions.

Côté UX, sources déjà dépouillées (citations précises dans la session d'origine) : NN/g [Indicators/Validations/Notifications](https://www.nngroup.com/articles/indicators-validations-notifications/), [Error-Message Guidelines](https://www.nngroup.com/articles/error-message-guidelines/), [Confirmation Dialogs](https://www.nngroup.com/articles/confirmation-dialog/) (Undo plutôt que confirm) ; Microsoft [InfoBar guidance](https://learn.microsoft.com/windows/apps/design/controls/infobar) (la règle d'arbitrage des canaux tient en 4 lignes sur cette page) ; [Error Message Guidelines Win32](https://learn.microsoft.com/windows/win32/debug/error-message-guidelines) (phrasing : pas de « please », pas de blâme, quoi/pourquoi/quoi faire). Pattern transversal observé (Settings Win11, PowerToys, Slack) : **même événement, canal qui suit l'attention disponible** — silencieux si succès attendu, InfoBar si fenêtre ouverte, toast si background, modal seulement si décision requise.

## Esquisse de design discutée (suggestif — à re-challenger)

- Un `MessageDescriptor` record immuable : id stable en `point.snake_case`, category, clés .resw pour title/messageFormat, DefaultSeverity, DefaultChannel (préférence, pas commande), Priority, Sticky/AutoDismissAfter, ActionDescriptor (id + label key, handler résolu via registry), AllowNeverShowAgain opt-in, HelpLinkUri, CoalesceWindow.
- Catalogue **distribué par module** (chaque module déclare ses descripteurs, ex. `AudioMessages`), indexé centralement au boot (unicité des ids vérifiée, liste exposée à une future page Settings « Notifications »). Évite le couplage module métier → module notification via un projet Abstractions léger.
- Quatre rôles internes : CatalogRegistry, UserPreferences (overrides, neverShowAgain), Dispatcher (routage canal effectif = préférence descripteur × contexte focus/activité × overrides ; dédup + throttling ; télémétrie de ses propres décisions sur son provider ETW — « pourquoi cette notif n'est pas apparue » lisible dans la LogWindow), Renderers par canal derrière une interface commune (HUD, InfoBar avec registry de slots par fenêtre, Toast, ContentDialog avec file sérialisée).
- Émission = double sortie en une ligne : `Emit(descriptor, args)` rend la notification ET émet l'event ETW d'audit. La trace persiste même si la notif est throttlée/mutée.
- Direction évoquée et accueillie avec enthousiasme : **source generator maison** (façon `LoggerMessage`, mais ciblant ce module — `LoggerMessageAttribute` lui-même ne route que vers `ILogger`, il ne se réutilise pas tel quel). Coût réel : écrire et tester un incremental generator Roslyn est un sous-chantier d'apprentissage en soi.

Attention au décalage temporel : cette esquisse parlait de `UserFeedbackEmitted` et du plan EventSource d'avant exécution. Depuis, la refonte est livrée — `Deckle.Diagnostics` (+ `.Logging`, `.Telemetry`), providers `Deckle-<component>`, `HudFeedbackEventListener` existant. Re-cartographier l'état réel avant de figer quoi que ce soit.

## Questions ouvertes du grilling (avec les pistes proposées, aucune tranchée)

1. **Sévérité et priorité, axes séparés ?** Piste : oui, séparés (sévérité 5 niveaux Info/Success/Warning/Error/Critical + priorité 4 niveaux façon VS Code) — « à quel point c'est grave » ≠ « à quel point on dérange ».
2. **Action en descripteur ou en delegate ?** Piste : descripteur strict (id + label key), handler via registry injecté. Le delegate est un piège documenté.
3. **Catalogue distribué ou central ?** Piste : distribué par module + index boot (modèle IntelliJ), l'audit global passant par l'index et la page Settings.
4. **Source generator ou déclaration manuelle ?** Dernière position de Louis : generator dès le départ (« coût unique, bénéfice composé, comme EventSource »). Reste à vérifier le coût réel d'apprentissage et de test du generator.
5. **Canal : préférence du descripteur ou décision du dispatcher ?** Piste : le descripteur suggère, le dispatcher décide selon contexte (focus, activité in-context, overrides).
6. **File ContentDialog (single-active) ?** Piste : coalescence par id + FIFO bornée à 3, au-delà downgrade en toast. Trois Critical concurrents = probablement un signal que l'un n'est pas Critical.
7. **Slots InfoBar : rétrofit maintenant ou Phase surfaces ?** Piste : slot par Window (pas par Page), coût XAML faible, fallback toast si aucun slot.
8. **`neverShowAgain` opt-in ou opt-out ?** Piste : opt-in par descripteur ; Critical jamais mutable, Info/Success par défaut oui.
9. **Localisation .resw immédiate ?** Piste : oui dès le premier descripteur (anti-pattern unanime sinon).
10. **Noms.** Module (`Deckle.Notifications` ?) et concept (`MessageDescriptor` ?) — à passer par `deckle-nomenclature` ; noter que les providers ETW sont devenus `Deckle-<component>`.
11. **Message UX sans event de jalon métier ?** Piste : pas de bypass — `Emit` trace toujours, au pire en Verbose.
12. **Migration de l'existant ?** Piste : inventaire des sites de feedback actuels, un descripteur par site dans le module owner, migration module par module sans coexistence.

## Séquencement évoqué

L'idée « testing d'abord » a été posée quand le testing n'existait pas encore ; les commits `test(*)` récents montrent que le bagage est arrivé. Le point de départ pragmatique discuté en handoff : commencer par le **canal toast interactif seul** (spike `AppNotificationBuilder` avec `AddTextBox` + boutons, validé à la main), parce que c'est le besoin immédiat de l'autocorrect, et laisser le catalogue complet se construire autour une fois le canal prouvé.
