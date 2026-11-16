# Deckle Projects (working name)

Draft of 2026-11-15, written without a question round, at Louis's request: "je veux quelque chose de minimaliste", "mature", answering the needs he already expressed. Each sentence ends with its source. **(Louis, date)** restates his words, as collected in the needs file of the 2026-11-15 orchestration session or as restated in `specs/atelier.md`; his feedback on mockup v3, given by voice on 2026-11-15, is cited as (Louis, 2026-11-15, on v3). **(session)** is a decision the session took on 2026-11-15 because Louis delegated the gaps; he has not reviewed any of them yet, and his review can overturn each one. The labels written here are the ones on screen: Deckle ships English strings only (session, checked in `src/*/Strings/en-US`).

The spec matches mockup v4 as changed after its UX review; a sentence that comes from that review is marked (session, after the v4 review). Its screens are cited by their code: H0 no projects yet, H1 Home with the pane closed, H2 Home in a wide window with the pane open, H3 Home with "Without a category" opened and a card's menu, C1 a category page, P1 a project page, P2 a project page with three versions and no script menu, P3 a project page with an action running and another failed, P4 a project page while its name is edited (session).

## Intention

Louis, alone, on his own machine, has several projects and wants one basic place to build and publish all of them (Louis, 2026-11-15: "un dashboard pour publier et builder tous les projets que j'ai"). It is above all a hub of links to the other apps, where he groups, sorts, sees, arranges and finds a project fast (Louis, 2026-11-15, on v3). He opens it to pick a project and reach, from one page, its folders, its version, and the scripts he runs often (Louis, 2026-11-15: "une page par projet ... des stats dessus, des liens vers les dossiers et des options de build et lancements de scripts").

It replaces the terminal he kept open only for his script menu (Louis, 2026-11-08), and that menu stays usable without it and without an LLM (Louis, 2026-11-08: "si jamais je me passe de LLM, il faut que je puisse un peu utiliser ça").

It shows the least possible: few things, nothing to manage (Louis, 2026-11-10, said of the Atelier; "minimaliste" for Projects on 2026-11-15). It is a clean dashboard of cards, not a Settings page (Louis, 2026-11-15, on v3: v3 was "fouillis", too much like a Settings page).

It is not part of the Deckle given to other people (session).

Its neighbours are Deckle Folders, which shows files and draws the map of a folder, and Deckle Agents, which will manage MCP servers and skills and does not exist yet (Louis, 2026-11-15, names the three; the boundaries between them are the session's). "Deckle Folders" is the module `specs/atelier.md` calls Deckle Atelier (session's reading of his message).

## Journeys

**Add a project.** Louis chooses "Add a project…", picks one folder, and its card is on Home under "Without a category" (session). Nothing is asked about it (Louis, 2026-11-15: "ajouter des projets ... de facon tres simple"). Projects are added one at a time, when a project starts; a folder on disk can stay out (Louis, 2026-11-15, on v3).

