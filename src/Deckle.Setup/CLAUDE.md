# CLAUDE.md — Deckle.Setup

Wizard first-run de Deckle. `SetupWindow` (shell trois-rows : header + Frame + footer Cancel/Back/Next) avec trois pages frame-navigées (`ChoicesPage`, `InstallingPage`, `SummaryPage`). Le module ne porte aucune primitive de provisioning — il orchestre les primitives `NativeRuntime`, `SpeechModels`, `Downloader`, `SetupContext` qui vivent dans `Deckle.Transcription/Setup/`. Détaché de `Deckle.App` en cartographie-cleanup pour que la `GeneralPage` de Settings puisse rouvrir le wizard sans que l'app hôte traîne le XAML.

## Rôle du wizard

Deckle ne peut pas transcrire sans trois familles d'artefacts post-install : un runtime natif whisper.cpp (8 DLLs, ~50 MB), un modèle Whisper (~150 MB pour `base`, ~3 GB pour `large-v3`), et un VAD Silero (~700 KB). Le binaire ship vide de ces trois pièces — le wizard les provisionne sous `<UserDataRoot>`. Pas de mode dégradé : sans modèle on ne fait rien d'utile. Le wizard est donc **bloquant** au premier launch, et ré-accessible à la demande depuis Settings une fois passé (pour swap de modèle, changement de location, ou ré-import des natives).

## Décisions structurelles

**Wizard linéaire dans une `Window` Mica dédiée.** Pas `ContentDialog` (trop léger pour > 3 étapes), pas `InfoBar` persistant (l'app serait semi-fonctionnelle), pas catalogue PowerToys (notre flow n'est pas exploratoire). Source d'inspiration : Dev Home `SetupFlow` pour la structure, PowerToys `OobeWindow` pour les conventions visuelles (Mica, TitleBar Tall, drag region).

**Frame stepper comme conteneur des étapes.** L'`Orchestrator` ViewModel + `ContentControl`/`DataTemplateSelector` de Dev Home est plus testable mais demande 3-4 VMs + un selector — disproportionné pour 3 pages. Refactor possible plus tard si le wizard grossit.

**3 étapes : Choix global → Installation en bloc → Résumé/erreurs.** L'utilisateur fait tous ses choix avant de lancer une seule série de téléchargements ; les erreurs remontent à la fin, on ne propose pas de retry inline (Retry global possible depuis Summary).

**Auto-download des natives par défaut, fallback Browse local.** Le bundle est publié comme release GitHub Deckle taggée `native-vX.Y.Z` ; le wizard fait un GET non-authentifié sur l'asset. La référence du bundle vit dans `NativeRuntime.cs` côté Transcription : `CurrentBundle = NativeRuntimeBundle(Version, Url, Sha256, SizeBytes, DisplayName)`. Mode dégradé : si le bouton Browse pointe sur un dossier valide, l'utilisateur saute le download.

## Structure UX

Window 720×520 centrée, `MicaBackdrop`, `TitleBar` Tall sans back button. Grid 3 rows : header (step title h2 + subtitle body secondary), body (`Frame`), footer fixe (Cancel | Back | Install/Next AccentButton).

**Étape 1 — Choices.** Pattern combiné Dev Home `RepoConfigView` + VS Installer "Locations". L'utilisateur fait *tous* ses choix sur une page : où installer + status du runtime natif + quel modèle. Ces choix conditionnent la taille totale, affichée en bas de page via `InfoBar Severity=Informational` comme feedback continu. Le runtime natif a trois états visuels : *Installed* (bouton `Replace...`), *Will be downloaded* avec taille en parenthèses (bouton `Use local copy...`), *Missing N file(s)* (bouton `Browse...`). Le 3ᵉ cas n'est qu'un filet pour les builds dev où le repo Deckle n'a pas encore publié de release native — `BundleUrlIsPlaceholder` revient à `true`, le gate Next force Browse. Choix path par `TextBox(IsReadOnly=True)` + `Button` qui ouvre `FolderPicker`, choix modèle par `RadioButtons`, cards par `controls:SettingsCard`. Footer : Cancel | Back disabled | **Install** accent.

