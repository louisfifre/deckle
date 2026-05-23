# CLAUDE.md — Deckle.Catalog

Référentiel des ressources UI nommées par clé sémantique. Couvre deux familles. La **localisation** via la façade `Loc` au-dessus du `ResourceLoader` Windows App SDK, consommée en code et en XAML (`x:Uid`) par tous les modules WinUI. Les **glyphes** Segoe Fluent Icons centralisés dans `Themes/Icons.xaml` (consommé en XAML via `{StaticResource Icon.X}`) et `Glyphs.cs` (consommé en code-behind via `Glyphs.X`), ~51 clés sémantiques organisées en groupes thématiques (génériques, actions, Whisper, Diagnostics, Ambient, badges HUD, transport).

Aujourd'hui ~51 glyphes et ~200 strings sur 15 surfaces user-facing. L'app reste user-facing **anglais d'emblée** (cf. CLAUDE.md projet). Cette itération produit uniquement le fichier `en-US`. Pas de FR, pas de dropdown de sélection de langue dans Settings — le runtime résout sur la langue d'affichage Windows et tombe sur `en-US` par défaut.

## Architecture localization

Trois pièces reliées par convention de fichiers et de noms.

**Fichier source des strings**. Chaque module qui ship du XAML avec `x:Uid` porte son propre `Strings/en-US/Resources.resw` (XML, format hérité ResX). Une entrée par clé. Pattern multi-assembly PRI : `<EnableMsixTooling>true</EnableMsixTooling>` dans le csproj du module génère un `.pri` à côté de la DLL au build, et `MakePri` peut être appelé pour pré-compiler les ressources. Au runtime, `ResourceLoader` du module résout sur sa propre instance. Modules concernés à ce jour : `Deckle.Settings`, `Deckle.Transcription`, `Deckle.Llm.Rewrite`, `Deckle.Lighting.Ambient`, `Deckle.Setup`, `Deckle.Playground`, plus l'app hôte `Deckle.App`.

**Langue neutre**. `<DefaultLanguage>en-US</DefaultLanguage>` dans chaque csproj qui porte du `.resw`. Sans cette balise, le résolveur MRT trouve bien le fichier mais aucune langue n'est déclarée comme fallback ; les `x:Uid` peuvent rester vides quand la langue système diverge. La balise verrouille `en-US` comme fallback inconditionnel.

**Consommation**. Deux modes côte à côte. `x:Uid="MyKey"` en XAML résout automatiquement les propriétés `MyKey.Text`, `MyKey.Header`, `MyKey.Description`, `MyKey.Title`, `MyKey.Content`, `MyKey.PlaceholderText`, `MyKey.ToolTipService.ToolTip`. Le résolveur XAML lit `Strings/<lang>/Resources.resw` au runtime et applique les valeurs trouvées à l'élément, zero-code côté XAML. `Loc.Get("Key")` et `Loc.Format("Key", args...)` en code, façade statique dans ce module. Utilisé pour tout ce qui est construit programmatiquement : `ConsentDialog`s, status moteur, HUD, tray, status dynamiques du setup wizard.

L'API utilisée est `Microsoft.Windows.ApplicationModel.Resources.ResourceLoader` (Windows App SDK), **pas** l'ancien `Windows.ApplicationModel.Resources.ResourceLoader` (UWP). Les deux existent dans `Microsoft.WindowsAppSDK 1.8` mais seul le premier fonctionne en unpackaged.

## Convention de clés

Un seul fichier `Resources.resw` par module. Les préfixes structurent la lecture humaine.

**`x:Uid` en XAML** — pattern `<UidValue>.<Property>` où `UidValue` est libre (à choisir clair en `CamelCase`, sans underscore ni séparateur) et `<Property>` est résolu automatiquement. Une même `UidValue` peut porter plusieurs propriétés. Convention : `<Surface><ElementRole>` en `CamelCase` (`LogWindowSearchBox`, `GeneralPageTranscribeCard`, `LlmEnableCard`, `SetupChoicesInstallLocation`).

**Lookup direct en code** — pattern `<Surface>_<Purpose>` pour les strings consommées via `Loc.Get`. `CamelCase` pour `Surface`, underscore comme séparateur, `CamelCase` ou minuscule pour `Purpose`. Exemples : `CorpusConsent_Title`, `CorpusConsent_Body_Intro`, `CorpusConsent_PrimaryButton`, `Setup_StepTitle_Choices`.