**Sort the projects.** Louis creates a category with "New category", names it, and puts projects in it by dragging their cards or through their menu (Louis, 2026-11-15, on v3: categories he creates as he goes, such as Learning, Development, Games, Leisure; the gestures are the session's).

**Find a project and enter.** On Home Louis reads each project's name and version, and clicks its card to open its page (Louis, 2026-11-15, on v3: "click the whole card to enter").

**Run the main action without entering.** On a card whose project has a main action, Louis clicks the card's button, for example "Build & run", and a terminal opens running it (Louis, 2026-11-15, on v3: a dedicated button on the card runs the action he marked as main).

**Go and work.** From the project page Louis opens the project in Deckle Folders, File Explorer, a terminal or its remote's web page, and continues there (Louis, 2026-11-15: "des liens vers les dossiers"; on v3: "links with their text: keep").

**Run anything else.** Louis clicks "Open script menu", and his script menu opens in a terminal, where he picks any script (Louis, 2026-11-15, on v3: run the script menu itself in a terminal, so nothing needs updating when scripts change).

**Remove a project.** Louis chooses "Remove from Deckle" in a card's menu. Its folder is left exactly as it was (session).

## Surfaces

### Window

- A window of Deckle whose title bar reads "Deckle Projects" and carries a hamburger button that opens and closes a side pane (session, H1).
- The pane is closed when the window opens (session, H1; Louis, 2026-11-15, on v3: behind a toggle, closed most of the time).
- Opened, the pane lists "All projects", then each category as a node holding its projects, then "Without a category" as a node (session, H2). "All projects" shows Home, a category shows its page, a project shows its page (session).
- On a category page and on a project page, the title bar also carries a back button, which returns to the screen Louis came from (session, C1, P1).
- The window opens on Home each time (session).
- There are no tabs, no presets and no charts (Louis, 2026-11-10, for the Atelier: tabs and presets not wanted, charts not read well; applied here by the session).
- One Refresh command, F5, reads again what the screen shows (session, as the Atelier does).

### Home

**Before any project is added (H0).** The title "Projects", then, centred: "No projects yet", the sentence "A project is a folder you add. Deckle reads its version, its scripts and what an agent reads there.", and the accent button "Add a project…" (session, H0; Louis, 2026-11-15, on v3: arrive, nothing, add a project). There is no pane button and no "New category" (session, H0).

**With projects (H1).** The title "Projects" with, on its right, "New category" as a subtle button and "Add a project…" as a standard button; then the categories, one under the other; then the fold "Without a category" (session, H1). No button on this screen takes the accent colour (session, after the v4 review).

- A category shows its title, the number of its projects, and a chevron; then its cards in a grid that puts as many cards across as the window's width allows, each at least 240 px wide (session, after the v4 review, H1). A category has a title and nothing else: no description is shown (session; Louis, 2026-11-15, on v3, called the description optional).
- Clicking the category's title opens its page (session, C1).
- A category's cards fill at most three rows of the grid. With more projects than places, the last place is a tile "+ N more" that opens the category's page (session; Louis, 2026-11-15, on v3: three rows at most, then a "+" to a category page). The three rows hold whatever fits across at the window's width. Example: at a width where four fit, there are twelve places, and a category of fifteen shows eleven cards, then "+ 4 more"; at a width where three fit, it shows eight cards, then "+ 7 more" (session, after the v4 review). No screen of v4 draws this tile (session).
- Categories come in the order Louis created them (session).
- "New category" adds a category at the end, with its title already a text box to type in (session; not drawn in v4).
- A category with no project stays on Home with its title and one sentence saying to drag a project onto it (session; not drawn in v4).

**Without a category.** A fold at the bottom of Home, with the number of projects it holds, closed by default (session, H1; Louis, 2026-11-15, on v3: at the bottom, hidden behind a toggle).

- Opened, it shows the line "Drag a card onto a category, or use its menu." and its cards in the same grid (session, H3).
- With no project in it, the fold is not shown (session; not drawn in v4).

**A card.** One per project (session, H1).

- It shows the project's name, which is its folder's name unless Louis gave it another, with a "…" button at its right, always visible; under the name one meta line; and a footer (session, after the v4 review, H1). The card has no icon and no path (session, after the v4 review).
- The meta line gives the version, then "N uncommitted" only when N is above zero: "v0.131.0 · 3 uncommitted", or "v0.6.0" alone (session, H1). Without a version tag it reads "No version yet" (session, H1).
- When Deckle found several versions that differ, the meta line says so in words, without an icon: "v0.32.0 · 3 versions found"; the detail is on the project page (session, after the v4 review, H1, P2). With uncommitted files too, the count of versions comes before them (session; not drawn in v4).
- A project with a main action has, alone in its card's footer, a button named after that action: "Build & run" on deckle, "Server power" on homelab. A project without one has an empty footer (session, H1).
- Clicking the button runs the main action. Clicking anywhere else on the card, outside "…", opens the project page (Louis, 2026-11-15, on v3).
- The "…" menu holds "Open in File Explorer" and "Open in terminal"; then "Move to", a submenu that holds one item per category the project is not in, then "New category…"; then "Rename" and "Remove from Deckle", whose tooltip says "Folder stays" (session, after the v4 review, H3; the submenu is from the second pass). "Open in File Explorer" and "Open in terminal" are off on a project whose folder was not found. The menu has no "Open": clicking the card does that (session, after the v4 review).
- On a project that is in a category, the "Move to" submenu also holds an entry that puts it back in Without a category (session; not drawn in v4).
- "Rename" opens the project page with its name already a text box (session, after the v4 review).
- A card dragged onto a category joins it; dragged onto "Without a category", it leaves its category (session; Louis, 2026-11-15, on v3: by selecting a project or by drag and drop).

### Category page (C1)

- From top to bottom: the breadcrumb "Projects ›", the category's title, the line "4 projects · drag a row to change the order", the list, and the "Delete this category" button (session, C1).
- The list is plain: one row per project with a gripper handle, the name over the full path, the same meta line as the card, and the main action's button when the project has one (session, C1; Louis, 2026-11-15, on v3: "a category page that is a plain list").
- Clicking a row outside its button opens the project page (session).
- Dragging a row by its gripper changes the order of the projects in the category; Home shows them in that order (session, C1).
- The title is renamed in place the same way as a project's name: a pencil shows on hover or focus (session, C1; see Renaming).
- "Delete this category" opens a small flyout under the button that asks "Delete <name>? Its N projects go back to Without a category." with one "Delete" button; Esc or a click away cancels. Confirmed, it does exactly that, and no project is removed. It asks even for an empty category (session, C1; second pass).
- A project added from this page joins this category (session). C1 does not draw the command "Add a project…" (session).

### Project page

From top to bottom: header, three readouts, "Open in", "Pinned actions" (session, after the v4 review, P1). The links come before the actions because the hub of links matters most (Louis, 2026-11-15, on v3).

**Header.** On the left, the breadcrumb "Projects › Development", the name (a pencil shows beside it on hover or focus, see Renaming), the full path (session, after the v4 review, P1). For a project without a category the breadcrumb is "Projects" alone (session; not drawn in v4). On the right, "Open script menu" and the main action as the accent button, named after the action: "Build & run" (session, P1). The main action appears there and nowhere else on the page (session, after the v4 review). A project with no main action has no accent button; a project without the script menu has neither button (session, P2).

**Renaming (P4).** Hovering the name, or giving it keyboard focus, shows a small pencil beside it (Louis, 2026-11-15, on v3: an edit mark that appears on hover). Clicking the name or the pencil turns the name into a text box holding the current name; "Rename" in the card's menu does the same (session, P4). The review proposed a pencil that is always there; the session kept Louis's hover mark, since the card menu's "Rename" already gives a visible way in (session, after the v4 review). Beside the box: "Enter to keep, Esc to cancel. The folder on disk keeps its name." (session, after the v4 review, P4). Enter keeps the name, Esc brings back the previous one, and an empty name brings back the folder's name (session). The name is Louis's label in Deckle only: the folder on disk is not renamed and the path does not change (session, P4).

**Readouts.** Three plain readouts in one row, each a label above its value, with no card around them and no line under them (session, after the v4 review, P1).

- "Version": the highest Git tag, "v0.131.0" (session, P1). Without a tag: "No version yet" (session).
- Deckle also reads the version declared in the standard manifests it finds in the repository. The first version's set is `Cargo.toml`, `package.json`, `*.csproj` and `Directory.Build.props`, `pyproject.toml`, `gradle.properties` (`mod_version`) and `build.gradle` (session; Louis, 2026-11-15, on v3: read what is standard across languages).
- When a manifest's version differs from the tag, a subtle info button, without colour, follows the value; its tooltip is "3 versions found" (session, after the v4 review, P2; Louis, 2026-11-15, on v3: "a small plain mention (no colour) with an icon he can click"). The count is the number of sources, the tag included (session, P2). "v0.32.0" and "0.32.0" are the same version (session).
- The info button opens a flyout under the readout: "3 versions found", one line per source with its value written without the "v", and "When a repository holds several parts, different versions can be normal." (session, after the v4 review, P2). On minecraft-mods: Git tag 0.32.0, `launcher/app/Cargo.toml` 0.1.0, `mods/voice/gradle.properties` 0.1.0 (session, P2, read from the repository).
- Without a tag, a project whose manifests declare a version still reads "No version yet", and the info button lists what the manifests say (session; not drawn in v4).
- "Uncommitted": the number of files with uncommitted changes, "3 files", or "Nothing" (session, P1, P2). The page says nothing about branches (Louis, 2026-11-15, on v3: keep version and uncommitted count, branches not needed).
- "An agent always reads": an estimate of the tokens an agent loads in every session there, "≈ 1,880 tokens" (session, P1). It is there so that Louis sees when the context overflows (Louis, 2026-11-08: the context "a un petit peu débordé"). It counts AGENTS.md, CLAUDE.md and the skills' descriptions, which an agent loads when a session starts (session).
- The list of what an agent reads, element by element, is not on this page: it is the map in Deckle Folders (session).
- In a folder that is not a Git repository, "Uncommitted" reads "Not a Git repository", and "Version" follows the rule of a project without a tag (session; not drawn in v4).
- While the readouts are read, they show the previous values, or nothing on a first read (session).

**Open in.** One row of standard buttons, each an icon and a name (session, after the v4 review, P1).

- "Deckle Folders" shows the project there, with its files and its map (session, P1).
- "File Explorer" opens the project's folder (session, P1).
- "Terminal" opens the app's terminal in the project's folder (session, P1).
- The remote's web page, named after its host, "GitHub", is there only when the project's remote has one (session, after the v4 review, P1; absent on P2).
- "Deckle Agents" joins the row once Deckle Agents exists, and is not shown before (session, after the v4 review).

**Pinned actions.** The entries of the project's script menu that Louis pinned, the main one left out, as cards (session, after the v4 review, P1: "Self-check" and "Update prod"; "Build & run" is in the header). Deckle does not list the whole menu: everything else is reached through "Open script menu" (session; Louis, 2026-11-15, on v3: "keep it open so nothing needs updating when scripts change").

- "Pin an action…", on the section's title line, lists the entries the project's menu gives (`scripts/menu.ps1 -List`). Louis pins and unpins entries there, and marks one pinned entry as main (session; the list itself is not drawn in v4).
- An entry with variants is pinned with one of them (session, P1: "self-check.ps1 · Debug").
- A card shows the menu's label, the script's file name and the variant, a state line, and "Run": "Self-check", "self-check.ps1 · Debug", "Not run yet" (session, P1).
- Under the cards: "Actions run in a terminal window, one at a time per project. Every other script is in the script menu." (it names no product, because the terminal is a choice with a fallback) (session, after the v4 review, P1).
- "Run" opens Windows Terminal running `scripts/menu.ps1 -Run <id>` in the project's folder (session). The header's accent button and the Home card's button do the same for the main action (session).
- While a pinned action runs, its state line shows a progress ring and "Running · 1 min 12 s", and "Run" gives way to "Show terminal", which brings its terminal to the front (session, after the v4 review, P3).
- While an action runs, each other pinned action reads "Waits for Self-check to end", with the running action's label, and its "Run" is disabled; the header's accent button is disabled too, and the line under it reads "Build & run waits for Self-check to end" in place of the main action's last result, which comes back when nothing runs (session, after the v4 review, P3; third pass). "Open script menu" and the links stay usable, and so do the other projects (session, P3).
- Deckle has no Stop button: Louis stops an action in its terminal (session, P3).
- When a pinned action ends, its state line reads "Succeeded 4 min ago · exit code 0" or, with a warning icon in the critical colour, "Failed 4 min ago · exit code 1" (session; not drawn in v4).
- The main action's last result shows as one line under the header's buttons: a warning icon in the critical colour and "Build & run failed 4 min ago · exit code 1" (session, after the v4 review, P3). A success reads "Build & run succeeded 4 min ago · exit code 0", without an icon (session; not drawn in v4).
- While the main action runs, that line shows the ring and "Build & run running · 1 min 12 s", and the accent button gives way to "Show terminal" (session; not drawn in v4).
- The output stays in the terminal; Deckle shows none of it (session). After a run that ended by itself, its window waits for a key, and while it is still open a small "Show terminal" button after the action's result line brings it to the front, on the card and in the header; once the window is closed the button goes (session, third pass).
- An action's result stays until that action runs again or Deckle closes (session, P3: the failure of "Build & run" is kept while "Self-check" runs, though the header's line gives way to the wait).
- With a script menu and nothing pinned, the section shows "Pin an action…" and the line under the cards, and the header has "Open script menu" alone (session; not drawn in v4).
- A project without the script menu shows, under "Pinned actions", one line, "This project has no script menu, so there is nothing to pin.", and the link "Copy the install command" (session, after the v4 review, P2). The link copies the install line of deckle-scripts, and its tooltip shows that line; Louis runs it in a terminal, and the page finds the menu when the window comes back to the front (session). Deckle installs nothing itself (session).
- A menu that cannot be read shows "The script menu could not be read" and the error's last lines (session; not drawn in v4).
- A pinned entry the menu no longer gives keeps its card, with "Run" disabled and a state line saying the menu no longer has it (session; not drawn in v4).