**Étape 2 — Installing.** Pattern Dev Home `LoadingView`. Tout est lancé d'un bloc à l'arrivée sur la page, pas d'interaction utilisateur en cours d'opération sauf Cancel. `ProgressBar` global déterminé (`Maximum=3, Value=tasksDone`) + un sub-progress par item avec taille courante / totale et pourcentage. Séquentiel : runtime natif → modèle Whisper → Silero VAD. Le runtime est court (DL ~18 MB + extract), les modèles peuvent prendre des minutes. Cancel = `CancellationTokenSource.Cancel()`, fichiers `.partial` supprimés. `SHA-256` vérifié pour le bundle natif (hash hardcodé dans `CurrentBundle.Sha256`) — pas pour les modèles HuggingFace (pas de hash canonique côté upstream). Footer : Cancel only, Back disabled, Install caché.

**Étape 3 — Summary.** Succès : `✓ All set` + récap Location / Runtime / Modèle / VAD + bouton `Get started`. Échec partiel : `! Some items could not be installed` + récap ligne par ligne avec succès et erreurs + boutons `[Retry] [Quit]`. Pas de partial-success boot — soit tout est OK et l'app boot, soit l'utilisateur Retry l'étape 2 ou Quit.

## Composants WinUI 3 par rôle

| Rôle | Contrôle |
|---|---|
| Window racine | `Window` + `MicaBackdrop` |
| TitleBar | `Microsoft.UI.Xaml.Controls.TitleBar` (Tall, no back button) |
| Stepper | `Frame` |
| Footer | `Grid` 2-col + `Button` Back / `AccentButton` Next |
| Choix path | `TextBox(IsReadOnly=True)` + `Button` → `FolderPicker` |
| Choix modèle | `RadioButtons` |
| Card de choix | `controls:SettingsCard` (CommunityToolkit) |
| Total estimé | `InfoBar Severity=Informational` |
| Progress global | `ProgressBar` déterminé (Min=0, Max=3) |
| Progress download | `ProgressBar` déterminé (Min=0, Max=ContentLength) |
| Status par item | `TextBlock` Body / Caption + `TextFillColorSecondaryBrush` |
| Erreurs récap | `InfoBar Severity=Error` + `TextBlock` détail |
| Theme resources | `MicaBackdrop`, `OverlayCornerRadius`, `CardBackgroundFillColorDefaultBrush`, `TextFillColor*Brush` |

## Primitives de provisioning (orchestrées depuis Setup, hébergées par Transcription)

Découplage volontaire : le wizard UI vit dans ce module, mais les primitives qui *exécutent* le provisioning vivent dans `Deckle.Transcription/Setup/` parce que le seul consommateur runtime du runtime natif et des modèles est `WhispEngine`. Si demain on extrayait un autre consommateur (e.g. Voxtral via un module séparé), les primitives pourraient être promues dans un module shared, mais à V1 elles restent côté Transcription.

**`NativeRuntime.cs`** — encapsule TOUTE la connaissance des DLLs whisper. Expose `const string EntryDll`, `IReadOnlyList<string> RequiredDllNames` (8 entries), `record NativeRuntimeBundle(Version, Url, Sha256, SizeBytes, DisplayName)`, `static NativeRuntimeBundle CurrentBundle` (single source of truth), `static bool BundleUrlIsPlaceholder`, `static bool IsInstalled()`, `static int CopyFromFolder(string source)`, `static Task<int> InstallFromZipAsync(string zipPath, CancellationToken)`, `static IReadOnlyList<string> GetMissing()`. **Encapsulation native** : ce module est le **seul** qui nomme `libwhisper.dll`, `ggml-*.dll`, `libgcc_s_seh-1.dll`, etc. Tous les autres consommateurs (wizard, settings, debug) passent par ses méthodes publiques. Si demain on bascule sur une autre stack (Vulkan → DirectML, MinGW → MSVC), seul ce fichier change. `NativeMethods.cs` (Core/Interop) reste isolé côté `[DllImport]` et `SetDllImportResolver` — il connaît `"libwhisper"` comme identifiant P/Invoke mais c'est `NativeRuntime` qui orchestre l'install. Le bundle versionné est produit par `scripts/lib/publish-native-runtime.ps1` (maintainer-only) ; recette de recompilation dans [docs/reference/reference--native-runtime--1.0.md](../../docs/reference/reference--native-runtime--1.0.md).