**Strings paramétrées** — suffixe `_Format` obligatoire et visible dans le code consommateur. Placeholders composite-format `{0}`, `{1}`, … consommés par `Loc.Format`. Exemples : `Status_Rewriting_Format = "Rewriting ({0})…"`, `Tray_Tooltip_Format = "Deckle — {0}"`, `Llm_StartOllama_Format = "Start Ollama or check the endpoint setting ({0})."`.

**Strings réutilisables** — préfixe `Common_` pour les boutons et statuts génériques qui apparaissent sur plusieurs surfaces. Avant de créer une clé spécifique de surface, vérifier qu'il n'existe pas déjà un `Common_*`. Exemples : `Common_Cancel`, `Common_Back`, `Common_Next`, `Common_Enable`, `Common_Reset`, `Common_Remove`, `Common_Keep`, `Common_Browse`. Une clé `Common_*` ne contient jamais de paramètre. Les variantes contextuelles (`Cancel install`, `Reset all`) gardent leur clé spécifique de surface — `Common_*` reste l'expression courte canonique.

## Strings techniques non traduites

Liste fermée des chaînes qui restent **hardcodées** dans le code et ne passent jamais par le `.resw` ni par `Loc`. Toute addition à cette liste demande une justification documentée ici.

- **Noms de fichiers et extensions** — `app.jsonl`, `latency.jsonl`, `microphone.jsonl`, `corpus.jsonl`, `settings.json`, `Deckle.pri`, `Deckle.exe`. Les noms sont des contrats avec le filesystem et avec les outils de diagnostic ; les traduire casse les scripts et la télémétrie.
- **URLs et endpoints** — `http://localhost:11434/api/chat` (Ollama default), URLs de redist GitHub, schémas `ms-resource://`. Identifiants techniques.
- **Noms de produits et marques** — `Deckle`, `Ollama`, `Silero VAD`, `whisper.cpp`. Identité produit ; pas de traduction possible ni souhaitable.
- **Noms de modèles Whisper** — `base`, `small`, `medium`, `large-v3`, `tiny`. Tag d'identification du modèle, exposé tel quel dans l'UI.
- **Noms d'EventSource providers et de tags log** (`Deckle.Audio`, `Deckle.Whisp`, etc., et leurs labels courts `AUDIO`, `WHISP`, …). Vocabulaire interne, lu par les développeurs dans la LogWindow et le JSONL, pas par les utilisateurs au sens UX du terme.

Tout autre texte visible par l'utilisateur passe par le `.resw`.

## Ajouter une nouvelle string

1. Choisir le pattern qui correspond. Si la string apparaît dans un attribut XAML statique, viser `x:Uid`. Si elle est construite en code, viser `Loc.Get` ou `Loc.Format`.
2. Avant d'inventer une clé spécifique, vérifier qu'un `Common_*` ne couvre pas déjà le besoin.
3. Ajouter l'entrée dans le `Strings/en-US/Resources.resw` du module qui possède la surface. L'ordre dans le fichier suit les sections — Common, puis par surface. Garder le fichier groupé pour faciliter la relecture humaine.
4. Côté code consommateur : en XAML, ajouter `x:Uid="<UidValue>"` sur l'élément et retirer la valeur littérale de l'attribut concerné ; en code, remplacer le littéral par `Loc.Get("<key>")` ou `Loc.Format("<key>_Format", args...)` (importer `Deckle.Catalog`).
5. Builder via `MSBuild.exe` Framework (cf. CLAUDE.md projet — `dotnet build` est cassé sur les projets WinUI). Vérifier au runtime que la string s'affiche bien ; en DEBUG une clé manquante apparaît comme `[!key]` à l'écran (assez voyant pour être détecté en quelques secondes).

## Ajouter une langue future

Quand le moment vient (FR, ES, …), créer `Strings/<lang>/Resources.resw` à côté du `en-US` du module concerné, en copiant le fichier puis en traduisant chaque `<value>`. Garder les clés strictement identiques. Ne pas toucher aux strings techniques de la liste plus haut. Pour les strings paramétrées `_Format`, garder le même nombre de placeholders `{0}`, `{1}` — la grammaire de la langue cible peut imposer un autre ordre, `string.Format` accepte les placeholders dans n'importe quel ordre dans la chaîne, c'est exactement à ça qu'ils servent.

Au runtime, MRT résout sur la langue d'affichage Windows. Pour exposer une sélection manuelle dans Settings (override), introduire un `ResourceContext` avec `QualifierValues["Language"] = "<lang>"` ou `Languages = new[] { "<lang>" }` et le câbler à un setting persistant. Hors scope V1.

## Pièges et notes opérationnelles