### A project whose folder is gone

Its card stays where it was, and its meta line reads "This folder was not found" (session; not drawn in v4). Its page shows the name, the path and that sentence; no readouts, no links, no actions (session). When the folder is back, Refresh brings the page back (session).

## Behaviours

- Deckle never runs an action by itself: every run starts with Louis's click on that action (session; Louis, 2026-11-08: "si je laisse tout faire, c'est pas bon. Donc il faut quand même que j'ai un peu la main").
- Giving a version to other people is one of those clicks and nothing else triggers it (session; the project's rule that a release is published on Louis's word).
- Deckle keeps, per project: its folder, Louis's name for it, its category and its place in it, its pinned actions, and which one is main. Everything else on screen is read from the folder each time (session).
- A project is in one category or in none (session).
- A new project lands in Without a category, unless it was added from a category page; then it joins that category (session).
- Adding a folder that is already a project opens its page and adds nothing (session).
- Removing a project needs no confirmation: it changes nothing on the disk and adding it again is one gesture. It cannot be done while an action of that project runs (session).
- One action at a time per project (session).
- On Home and on a category page, a project with an action running shows a progress ring next to its main-action button, which is disabled; a project whose main action failed last shows the warning icon in the critical colour there (session, after the v4 review; not drawn in v4).
- When the chosen terminal is missing from the machine, the action runs in Windows's default console host, and the action's state line says so (session, after the v4 review).
- Leaving the page, or closing the Projects window, does not stop a running action: it lives in its terminal (session).
- The terminal that runs actions, opens the script menu and serves the "Terminal" link is one choice for the whole app, made in Deckle's settings; Windows Terminal by default (session; Louis, 2026-11-15, on v3: Deckle may propose the terminals present on the machine, Windows default for now).
- Deckle adds no confirmation of its own before an action: what the menu asks, it asks in the terminal (session).
- Deckle writes nothing about what an action is: "Build" and "Publish" are whatever the project's scripts are (session).
- The readouts and the cards' meta lines are read again when the screen opens, when the window comes back to the front, on Refresh, and when an action ends (session).
- Everything on screen is found without a model (Louis, 2026-11-10: "the most is still done without a model").
- A card's menu is the only menu; a right-click on a card opens the same one (session; Louis, 2026-11-08: "j'ai pas besoin ... d'avoir un clic droit énorme").
- Dependencies point one way: Projects uses the map's inventory of Deckle Folders and the project's script menu. Deckle Folders, Deckle Agents and the script menu work the same whether Projects exists or not (session).
- "Deckle Folders" on a folder that Deckle Folders does not hold yet adds it there, then shows it (session).