**`SpeechModels.cs`** — catalogue + résolution des modèles. Expose `record ModelEntry(Id, FileName, Url, SizeBytes, Sha256?)`, `IReadOnlyList<ModelEntry> WhisperModels`, `ModelEntry VadModel`, `bool IsInstalled(ModelEntry)`. Pas de SHA-256 sur les modèles HuggingFace en V1 — pas de hash canonique publié côté upstream, à ajouter quand on fixe les hashes côté upstream ou côté catalog.

**`Downloader.cs`** — primitive HTTP `HttpClient` + `IProgress<DownloadProgress>` + `SHA-256` + écriture `.partial` avant rename atomique. Cancel via `CancellationToken` supprime le `.partial`.

**`SetupContext.cs`** — état partagé entre les pages du wizard (`Location`, `SelectedModel`, `List<InstallResult> Results`). Passé via `Frame.Navigate(typeof(X), context)` + `OnNavigatedTo` qui récupère depuis `e.Parameter`. Les pages mutent le contexte ; `SetupWindow` observe pour activer/désactiver Next ou conclure.

**`CopyFromFolder` vs `InstallFromZipAsync`** — à ne pas confondre. Le premier est sync, lit un dossier dont l'utilisateur garantit le contenu (Browse). Le second est async, lit un zip dont l'intégrité est garantie en amont par `Downloader` (SHA-256 vérifié). Les deux convergent sur `NativeDirectory` mais ne sont pas interchangeables.

## Wire-up côté App

```csharp
// App.OnLaunched (gate first-run)
if (!NativeRuntime.IsInstalled() || !SpeechModels.IsDefaultInstalled())
{
    var setup = new SetupWindow();
    setup.Body.Navigate(typeof(ChoicesPage), setup);
    setup.Activate();
    bool success = await setup.Completion;
    if (!success) { Environment.Exit(0); return; }
}

// Settings — bouton "Run setup again..."
SettingsHost.OpenSetupWizard = () => new SetupWindow().Activate();
```

## Anti-patterns à éviter

- **`FolderPicker` inline** dans la page. Pattern Dev Home/PowerToys : `TextBox(IsReadOnly)` + `Button`.
- **`ProgressBar` indéterminé** quand le total est connu. HuggingFace renvoie `Content-Length` — bar déterminée + ratio bytes/total.
- **`DesktopAcrylicBackdrop`** sur la fenêtre setup. Réservé aux transient (HUD, popups). Setup est persistante → `MicaBackdrop`.
- **`NavigationView` left-pane** pour un wizard linéaire. Le pane suggère une nav libre, faux signal pour notre cas.
- **`ContentDialog` sans `XamlRoot`** — crash WinUI 3 systématique.
- **`Frame.GoBack()` avec history visible.** Back doit annuler le commit de l'étape précédente, pas naviguer dans une stack.
- **UI Element créé hors thread UI.** Les callbacks `HttpClient` → `DispatcherQueue.TryEnqueue` pour toute mise à jour de Progress.
- **Hardcoder des noms de DLL ailleurs que dans `NativeRuntime`** — viole l'encapsulation native. Le catalogue est dupliqué côté PowerShell (`scripts/lib/setup-assets.ps1`, `scripts/lib/publish-native-runtime.ps1`) avec un commentaire de traçabilité ; toute autre duplication est un bug.
- **Hardcoder des `#xxxxxx` ou des `CornerRadius` numériques** dans le XAML. Theme resources only.

## Observabilité

Toutes les émissions passent par `DeckleSetupSource.Log` — provider `Deckle.Setup`, tag SETUP dans la LogWindow.

## Out of scope V1

Téléchargement parallèle native + modèle (séquentiel suffit — le goulot c'est le modèle). Reprise d'un download interrompu (le `.partial` est supprimé, l'user recommence depuis zéro). Vérification SHA-256 des modèles HuggingFace (pas de hash canonique publié). Migration auto des settings/télémétrie depuis l'ancien layout `<exe>/config/` (clean break, l'utilisateur recopie s'il veut). Sélection runtime de la langue dans Settings (override du `ResourceContext`) — V1 résout sur la langue d'affichage Windows uniquement.
