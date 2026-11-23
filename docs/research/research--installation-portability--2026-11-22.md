# Portabilité de l'installation de Deckle — audit du 2026-11-22

Méthode : 10 agents Sonnet ont lu chacun une tranche du dépôt ; un onzième a étudié Parakeet ; une synthèse a dédoublonné les 163 constats. La session principale a ensuite revérifié elle-même les bloquants. Ses corrections sont marquées **[revu]**.

## En bref

- Bloquants : 10 (dont 5 propres à des modules « personnels » : Anytype x2, Mail, Hue, Taskbar ARM64). Dégradations : environ 45. Mineurs : environ 50.
- À corriger en premier : (1) **la désinstallation peut effacer des dossiers qui ne sont pas à Deckle** (bloquant 0) ; (2) par défaut, ne pas activer les modules du mainteneur (Anytype, Hue, Orchestration, Trackpad, Projects, Mail) et ne pas présélectionner tout le catalogue dans l'assistant ; (3) choisir la langue (Whisper `fr`, autocorrection française, invite de réécriture) d'après la langue de Windows, ou dire clairement que Deckle est français d'abord ; (4) Parakeet n'a pas de téléchargement.
- Couverture inégale : la tranche Mail + Folders n'a ouvert que 16 fichiers sur environ 300, et la tranche App environ 60 sur 150. Ces zones restent à relire.

## Bloquants pour une installation chez quelqu'un d'autre