## Limits

- Projects lists and links; it does not edit a file, a skill or an MCP server (session).
- It shows one checkout per project. A worktree is a project only if Louis adds its folder (session).
- The token count is an estimate; it serves to compare and to see a drift, not to bill (session).
- Not verified: whether Deckle can follow a process it starts in Windows Terminal and read its exit code. The likely way is that Deckle starts the shell process itself and lets Windows host it in the terminal. The running state, "Show terminal", and the "succeeded / failed · exit code N" lines all depend on it (session).
- Verified on 2026-11-15: Windows Terminal 1.25 is installed on Louis's machine, `wt.exe` in WindowsApps (session).
- Not verified: which manifest files count as a project's version on Louis's repositories. The set above is a first list; a repository with many `package.json` or `*.csproj` files may give more lines than are useful (session).
- Not verified: whether `menu.ps1 -Run <id>` asks the menu's confirmation for an entry marked dangerous (session).
- Not verified: whether Louis's other projects carry the script menu. One that does not gets the page without actions, as P2 (session).
- Known trap: in Deckle's own repository, building on main closes the running Deckle (Louis, 2026-11-08: "quand je build sur main, ça la quitte"), and so the window that follows the action. See Open 2.
- Deckle is Windows-only. On Linux the same shape would use a terminal the user chose, for example `$TERMINAL`; this is noted for Louis's thinking and nothing is built for it (session; Louis, 2026-11-15, on v3: "think how it would work on Linux").