- **API à utiliser** : `Microsoft.Windows.ApplicationModel.Resources.ResourceLoader` (Windows App SDK). L'ancien `Windows.ApplicationModel.Resources.ResourceLoader` UWP est encore référencé dans certains résultats de recherche obsolètes — il ne marche pas en unpackaged.
- **Création du `ResourceLoader`** : se fait après l'init runtime Windows App SDK (auto-bootstrap via `<WindowsPackageType>None</WindowsPackageType>` qui appelle l'API bootstrapper). Le `_loader` de `Loc` est paresseux pour garantir cette contrainte temporelle ; n'utiliser `Loc.Get` qu'à partir du moment où `App.OnLaunched` a démarré.
- **Clé manquante** : `ResourceLoader.GetString` retourne **string vide** par contrat WindowsAppSDK, sans exception. En DEBUG, `Loc.Get` substitue `[!key]` pour rendre la régression visible. En RELEASE le comportement par défaut est conservé.
- **Inspection de la PRI** : `MakePri.exe dump <chemin>.pri` (SDK Windows 10) liste les ressources embarquées et leurs clés. Utile pour vérifier que le pipeline build a bien embarqué un `.resw` après modification.
- **`x:Uid` invalide en XAML** : génère un avertissement `WMC*` au build mais n'empêche pas la compilation. Surveiller la sortie MSBuild pour rattraper les Uids cassés tôt.
- **Partage d'`x:Uid` entre éléments hétérogènes** — pas un avertissement build, **plante au runtime** dans `InitializeComponent` avec `XamlParseException: Unable to resolve property '<Prop>' while processing properties for Uid '<Uid>'`. Cause : MRT applique chaque propriété déclarée dans la `.resw` à **chaque** élément qui porte cet `x:Uid`. Si l'un des éléments n'expose pas la propriété (un `Button` n'a pas de `.Text`, il a `.Content`), tout le chargement XAML de la page tombe. Pattern correct : un `x:Uid` distinct par type d'élément, suffixe de rôle explicite (`*Button` pour le conteneur interactif, `*Label` pour le `TextBlock` interne). Cas toléré : plusieurs éléments du **même type** partageant un même Uid (par exemple huit `HyperlinkButton x:Uid="Settings_SectionResetLink"` dans les sections Settings, tous résolus sur `.Content` et `.ToolTipService.ToolTip` — aucun n'a de propriété manquante).
- **`<data name name>` value OR scope, pas les deux** — une même `name` ne peut pas servir à la fois de valeur (`<data name="X"><value>...</value></data>`) et de scope pour des sous-clés (`X.SubKey`). Au build, l'erreur `PRI175` ou `PRI278` apparaît. Toujours utiliser deux clés distinctes (`X_Label` + `X.SubKey`).
- **Commentaire XML** : `--` est interdit dans un commentaire `.resw` (`MSB4025`). Échapper en `--` espacé ou réécrire sans le double tiret.

## Glyphes Segoe Fluent Icons

`Themes/Icons.xaml` est un `ResourceDictionary` qui mappe ~51 clés sémantiques (`Icon.Transcribe`, `Icon.Rewrite`, `Icon.Save`, `Icon.Pin`, etc.) vers les hex Segoe Fluent. Consommé en XAML via `{StaticResource Icon.X}` sur un `FontIcon.Glyph` ou un `PathIcon`. Le dictionnaire est référencé une fois dans chaque module qui utilise des icônes via `<ResourceDictionary Source="ms-appx:///Deckle.Catalog/Themes/Icons.xaml" />` dans les ressources du module.

`Glyphs.cs` est la version code-side : une classe statique avec les mêmes clés sémantiques exposées comme `const string` (par exemple `Glyphs.Transcribe = ""`). Consommée par tous les sites qui construisent un `FontIcon` programmatiquement (typiquement le tray, les badges HUD, certains items générés à la volée).

Les deux fichiers sont synchronisés par convention de nommage — chaque clé sémantique existe dans les deux. Le pattern est documenté en commentaire de tête dans les deux fichiers. Modifier le glyphe d'une clé met à jour les deux entrées en même temps. Ajouter une clé suit le même principe et choisit son groupe thématique (génériques, actions, Whisper, Diagnostics, Ambient, badges HUD, transport).

## Pointeurs

- [Microsoft Learn — Resource files (.resw)](https://learn.microsoft.com/en-us/windows/uwp/app-resources/localize-strings-ui-manifest) — base ResourceLoader/MRT.
- [Microsoft Learn — Segoe Fluent Icons](https://learn.microsoft.com/en-us/windows/apps/design/style/segoe-fluent-icons-font) — catalogue Segoe Fluent.