### 0. [revu] L'installation et la désinstallation peuvent effacer des fichiers étrangers à Deckle
- Où : `src/Deckle.Setup/FoldersPage.xaml.cs:78` (le dossier choisi devient tel quel `InstallDirectory`, sans sous-dossier `Deckle`) ; `src/Deckle.Installer/Install/Uninstaller.cs:171` (`rmdir /s /q "{installDir}"`) ; `src/Deckle.Installer/Install/Uninstaller.cs:188` (`Directory.Delete(dir, recursive: true)` sur la racine de données) ; `src/Deckle.Setup/DeployPage.xaml.cs:159-167` (à la mise à jour, tout le contenu du dossier est vidé s'il contient `Deckle.exe`).
- Pourquoi ça casse : une personne qui choisit `C:\Apps` installe Deckle à plat dans ce dossier. À la désinstallation, `rmdir /s /q C:\Apps` efface aussi toutes ses autres applications. Le même risque vaut pour la racine de données. Chez toi, le dossier choisi est dédié à Deckle, donc tu ne le vois jamais.
- Piste : toujours ajouter un sous-dossier `Deckle` au dossier choisi ; à la désinstallation, ne supprimer que les fichiers listés au moment de l'installation.
- Confiance : vérifié dans le code par la session principale. L'effacement réel n'a pas été reproduit.

### 1. [revu] Le dépôt `louisfifre/deckle` est la seule source de l'installation (risque réduit)
- Vérification de la session principale : le dépôt est **public**, et la dernière release est `v0.135.1`, du 2026-11-18. Le scénario « dépôt privé » ne s'applique donc pas. Le reste tient : il n'y a pas de miroir, et le 403 dû à la limite de l'API GitHub n'est pas distingué. La gravité réelle est « dégradation ».
- Où : `src/Deckle.Install/ReleaseResolver.cs:30` (`Repo = "louisfifre/deckle"`, API REST non authentifiée ligne 73) ; `src/Deckle.Transcription.Whisper/Setup/NativeRuntime.cs:86` (URL du bundle natif `native-v1.9.2`) ; `src/Deckle.Setup/SummaryPage.xaml.cs:191`.
- Pourquoi ça casse : si le dépôt est privé, renommé, forké ou sans release `vX.Y.Z`, l'installeur reçoit un 404 et personne d'autre ne peut installer. Sans le bundle natif (whisper.dll, ggml, Vulkan), rien ne transcrit. Derrière un NAT partagé, la limite de 60 requêtes par heure de l'API GitHub renvoie un 403 présenté comme un problème réseau (`InstallFlow.FriendlyError`). Cela contredit aussi le but « pas de dépendance structurelle à un compte unique ».
- Piste : confirmer que dépôt et releases sont publics et permanents ; prévoir un miroir ou un manifeste stable ; distinguer 403 et 404 dans les messages ; documenter l'import manuel.
- Confiance : vérifié dans le code (les constantes) ; probable (l'effet réel, car l'état public du dépôt n'est pas connu).

### 2. Parakeet : le dossier de modèle n'est ni téléchargé ni documenté
- Où : `src/Deckle.Transcription.Parakeet/ParakeetModelFiles.cs:15` (dossier `parakeet-tdt-0.6b-v3-ultra`, sous-dossiers `fp32/`, `fp16/`, `vocab.txt`) ; `src/Deckle.Transcription/TranscriptionSettings.cs:107` ; `src/Deckle.App/App.Startup.Modules.cs:66`.
- Pourquoi ça casse : aucun code ne télécharge ce dossier (ni `SpeechModels.cs`, ni `setup-assets.ps1`). Il s'agit d'un export ONNX préparé à la main. Choisir Parakeet donne `file_not_found`, ou un module de transcription silencieusement inactif (`SkipSpeechModelMissing`). Le parcours de réparation propose seulement Whisper.
- Piste : masquer le choix Parakeet tant qu'aucun téléchargement (URL épinglée + SHA-256) n'existe, ou retomber sur Whisper si le dossier est absent.
- Confiance : vérifié dans le code.

### 3. Anytype : aucun parcours de première installation
- Où : `src/Deckle.Anytype/Api/AnytypeCredentials.cs:71` ; `BackendInstallation.cs:41-43` ; `GatewayRuntime.cs:18`.
- Pourquoi ça casse : il faut un `credentials.json` (api_version, space_id) écrit à la main et une clé d'API dans le coffre sous `anytype-api-key`. Rien dans le dépôt ne les produit. La passerelle quitte avec le code 3 (`NotProvisioned`) et les surfaces MCP Anytype et Home ne démarrent jamais.
- Piste : étape de provisionnement au premier lancement, ou module optionnel, désactivé tant qu'aucun compte n'est configuré.
- Confiance : vérifié dans le code.

### 4. Anytype : le schéma est celui du mainteneur
- Où : `src/Deckle.Anytype/Schema/DevSpace.cs:35` (types `epic/project/task/report`, statuts, priorités `p_0..p_5`, créneaux Cap/Passion/Entraînement, rôles).
- Pourquoi ça casse : sur un espace vide ou différent, les gestes échouent faute de clés de type et de propriété. Rien ne construit le schéma automatiquement.
- Piste : module optionnel ; étape « appliquer le schéma » à partir du manifeste ; sortir les vocabulaires personnels dans le manifeste ou les réglages.
- Confiance : vérifié dans le code.

### 5. Mail ne montre que des données fictives
- Où : `src/Deckle.Mail.Hub/Views/MailWindow.xaml.cs:33` (`new SampleMailHubSource(SampleScenario.NormalDay)`).
- Pourquoi ça casse : aucune implémentation réelle de `IMailHubSource` n'existe dans la tranche. Chez tout le monde, Mail affiche de faux courriels français ; on ne peut pas connecter une vraie boîte.
- Piste : marquer Mail « aperçu » ou le retirer de l'application livrée jusqu'à l'existence d'un moteur ; garder la source fictive pour le self-check.
- Confiance : vérifié dans le code.

### 6. Éclairage ambiant : Philips Hue uniquement, adresses restreintes
- Où : `src/Deckle.Lighting/Hue/HueBridgeClient.cs:344` (`IsPrivateBridgeIp`).
- Pourquoi ça casse : sans pont Hue, le module est inutilisable. Un pont sur une adresse hors RFC1918 / 169.254 (IPv6, CGNAT, Tailscale 100.64/10) est refusé avec « not on a private LAN range ».
- Piste : dire « pont Hue requis » sur la page ; permettre de confirmer une adresse hors plage ; accepter les IPv6 de portée locale unique.
- Confiance : vérifié dans le code.

### 7. Autocorrection : moteur français uniquement, activé par défaut
- Où : `src/Deckle.Autocorrect/Engine/ConservativeTypoCorrector.cs:83` (`_french.Contains(lower)`) ; `AutocorrectSettings.cs:14` (`Enabled = true`, Notepad pré-inscrit) ; `src/Deckle.App/App.Autocorrect.cs:36`.
- Pourquoi ça casse : le lexique de base est toujours le français ; les paquets de domaine (`fr-it`, `en-it`) ne changent pas la base. Un mot anglais qui n'est pas dans le lexique français est traité comme une faute. La première application inscrite est déjà mal corrigée.
- Piste : choisir le lexique de base d'après la langue d'écriture de Windows, ou désactiver par défaut jusqu'à confirmation de la langue ; l'annoncer sur la page.
- Confiance : vérifié dans le code (français d'abord) ; probable (effet exact sur un texte anglais).

### 8. Barre des tâches : DLL x64 injectée dans explorer.exe, pas de ARM64
- Où : `native/Deckle.Shell.Taskbar.Native/patches/constant_return.cpp:21` (octets machine x64) ; `thumbnail_box.cpp:62-67`.
- Pourquoi ça casse : sur Windows ARM64, `explorer.exe` est natif ARM64 et ne peut pas charger une DLL x64 ; le moteur échoue (« indisponible »). Le reste de l'application tourne émulé.
- Piste : détecter ARM64 et masquer le module, ou fournir une DLL ARM64 avec ses propres patchs.
- Confiance : probable.

### 9. Whisper : langue forcée au français
- Où : `src/Deckle.Transcription/TranscriptionSettings.cs:115` (`Language = "fr"`) ; invite initiale lignes 120+ ; `WhisperViewModel.cs:30`.
- Pourquoi ça casse : la dictée d'un anglophone est décodée en français (texte déformé ou traduit). L'invite par défaut parle de Git, de Visual Studio et de « contour animé » (le HUD de Deckle) et est reportée sur chaque fenêtre de 30 s.
- Piste : `auto` par défaut, ou initialiser d'après la langue de Windows ; invite vide par défaut.
- Confiance : vérifié dans le code. Classé bloquant car c'est la fonction vedette de l'application ; l'agent de balayage l'avait classé « dégradation ».

## Dégradations

### Installeur et premier lancement
- **Pas de signature de code.** `scripts/release/publish-app.ps1:408`, `InstallFlow.cs:121`. Aucune signature Authenticode nulle part. SmartScreen « éditeur inconnu » ; Defender peut bloquer un exe non signé lancé depuis `%TEMP%`. La somme SHA-256 vient de la même release : elle ne protège que contre la corruption. Piste : signer stub, `Deckle.exe`, passerelle. Probable.
- **Dossier d'installation vidé ou supprimé en entier.** `src/Deckle.Setup/DeployPage.xaml.cs:159`, `FoldersPage.xaml.cs:101`, `Uninstaller.cs:170-171`. Le dossier choisi n'a pas de sous-dossier « Deckle » ; s'il contient déjà `Deckle.exe`, tout est effacé à la mise à jour, et `rmdir /s /q` à la désinstallation. Piste : toujours installer dans un sous-dossier « Deckle », ou marquer les fichiers propres. Vérifié dans le code.
- **Dossier de données supprimé en entier.** `Uninstaller.cs:118`. Même problème pour la racine de données choisie dans l'assistant (la page Réglages, elle, ajoute un sous-dossier). Vérifié dans le code.
- **Sélecteur de dossier UWP cassé en administrateur.** `FoldersPage.xaml.cs:101` (handlers `async void` sans try/catch). Lancé en élévation, « Parcourir » lève une exception et ferme l'assistant. Piste : `Microsoft.Windows.Storage.Pickers.FolderPicker` + try/catch. Probable.
- **Processus Deckle élevé non détecté.** `src/Deckle.Install/RunningProcesses.cs:92`. Un installeur non élevé ne lit pas le module d'un Deckle élevé, laisse passer la vérification, puis échoue en plein vidage du dossier : installation à moitié supprimée. Probable.
- **Téléchargeur partagé sans reprise.** `src/Deckle.Core/Io/Downloader.cs:148`. Pas de délai de blocage, pas de reprise, pas de réessai ; `.partial` réécrit depuis zéro. Un modèle de plusieurs Go sur connexion instable est fragile. Vérifié dans le code.
- **Présélection de tout le catalogue de modules.** `src/Deckle.Setup/SetupWindow.xaml.cs:55` ; `src/Deckle.Modules/ModulePresence.cs:37` (sans `presence.json`, tous les modules sont présents). Anytype, Hue, Orchestration, Trackpad, Projects démarrent pour quelqu'un qui n'a rien de tout cela. Piste : noyau minimal (transcription, réécriture, autocorrection), reste en option. Vérifié dans le code.
- **Modèles dans un dossier personnalisé non vus par l'assistant.** `SpeechModels.cs:51,67` utilisent toujours le dossier par défaut alors que le moteur lit le dossier personnalisé : l'assistant dit « non installé » et retélécharge. Vérifié dans le code.
- **Assistant : script `bootstrap-dev-env.ps1` obsolète** (voir Outillage).

### Matériel et système
- **x64 uniquement, pas de garde ARM64 ni AVX2.** `app.manifest:20`, `Directory.Build.props:27`, `InstallFlow.cs:47` (seul contrôle : « OS 64 bits »), `NativeRuntime.cs:47`, `BackendInstallation.cs:59`. Sous ARM64, l'émulation x64 est incertaine pour hooks clavier, natifs whisper/ONNX et DLL injectées. Piste : afficher les prérequis et refuser clairement. Vérifié dans le code (x64) ; spéculatif (comportement émulé).
- **Taskbar : arbre XAML et symboles non documentés propres à Windows 11 build 26100.** `TaskbarDocumentBuilder.cs:26,211`, `ExplorerFunctionLocator.cs:41`, `thumbnail_box.cpp:30-55`. Sur un autre build, règles non appliquées ou, pire, correctif écrit sur une signature différente et plantage d'explorer. Piste : liste blanche de builds (horodatage/taille de `Taskbar.View.dll`), comparer le prologue avant écriture. Vérifié dans le code. Le module est désactivé par défaut mais enregistré partout.
- **Taskbar : symboles téléchargés chez Microsoft au premier usage.** `ExplorerFunctionLocator.cs:41`. Hors ligne ou derrière un proxy : erreur répétée toutes les 10 minutes. Vérifié dans le code.
- **Taskbar : DLL non signée injectée.** `TaskbarHookInjector.cs:36`. Smart App Control, WDAC, antivirus peuvent la bloquer ou la mettre en quarantaine. Spéculatif.
- **Pas de niveau d'exécution dans le manifeste.** `app.manifest:5`. Les hooks clavier et d'entrée n'agissent pas sur les fenêtres élevées (UIPI) ; sous élévation, les toasts sont supprimés. Rien n'est expliqué au premier lancement. Probable.
- **Toasts : seul canal de notification.** `ToastChannel.cs:52`. Indisponible en élévation, avec notifications désactivées ou stratégie de groupe : les questions d'inscription de l'autocorrection sont perdues. Piste : canal de repli dans l'application. Vérifié dans le code.
- **Orchestration : WMI et élévation.** `WmiProcessStartSource.cs:14`, `Directory.Packages.props:56`. La détection immédiate du lancement d'une application ne fonctionne qu'élevé ; sinon seul le moniteur de premier plan joue. Vérifié dans le code.
- **Orchestration : règle HDR conçue pour le build 26100.** `HdrStateSwitch.cs:13`. En dessous, bascule sur « advanced color », qui confond HDR et gamut étendu : un écran SDR large gamut peut être modifié sans demande. Vérifié dans le code.
- **Capture d'écran : `IsSupported()` renvoie toujours true.** `src/Deckle.Vision/ScreenCaptureService.cs:210`. Échec sous Bureau à distance, session verrouillée, VM sans sortie GPU. Probable.
- **Trackpad / PrecisionScroll.** `WindowsGestureNeutralizer.cs:31` (valeurs de registre non documentées, effet « à chaud » non vérifié ; temporisations réglées sur un Magic Trackpad), `PrecisionScrollEngine.cs:65` (refuse de démarrer sans réglages de pavé tactile précis, donc inutilisable avec une simple souris). Probable.

### GPU, modèles et inférence
- **Whisper : GPU par défaut, une seule vérification de mémoire.** `TranscriptionSettings.cs:114` (`UseGpu = true`), `GpuMemoryPreflight.cs:26`, `VideoMemoryProbe.cs:83`. L'adaptateur DXGI mesuré (le plus de mémoire dédiée) n'est pas forcément celui qu'utilise Vulkan ; whisper.cpp fait `abort()` sur une allocation Vulkan ratée, sans handler géré. Sur portable à deux GPU, risque de crash au premier chargement. Vérifié dans le code (le choix d'adaptateur) ; probable (le crash).
- **large-v3 proposé sans test de matériel.** `SpeechModels.cs:34-40`. Sur CPU seul, latence de plusieurs secondes à minutes sans avertissement ; repli CPU silencieux. Probable.
- **Whisper : chemin du modèle en ANSI.** `WhisperPInvoke.cs:94`. Un nom d'utilisateur ou dossier hors page de code ANSI (cyrillique, CJK) devient `?` et le modèle n'est pas trouvé. Jamais exercé (profil du mainteneur en ASCII). Piste : tester avec un profil non ASCII ; passer par un tampon mémoire. Probable.
- **Parakeet : DirectML toujours sur l'adaptateur 0.** `OnnxParakeetModelLoader.cs:56`, `OnnxSessionOptions.cs:23`. Sur portable hybride, l'adaptateur 0 peut être l'iGPU ou un adaptateur logiciel. Voir la section Parakeet. Vérifié dans le code.
- **Parakeet : plafond de 120 s calibré sur une RX 7900 XT.** `LongAudioChunker.cs:24`. Environ 5 Go à 120 s ; hors de portée d'une carte de 4 à 6 Go. Probable.
- **Parakeet : pas de repli CPU après échec d'encodage.** `ParakeetBackend.Transcribe.cs:159`. Le repli n'existe qu'à la création de session ; un échec DML en cours de route perd toute la prise. Probable.
- **Parakeet : le moteur ONNX vient de Windows ML (Windows App SDK).** `Deckle.Inference.Onnx.csproj:13`. Exigences de version Windows non vérifiées. Spéculatif.
- **Réécriture : modèle et fournisseur codés en dur.** `ParagraphRewrite.cs:27,32,36` (`ollama`, `ministral-3:14b`, échéance 3 s), `SentenceRewrite.cs:12-14`, `LlmSettings.cs:243`. Il faut Ollama local, ce modèle de 14 milliards de paramètres (environ 9 Go de mémoire) et une machine assez rapide pour tenir 3 s. Sinon la fonction « n'offre rien » avec seulement une ligne de journal. Aucun texte de premier lancement ne l'explique. Vérifié dans le code.
- **Réécriture : gabarit de prompt déduit du nom du modèle.** `PromptTemplates.cs:20`. Mauvais gabarit possible pour d'autres modèles (ChatML par défaut) ; sorties rejetées sans message. Probable.
- **Réécriture : garde-fou lexical français.** `GateLexicon.cs:22`, `GateToken.cs:89-101`, invites en français. En anglais presque toute correction est rejetée ou traduite. Vérifié dans le code.
- **Photos de Folders : description toujours demandée en français.** `PhotoDescriptionPrompt.cs:15`. La recherche dans une autre langue ne trouve rien. Vérifié dans le code.
- **Folders : description des photos exige un serveur de vision local** (`LlmPhotoDescriptionModel.cs:63`), **recherche déléguée à PowerToys Command Palette** (`PackagedCommandPaletteProbe.cs:21`, raccourci « Win+Alt+Space » codé dans `Resources.resw:34`), **codecs RAW/HEIC de la Store** (`PhotoPictureReducer.cs:22`, absents sur LTSC et éditions N). Vérifié dans le code (le premier et le deuxième), probable (le troisième).

### Langue et disposition clavier
- **Valeurs par défaut de la transcription** : invite en français du mainteneur ; hallucinations connues uniquement en français (`KnownHallucinations.cs:52`) ; seuils réglés sur sa voix. Vérifié dans le code.
- **Autocorrection : adjacence QWERTY-US codée** (`QwertyAdjacency.cs:3`). Sur AZERTY ou QWERTZ, les fautes de frappe réelles ne sont pas reconnues. Tokeniseur français (`WordBoundaries.cs:25`). Seuls deux paquets de domaine (`DomainPack.cs:39`). Vérifié dans le code.
- **Anytype/Home : libellés et messages français** (`DevSpace.cs:236`, `TaskGestures.cs:184`), schéma Home propre à une maison française (`schema-manifest.json:388`, `HomeCode.cs:13`) avec échec fermé si non conforme (`HomeSchema.cs:280`). Fichier `spaces.json` à la main (`HomeMcp.cs:46`). Vérifié dans le code.

### Réseau et ports
- **Ports fixes.** Backend Anytype 31012 (`BackendHealthProbe.cs:35`, `BackendListenerOwner.cs:26`) ; passerelle MCP 33255 (`GatewayContract.cs:12`). Un autre programme sur ce port bloque la fonction sans réglage possible. Vérifié dans le code.
- **Hue : API CLIP v1 et v2 mixtes** (`HueBridgeClient.cs:150`) ; v1 annoncé comme déprécié. Probable. **Découverte mDNS uniquement** (`HueLocalDiscovery.cs:13`) ; échec derrière VPN ou VLAN. Probable. **Pont v2 en HTTPS 443 seulement** (`HuePairingService.cs:187`). Vérifié dans le code. **Entertainment (UDP 2100)** retombe silencieusement sur REST (`HueEntertainmentTransport.cs:45`). Vérifié dans le code.

### Projects (module personnel)
- Lien d'installation vers `louisfifre/deckle-scripts` avec `irm … | iex` (`DeckleScriptsInstall.cs:15`) ; exécution des scripts sans `-ExecutionPolicy` (`RunWrapper.cs:48`) alors que Windows 11 est en `Restricted` par défaut ; dépend de `pwsh` 7 et de Git dans le PATH (`ShellLocator.cs:20`). Vérifié dans le code (liens et PATH) ; probable (la politique d'exécution).

## Mineurs

| Quoi | Où | Confiance |
| --- | --- | --- |
| Éditeur « Louis Fifre » en dur dans les Applications installées | `UninstallEntry.cs:35` | vérifié dans le code |
| Mise à jour « prod » (dossier `current`, PowerShell en `Bypass`) livrée dans tous les builds | `ProdUpdate.cs:67,100` | vérifié dans le code |
| Lexique anglais complet exclu de la copie | `Deckle.App.csproj:273` | spéculatif |
| Orchestration (WMI + HDR), Ambient, Trackpad visibles par défaut | `App.Orchestration.cs:20`, `App.Startup.Modules.cs:102`, `App.Trackpad.cs:20` | spéculatif / probable |
| Raccourcis par scancode choisis sur AZERTY ; défaut `Win+0x29` peut être refusé | `App.Hotkeys.Actions.cs:117`, `AppSettings.cs:47` | spéculatif |
| Modèle de départ Whisper `base` seulement ; pas d'étape intermédiaire | `SpeechModels.cs:23` | vérifié dans le code |
| Structures P/Invoke whisper copiées de la 1.9.1 ; bundle en 1.9.2 ; import manuel possible | `WhisperStructs.cs:7`, `WhisperParamsMapper.cs:104` | probable |
| Modèle VAD Silero téléchargé après la première prise depuis raw.githubusercontent.com | `SileroVadModel.cs:12` | vérifié dans le code |
| PortAudio WASAPI seul, 32 périphériques max ; `portaudio.dll` cherchée seulement à côté de l'assembly | `PortAudioInterop.cs:7` | probable |
| Décodage de fichiers via Media Foundation seulement (éditions N sans codecs) | `AudioFileDecoder.cs:87` | spéculatif |
| Page de propriétés du micro `ms-settings:sound-properties?endpointId=` propre à Windows 11 | `RecordingPage.xaml.cs:255` | spéculatif |
| Coupure de phrase latine ; `CharsPerMinute = 700` mesuré sur ses dictées | `SentenceBoundary.cs:42`, `LlmSettings.cs:129` | probable / vérifié |
| Seuils de threads CPU Parakeet `ProcessorCount / 2` | `OnnxParakeetModelLoader.cs:61` | probable |
| Variables `DECKLE_AMBIENT_SAMPLER_PATH`, `DECKLE_MODEL_PATH` (hooks dev) | `FrameSampler.Reduction.cs:26`, `WhisperBackend.Model.cs:205` | vérifié dans le code |
| Capture d'interface : bordure jaune sous Windows 10 | `WindowSnapshot.cs:128` | probable |
| Horloge de la barre : lignes fixes en anglais, 24 h, gris sur fond sombre ; clé `NotifyIconSettings` propre à Windows 11 | `TaskbarCustomization.cs:238`, `NotifyIconSettingsKey.cs:72` | vérifié / probable |
| Écran de veille : relais mesuré avec un seul économiseur | `ScreenSaverHotkeyRelay.cs:18` | spéculatif |
| HUD sur l'écran principal ; perte du device Win2D sur GPU partagé | `HudWindow.Windowing.cs:23` | probable |
| Tâche planifiée « Start elevated » liée au chemin de l'exe | `ElevatedStartupService.cs:24` | vérifié dans le code |
| Jetons MCP écrits en clair dans l'environnement utilisateur | `McpClientTokens.cs:90` | vérifié dans le code |
| Suppression d'objet Anytype exige un toast (échec fermé) | `ManagementGestures.cs:184` | vérifié dans le code |
| Formats `fr-FR` fixes ; `Segoe Fluent Icons` sans repli Windows 10 | `ProjectGestures.cs:213`, `ProjectsGlyphs.cs:3` | vérifié / spéculatif |
| Chemin hérité Anytype 0.3.6 (code mort) | `BackendInstallation.cs:17` | probable |
| Fournisseurs de courriel : 6 domaines, 3 pointant vers Infomaniak, pas d'OAuth ; tâche liée à Anytype | `MailProviders.cs:12`, `TriageSession.Gestures.cs:24` | vérifié dans le code |
| Darktable cherché via App Paths ; bouton PowerToys Store | `Darktable.cs:47`, `PowerToysInstallPage.cs:17` | vérifié / probable |
| Règles d'icônes de dossier, pluriels, aperçu texte en Windows-1252 (français/anglais d'abord) | `FolderIconRule.cs:29`, `EntryFormat.cs:57`, `TextPreviewReader.cs:139` | vérifié / probable |
| Mots protégés « anytype, winui, xaml… » ; Hue : gamut C unique ; plafonds de capture 120 Hz non mesurés ailleurs ; découverte Hue en ligne via Philips | `GlobalEnglishLexicon.cs:14`, `HueColorMath.cs:67`, `AmbientCaptureCap.cs:53`, `HueDiscovery.cs:10` | vérifié / spéculatif |
| Diagnostic : plafonds de logs calibrés sur le mainteneur ; noms Anytype/Ollama/Hue dans les filtres ; Steam seul dans l'analyse de jeux ; `createdump` non testé hors .NET 10.0.11 | `LoggingSettings.cs:62`, `LogLineFormatter.cs:26`, `SteamLibrary.cs:24`, `CrashDumpCapture.cs:16` | vérifié / spéculatif |
| Pas de MSIX ni de signature pour `Folders.App` et `Mail.App` ; x64 seulement | `Deckle.Folders.App.csproj:16` | spéculatif |