## Out of this version

Decided by the session unless a source says otherwise; each comes back only on Louis's word.

- Branches, the history curve, any Git visualisation (Louis, 2026-11-15, on v3: not needed yet).
- Adding a parent folder that holds several projects (Louis, 2026-11-15, on v3: considered and set aside).
- Reordering the categories on Home.
- A description under a category's title.
- A PowerToys Command Palette extension, "Alt+Space build deckle" (Louis, 2026-11-15, on v3: later).
- The repository numbers: lines, files, binaries (Louis named them on 2026-11-08).
- A terminal embedded in the page, or an action's output shown in Deckle.
- Installing or updating the script menu in a project: the page only copies the install command, Louis runs it.
- Editing or rendering a file; managing MCP servers and skills (Deckle Folders and Deckle Agents).
- An icon or a colour per project; searching the projects.
- Any tie with the Project objects of the Anytype management space.
- A history of past runs, a schedule, or an action started by an agent.
- Commands for agents over Projects.

## Open

1. **At which total does the context overflow?** "An agent always reads" shows a plain number. Until Louis names a figure, nothing marks it as too much.
   - A. No mark: Louis judges the number.
   - B. A figure he gives, the same for every project; above it the readout says by how much.
   - Depends on it: nothing else.
2. **What happens to an action that closes Deckle itself?** Only for Deckle's own repository.
   - A. No special treatment: the terminal goes on, and the result is not shown when Deckle opens again.
   - B. Deckle remembers the run and shows its result when it opens again.
   - Until answered: A. Depends on it: whether a run must outlive Deckle, which weighs on how the process is started (Limits).
3. **Is a project the same thing as an added folder of Deckle Folders?** The session kept two lists; Louis said the two are "lié" (2026-11-15) without saying how.
   - A. Two lists, as written.
   - B. One list: every project is an added folder.
   - Depends on it: where "Add a project…" writes, and what "Remove from Deckle" does in Deckle Folders.
4. **Does the pane open by itself in a wide window?** Louis said: closed most of the time, visible when the window is large (2026-11-15, on v3). H2 draws it open at 1600 px.
   - A. Never by itself: only the hamburger button opens it.
   - B. Open by itself above a width, closed under it, as Windows Settings does.
   - Until answered: A. Depends on it: the width at which it opens; Home's grid then holds fewer cards across.
5. **What does "Deckle Scripts … c'est Deckle Project" mean?** (Louis, 2026-11-15.) The session reads it as: Deckle Projects drives the script menu the same way the terminal does.
   - A. That reading only; the deckle-scripts repository keeps its name.
   - B. The repository and its menu are renamed too.
   - Depends on it: the install line that P2's link copies, and the words "script menu" on the page.
6. **Where is the window opened from?** "Deckle Projects" is Louis's word of 2026-11-15, taken as a working name. The session did not decide its place among Deckle's entries.
   - Until answered: a builder follows what the Atelier's window does.