## Outillage de développement

- **Version de build = étiquette git.** `Deckle.App.csproj:66`, `Deckle.Folders.App.csproj:37`, `scripts/lib/version.ps1:45`. Le build échoue sans Git ni étiquette `vX.Y.Z` accessible (zip, clone superficiel, fork). Piste : repli `0.0.0-dev+sha`, garder l'échec strict pour les releases. Vérifié dans le code.
- **Prérequis non documentés.** `global.json` seulement dans `src/Deckle.App` (SDK 10.0.104, `latestFeature`) ; cible `net10.0-windows10.0.26100.0` ; PowerShell 7, SDK Windows 26100, Visual Studio. Pas de guide de build. Vérifié dans le code.
- **Bootstrap incomplet ou périmé.** `scripts/setup/bootstrap-dev-env.ps1:43` (bundle `1.9.1` alors que le code épingle `1.9.2` : `-IncludeAssets` échoue) ; ne planifie ni PowerShell 7 ni Go (`:197`) ; installe Scoop et change la politique d'exécution (`:251`). `setup-assets.ps1:17,102` : sans argument, cherche un clone `whisper.cpp` voisin, sinon n'installe aucun natif ; slug GitHub dupliqué. Vérifié dans le code.
- **Taskbar natif.** `Deckle.Shell.Taskbar.csproj:60,63` : DLL construite seulement avec Visual Studio, toolset v145, SDK 26100 ; sinon avertissement DECKLE001 et application sans DLL, sans échec du build. Vérifié dans le code.
- **Post-build `pwsh` et chien de garde.** `Directory.Build.targets:55`. Avertissement par projet sans `pwsh` ; tue les serveurs de build dotnet de tous les arbres de travail. Vérifié dans le code.
- **`build-locked.ps1:61,69`** : trois créneaux et `/m:4` dimensionnés pour 16 processeurs logiques. Probable.
- **Scripts qui arrêtent tous les `Deckle.exe`.** `publish-app.ps1:302`, `build-run.ps1:122`, `launch-app.ps1:76`, `lib/deckle-process.ps1:41`. Contredit la règle d'AGENTS.md. Vérifié dans le code.
- **Mono-mainteneur par construction.** `verify-branch.ps1:44` (un hôte SSH du mainteneur), `deploy-prod.ps1:138` (arbre `worktrees\` ; échoue si le dépôt est à la racine du disque), `release-validation.psm1:104` (branche `main`, origine github.com), `publish-app.ps1:134` (`<owner>/deckle`), `apply-anytype-schema.ps1:98` (port 33255, chemins absolus de la machine du mainteneur), `build-anytype-backend.ps1:118`. Vérifié dans le code.
- **Fuite d'identité dans un artefact public.** `publish-native-runtime.ps1:281,296` écrit `COMPUTERNAME` et les chemins de Visual Studio dans `PROVENANCE.txt` du zip public. Piste : retirer le nom de machine. Vérifié dans le code.
- **GGML_NATIVE non imposé.** `publish-native-runtime.ps1:261`. Si le bundle est reconstruit avec `GGML_NATIVE=ON`, le CPU de base peut exiger AVX2 ou AVX-512 : instruction illégale ailleurs. Spéculatif.
- **Liens symboliques.** `scripts/tests/lib/agent-skills-layout.tests.ps1:32` : `.claude\skills` doit être un vrai lien (mode développeur, `core.symlinks`), sinon la suite de tests de scripts échoue. Vérifié dans le code.
- **Hooks git.** `scripts/lib/hooks/validate-commit-attribution.ps1:78` et `pre-commit` : refusent toute co-signature, exigent `pwsh`, et mettent en index `TREE.md` et `INVENTORY.md`. Vérifié dans le code.
- **Réglage partagé.** `.claude/settings.json:15` : autorisation de `deploy-prod.ps1` propre au mainteneur dans un fichier versionné. Vérifié dans le code.
- **Autres outils maintainer.** `self-check.ps1:121` (jonctions vers ses modèles), `fetch-autocorrect-data.ps1:140,274` (corpus français de plusieurs Go), `LexiconBuilder.cs:54` (corpus bruts, juge LLM manuel), `--post-build` et bench `DECKLE_BENCH_DICTATION` livrés dans l'exe (`App.Startup.CommandLine.cs:86`), passerelle publiée dépendante du framework hors publication (`Deckle.App.csproj:48`). Vérifié / probable.
- Détail sans impact : dossier d'exemple `C:\Users\Public\Documents\Inbox` en capture (`PasteWindow.Capture.cs:15`), exemples en français dans les captures de Folders (`SampleFolder.cs:9`).

## Parakeet sur tous les GPU et CPU

Étude condensée. Étiquettes : [CODE] lu dans le dépôt, [DOC] documentation officielle lue, [SPEC] inférence non vérifiée.

### État actuel
- Un seul choix : le booléen `UseGpu` (vrai par défaut, partagé avec Whisper, `TranscriptionSettings.cs:114`) et la précision FP32 ou FP16 (FP32 par défaut, `:111`). Pas de BF16, pas de précision par appareil. [CODE]
- Chaîne en deux étapes dans `OnnxParakeetModelLoader.OpenEncoder` (lignes 50-68) : DirectML sur l'adaptateur 0, puis CPU avec `ProcessorCount / 2` threads. [CODE]
- `OnnxModelSession.cs:55-56` : toute erreur ORT sur le chemin DML (modèle invalide, mémoire insuffisante) devient « appareil indisponible » et bascule sur CPU. Seul le nom du type d'exception atteint le journal. [CODE]
- Le front-end mel et `decoder_joint` tournent toujours sur CPU, en FP32, un seul thread ; un appel ORT par pas de décodage. [CODE]
- Journal : `ParakeetBackend.Model.cs:66-85` écrit `directml_0` en dur ; `ModelOnCpu` dit « comme demandé par les réglages » même si le CPU vient d'un échec. [CODE]
- Pas de Windows ML (EP certifiés, NPU, TensorRT, OpenVINO) : aucune correspondance pour `ExecutionProviderCatalog`, etc. Seuls CPU et DML intégrés. [CODE]
- Pas de contrôle matériel au démarrage : la seule condition est « dossier modèle présent ». Aucune prévérification de mémoire vidéo (Whisper en a une, `GpuMemoryPreflight.cs:26-41`). [CODE]
- Mémoire mesurée par le projet (FP32, RX 7900 XT) : modèle au repos 2,1 à 2,5 Go ; DML 120 s : 5,0 Go dont 4,7 Go de VRAM ; 240 s : 8,7 Go ; 480 s : 15,6 Go ; CPU 120 s : 4,3 Go. Plafond fixé à 120 s. Aucune mesure pour FP16. [CODE]
- Fichiers : encodeur FP32 environ 2,3 Go, FP16 environ 1,16 Go, décodeur 69 Mo. Variantes int8/w4a8 existent mais sont exclues par la règle du projet. [CODE]
- Tests : l'adaptateur 999 permet de tester le repli sans GPU ; aucun test de `OpenEncoder` (pas de point d'injection). [CODE]
- `NOTICE.md:71-92` décrit encore `Microsoft.ML.OnnxRuntime.DirectML 1.23.0` : texte obsolète, à corriger. [CODE]

### Cas matériels

| Cas | Aujourd'hui | Ce qui peut casser | Statut |
| --- | --- | --- | --- |
| NVIDIA récent (RTX 30+) | DML adaptateur 0, FP32 | Fonctionne. Pas le plus rapide (TensorRT RTX non utilisé). 6 Go ou moins : risque à 120 s ; un manque de mémoire DML retombe sur CPU, plus lent. | chemin vérifié dans le code ; vitesse TRT non mesurée |
| NVIDIA ancien (Kepler–Pascal) | DML sur tout appareil DX12 | FP32 prudent ; peu de VRAM, FP16 lent ou absent. | documenté (DML) ; cartes précises spéculatives |
| AMD discret | DML adaptateur 0 | Cas mesuré du mainteneur (3,5 fois plus rapide que le CPU). Un iGPU en adaptateur 0 sur un bureau avec carte discrète serait choisi à tort. | vérifié et mesuré pour la 7900 XT ; autres cartes spéculatives |
| Intel Arc | DML adaptateur 0, FP32 | Probablement correct, pilotes variables. OpenVINO non utilisé. | spéculatif |
| GPU intégré (Intel/AMD) | DML adaptateur 0 aveugle | Mémoire partagée, bande passante faible : DML peut être plus lent que le CPU. Sur portable hybride, l'adaptateur 0 peut être l'iGPU, alors que la sonde Whisper prend la carte avec le plus de mémoire dédiée : incohérence. | documenté (ordre des adaptateurs) ; performances spéculatives |
| Snapdragon / NPU | QNN non enregistré : NPU jamais utilisé. Application x64 sous émulation. | Les EP Windows ML et pilotes NPU ne seraient pas atteignables depuis un processus émulé [SPEC]. QNN demande un modèle quantifié, interdit par le projet. | documenté (exigences QNN) ; conséquences de l'émulation spéculatives |
| Sans GPU, CPU avec AVX2 | CPU, `ProcessorCount / 2` threads, FP32 | Fonctionne ; environ 3,5 fois plus lent que la 7900 XT ; 4,3 Go à 120 s. Si FP16 est choisi, le graphe FP16 est chargé sur CPU (risque : lent, aucun gain mémoire, dérive possible). | vérifié dans le code ; vitesse mesurée sur une machine |
| Sans GPU, sans AVX2 | Non géré | Les binaires ORT x64 semblent supposer AVX2 (sources secondaires seulement) ; instruction illégale non rattrapable. | spéculatif, non vérifié |
| ARM64 | App x64 seule | `onnxruntime.dll` ARM64 et ARM64EC existent dans le paquet. Il faudrait aussi ggml/whisper, Win2D et le reste en ARM64 ; module Parakeet lui-même non bloquant. | Windows ML ARM64 documenté ; faisabilité de toute l'app spéculative |

Faits de documentation [DOC] : Windows ML prend en charge x64 et ARM64 ; CPU et GPU (DirectML) sur toutes les versions de Windows ; EP optimisés NPU et GPU spécifiques à partir de Windows 11 24H2 (build 26100). DirectML est en « sustained engineering » ; `device_id` suit l'ordre `EnumAdapters` et l'appareil 0 « est souvent le GPU principal d'affichage », pas toujours le plus performant.

### Changements recommandés (propositions, rien n'existe)
1. Chaîne de repli explicite `[DirectML(adaptateur) -> CPU]`, EP Windows ML hors de la v1 (téléchargement à la demande, donc en tension avec « pas de dépendance cloud », et réservé à 24H2). Ne plus transformer toute `OnnxRuntimeException` en « appareil indisponible » : distinguer absence d'appareil et échec de graphe ou de mémoire. Choisir l'adaptateur délibérément (DXGI, ignorer les adaptateurs logiciels, préférer le discret avec le plus de mémoire dédiée, réutiliser l'interop de `VideoMemoryProbe`). Ne pas écarter l'iGPU avant mesure : le journaliser.
2. Précision par appareil. FP32 par défaut partout (règle du projet sur la fidélité du français). FP16 en option et seulement sur DML ; sur CPU, forcer FP32 sauf mesure contraire. Une fonction unique « précision effective » journalisée à côté de la précision demandée. Pas de BF16 sans mesure. `decoder_joint` reste FP32 sur CPU.
3. Prévérification de VRAM sur le modèle de Whisper, mais avec une marge dépendant de la durée du morceau : environ 2,4 Go au-delà des poids à 120 s en FP32 et environ 1,9 Go de plus par minute supplémentaire sur DML [calcul à partir des mesures]. En cas de manque : morceau plus court (mais la coupe a déjà déplacé 48 transcriptions sur 57 de 30 s et plus par rapport à la référence), puis FP16 si permis, puis CPU. Et ajouter un repli CPU (ou un morceau plus court) quand l'encodage échoue en cours de prise (`ParakeetBackend.Transcribe.cs:154-162`).
4. Rapport d'appareil : vrai nom de l'adaptateur et indice à la place de `directml_0` ; précision demandée et effective ; raison du repli en vocabulaire fermé (aucun adaptateur, mémoire insuffisante, graphe refusé) ; distinguer « CPU parce que GPU désactivé », « pas de GPU » et « après échec ». Afficher la même information dans les Réglages en langage simple.
5. Tests sans posséder tous les GPU : point d'injection (`IOnnxSessionFactory`) pour tester `OpenEncoder` avec des faux ; fonctions pures pour le choix d'adaptateur, la marge et la précision effective ; adaptateur 999 (existe déjà) ; WARP avec un petit graphe Identity ; VM Hyper-V sans GPU et mode de compatibilité processeur pour le cas sans AVX2 (non vérifié) ; VM ARM64 ou kit de développement pour ARM64 ; retours de journaux utilisateur pour les cartes exotiques.

### Questions ouvertes (seule une mesure sur matériel réel tranche)
1. FP16 sur CPU x64 : chargement, vitesse, mémoire, fidélité sur le français.
2. FP16 sur DML (NVIDIA, AMD, Intel Arc, iGPU) : vitesse, VRAM, fidélité (le README indique un export visé pour WebGPU).
3. Comportement de DML sur une carte à mémoire insuffisante : échec à la création, exception rattrapable, ou blocage/TDR ? Cela décide si la prévérification est obligatoire.
4. Les EP Windows ML (TensorRT RTX, AMDGPU, OpenVINO) battent-ils DML ? Taille de téléchargement, compilation au premier lancement, prise en charge des opérateurs du Conformer et des fichiers `.data` externes.
5. GPU intégrés : DML contre CPU sur la même prise.
6. Portables hybrides : quel indice est le GPU discret ; l'ordre DXGI correspond-il au `device_id` de DML ?
7. Nombre de threads CPU (`ProcessorCount / 2`) sur machines à 4 cœurs.
8. x64 sans AVX2 : la `onnxruntime.dll` livrée démarre-t-elle ?
9. ARM64 : application native complète, DML sur Adreno, QNN avec un modèle FP16 non quantifié, vitesse sous émulation.
10. Windows 10 et Windows 11 avant 24H2 : exigences du runtime autonome Windows App SDK 2.5.1 non vérifiées.
11. Pic de mémoire FP16 à 120 s (seul FP32 mesuré).
12. `UseGpu` doit-il rester un booléen partagé avec Whisper, ou Parakeet a-t-il besoin de son propre réglage ?

## Ce qui reste incertain

- **Contrôle par échantillon.** 10 constats parmi les plus graves ouverts à la ligne citée (`ReleaseResolver.cs:30`, `NativeRuntime.cs:86`, `ParakeetModelFiles.cs:15`, `AnytypeCredentials.cs:71`, `DevSpace.cs:35`, `constant_return.cpp:21`, `ConservativeTypoCorrector.cs:83`, `HueBridgeClient.cs:344`, `MailWindow.xaml.cs:33`, `TranscriptionSettings.cs:115`) : 10 confirmés, 0 écarté. Le contrôle ne couvre que la présence du code cité ; les effets décrits (plantage, comportement ARM64, politique d'exécution) n'ont pas été reproduits.
- **Tranches manquantes** : aucune. Mais 10 tranches lues par échantillonnage de fichiers : un défaut hors des fichiers lus n'apparaît pas ici.
- **Doublons fusionnés** : par exemple Whisper `fr` (deux entrées), x64/ARM64 (cinq entrées), élévation/WMI (trois), autocorrection française (cinq), Parakeet sans téléchargement (trois), tag git (trois). La classification de gravité de certains constats a été relevée ou abaissée par jugement (Whisper `fr` et taskbar ARM64).
- **Spéculatif** : tout ce qui concerne ARM64 émulé, Smart App Control, WDAC, antivirus, certaines clés de registre non documentées de Windows, le comportement des autres pavés tactiles, économiseurs d'écran, éditions Windows N, `createdump`, et l'absence d'AVX2 (sources secondaires seulement).
- **Seule une vraie autre machine peut dire** : si le dépôt GitHub et la release sont réellement publics ; le premier lancement sur une installation Windows 11 retail propre (24H2/25H2) et sur Windows 10 ; le comportement avec un profil utilisateur non ASCII, un portable à deux GPU, un GPU intégré seul, un PC ARM64 ; SmartScreen et Defender sur l'installeur non signé ; une disposition de clavier non AZERTY ; l'absence de Git, de PowerShell 7, d'Ollama.
- **Hors périmètre de l'audit** : l'état de `deckle-scripts` (installé, non édité ici) ; la question de savoir si l'application doit officiellement viser des tiers (les agents l'ont supposé).


## Suite donnée à l'audit (2026-11-23)

Deux vagues de corrections sont entrées dans main : v0.146.1 (seize corrections de portabilité), v0.147.0 (choix par défaut décidés par le mainteneur) et v0.148.1 (Windows 11 minimum). Les décisions prises :

- À l'installation, seuls la dictée, la réécriture et la correction automatique sont cochés ; une installation existante garde ses modules.
- La correction automatique est française, active au premier lancement seulement si Windows est en français.
- La réécriture de paragraphe ne démarre que si Ollama répond et liste le modèle épinglé ; sinon la page le dit.
- Deckle n'est pas signé ; le README explique l'avertissement SmartScreen.
- L'installeur refuse ARM64, les CPU sans AVX2 et Windows 10, en disant pourquoi ; le moteur de barre des tâches reste coupé sur ces machines. `TargetPlatformMinVersion` vaut 22000 partout sauf dans l'installeur, qui garde 17763 et épingle `IlcInstructionSet` à `x86-64-v2` pour pouvoir démarrer et afficher son refus.
- La langue de dictée n'est plus une question : Whisper est retiré et Parakeet détecte la langue.

Ce qui reste, et où reprendre :

- **Désinstallation d'une ancienne installation à plat.** Les dossiers choisis finissent désormais par `Deckle`, et la désinstallation n'efface que ce qui appartient à Deckle dans la racine de données. Une installation faite par une version antérieure directement dans un dossier partagé serait encore vidée en entier : il faut un manifeste d'installation qui liste ce que Deckle a écrit (`src/Deckle.Install/FolderSafety.cs`, `src/Deckle.Setup/Uninstaller.cs`).
- **Folders et Orchestration.** Les corrections de franchise de l'interface sont faites ; le reste est une décision produit (tâches Anytype `folders-ui-honesty` et `orchestration-detection-notice`).
- **AVX2 dans l'installeur publié.** ILC accepte `x86-64-v2` ; l'édition de liens n'a pas tourné dans la console de l'agent (`vswhere.exe` absent du PATH). La prochaine publication du mainteneur le confirme.
- **Vulnérabilité NU1903** sur `Microsoft.DotNet.ILCompiler` 10.0.11 (décodeur Brotli de .NET) : l'installeur et le zip de v0.135.1 embarquent une version touchée, mais aucun code de Deckle ne décode du Brotli. La correction passe par une montée du SDK épinglé dans `global.json`.
- **Ce que seule une autre machine peut dire** : la liste de la section précédente tient toujours.

Les 64 tâches nées de l'audit sont dans Anytype, sous la tâche d'orchestration « Orchestrer les corrections de portabilité de l'installation ».
