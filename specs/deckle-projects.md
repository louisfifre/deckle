# Find any of your projects and reach its folders, its version and its scripts from one place

## Need

Draft, not confirmed

**Context of use.** The user works alone on their own machine and has several projects that they build and publish. Today they keep a terminal open only for the script menu of each project. They want to keep working this way, at least a little, without an LLM.

**User need.** The user needs to find a project fast and reach, from one place, its folders, its version and the scripts they run often, in order to build and publish all their projects.

**Intended outcome.**

- They group and sort their projects their own way, and find one fast.
- They see a project's version, and whether something is left uncommitted, without digging.
- They reach a project's folders, and the other tools that serve it, in one step.
- They run a project's usual action, and any other of its scripts, without an LLM and without a terminal kept open for it.
- Nothing runs unless they start it.
- They see when what an agent always reads in a project grows too big.
- Adding a project is simple, and removing one leaves its folder exactly as it was.
- Few things show, and there is nothing to manage.

## Journey

| The user wants to | The product must |
| --- | --- |
| add a project when it starts | take one folder, and ask nothing about it |
| sort the projects | let them create groups as they go and move projects between them |
| find a project and enter | show each project's name and version, by group, and open its page in one click |
| run the main action without entering | run it from the project's place among the others |
| go and work on it | open it in Deckle Folders, File Explorer, a terminal or its remote's web page |
| run anything else | open the project's script menu as it is, so nothing needs updating when scripts change |
| know where a run stands | say whether it runs, succeeded or failed, and bring its terminal to the front |
| stop following a project | remove it, and leave its folder exactly as it was |

When it goes another way:

- No project is added yet: the only thing to do is add one.
- A project has no script menu: nothing can be run from here, and the user can copy the command that installs one.
- A project's folder is gone: the project stays, says so, and comes back when the folder does.
- Several versions differ inside a project: the user is told how many, and which, and that it can be normal.
- An action already runs in a project: the project's other actions wait for it to end.

## Answer

Words a reader needs:

- A **project** is a folder the user added. Its name is its folder's name unless the user gave it another; that name lives in Deckle only.
- A **category** is a group the user creates and names. A project is in one category or in none; **Without a category** holds the others.
- **Home** is the screen of all the projects, by category.
- A **card** is a project's place on Home.
- The **script menu** is the project's `scripts/menu.ps1`, installed from the deckle-scripts repository.
- A **pinned action** is an entry of the script menu the user chose to keep at hand. One pinned action can be the **main action**.
- **Deckle Folders** is the module that shows files and draws the map of a folder. **Deckle Agents** will manage MCP servers and skills, and does not exist yet.

The screens are drawn in mockup v4; the wording is in the module's strings.

### The window

1. The title bar reads "Deckle Projects" and carries a button that opens and closes a side pane. The pane follows `navigation-pane/deckle-navigation-pane.md`: it never opens by itself.
2. The window opens on Home each time. The first time, the pane is its rail; after that, it is as the user left it.
3. Opened, the pane lists "All projects", then each category as a node holding its projects, then "Without a category" as a node. "All projects" shows Home, a category shows its page, a project shows its page.
4. On a category page and on a project page, the title bar also carries a back button, which returns to the screen the user came from.
5. There are no tabs, no presets and no charts.
6. One Refresh command, F5, reads again what the screen shows.

### Home before any project

1. The title "Projects", then, centred: "No projects yet", one sentence saying that a project is a folder the user adds and that Deckle reads its version, its scripts and what an agent reads there, and the main button "Add a project…".
2. There is no pane, no pane button and no "New category".

### Home with projects

1. The title "Projects" with, on its right, "New category" and "Add a project…"; then the categories, one under the other; then the fold "Without a category". No button of this screen is drawn as the main one.
2. A category shows its title, the number of its projects and a chevron, then its cards in a grid that puts as many cards across as the window's width allows. A category shows no description.
3. Clicking the category's title opens its page.
4. A category's cards fill at most three rows of the grid. With more projects than places, the last place is a tile "+ N more" that opens the category's page. The three rows hold whatever fits across at the window's width. Example: at a width where four fit, there are twelve places, and a category of fifteen shows eleven cards, then "+ 4 more"; where three fit, it shows eight cards, then "+ 7 more".
5. Categories come in the order the user created them.
6. "New category" adds a category at the end, with its title already a text box to type in.
7. A category with no project stays on Home with its title and one sentence saying to drag a project onto it.
8. "Without a category" is a fold at the bottom of Home, with the number of projects it holds, closed by default. Opened, it shows one line saying to drag a card onto a category or use its menu, then its cards in the same grid. With no project in it, the fold is not shown.

### A card

1. A card shows the project's name with a "…" button at its right, always visible; under the name, one meta line; then a footer. The card shows no icon and no path.
2. The meta line gives the version, then "N uncommitted" only when N is above zero: "v0.131.0 · 3 uncommitted", or "v0.6.0" alone. Without a version tag it reads "No version yet".
3. When Deckle found several versions that differ, the meta line says so in words, without an icon: "v0.32.0 · 3 versions found"; the detail is on the project page. With uncommitted files too, the count of versions comes before them.
4. A project with a main action has, alone in its card's footer, a button named after that action. Example: "Build & run" on deckle, "Server power" on homelab. A project without one has an empty footer.
5. Clicking the button runs the main action. Clicking anywhere else on the card, outside "…", opens the project page.
6. The "…" menu holds "Open in File Explorer" and "Open in terminal"; then "Move to", a submenu with one item per category the project is not in, then "New category…"; then "Rename" and "Remove from Deckle", whose tooltip says the folder stays. On a project that is in a category, "Move to" also holds an entry that puts it back in Without a category. "Open in File Explorer" and "Open in terminal" are disabled on a project whose folder was not found. The menu has no "Open": clicking the card does that.
7. A right-click on a card opens the same menu. It is the only menu.
8. "Rename" opens the project page with its name already a text box.
9. A card dragged onto a category joins it; dragged onto "Without a category", it leaves its category.

### Adding a project

1. "Add a project…" takes one folder and asks nothing about it.
2. A new project lands in Without a category, unless it was added from a category page; then it joins that category.
3. Adding a folder that is already a project opens its page and adds nothing.

### A category page

1. From top to bottom: the breadcrumb "Projects ›", the category's title, a line with the number of projects and that a row can be dragged to change the order, the list, and "Delete this category".
2. The list is plain: one row per project with a handle to drag it, the name over the full path, the same meta line as the card, and the main action's button when the project has one.
3. Clicking a row outside its button opens the project page.
4. Dragging a row by its handle changes the order of the projects in the category; Home shows them in that order.
5. The title is renamed in place the same way as a project's name.
6. "Delete this category" opens a small flyout under the button that asks to confirm, names the category, and says that its projects go back to Without a category, with one "Delete" button; Esc or a click away cancels. Confirmed, it does exactly that, and no project is removed. It asks even for an empty category.

### A project page

1. From top to bottom: the header, three readouts, "Open in", then "Pinned actions". The links come before the actions because the place of links matters most.
2. Header, on the left: the breadcrumb "Projects › " and the category (or "Projects" alone for a project without a category), the name, and the full path. On the right: "Open script menu" and the main action as the page's main button, named after the action. The main action appears there and nowhere else on the page. A project with no main action has no main button; a project without the script menu has neither button.

**Renaming.**

1. Hovering the name, or giving it keyboard focus, shows a small pencil beside it.
2. Clicking the name or the pencil turns the name into a text box holding the current name; "Rename" in the card's menu does the same. Beside the box, a line says that Enter keeps the name, Esc cancels, and the folder on disk keeps its name.
3. Enter keeps the name, Esc brings back the previous one, and an empty name brings back the folder's name.
4. The name is the user's label in Deckle only: the folder on disk is not renamed and the path does not change.

**Readouts.** Three plain readouts in one row, each a label above its value.

1. "Version": the highest Git tag, such as "v0.131.0". Without a tag: "No version yet".
2. Deckle also reads the version declared in the standard manifests it finds in the repository: `Cargo.toml`, `package.json`, `*.csproj` and `Directory.Build.props`, `pyproject.toml`, `gradle.properties` (`mod_version`) and `build.gradle`. "v0.32.0" and "0.32.0" are the same version.
3. When a manifest's version differs from the tag, a discreet info button, without colour, follows the value; its tooltip gives the number of versions found, the tag counted as one source. It opens a flyout under the readout: the number of versions found, one line per source with its value written without the "v", and a sentence saying that different versions can be normal when a repository holds several parts. Example: on minecraft-mods, Git tag 0.32.0, `launcher/app/Cargo.toml` 0.1.0, `mods/voice/gradle.properties` 0.1.0.
4. Without a tag, a project whose manifests declare a version still reads "No version yet", and the info button lists what the manifests say.
5. "Uncommitted": the number of files with uncommitted changes, "3 files", or "Nothing". In a folder that is not a Git repository it reads "Not a Git repository", and "Version" follows the rule of a project without a tag. The page says nothing about branches.
6. "An agent always reads": an estimate of the tokens an agent loads in every session there, such as "≈ 1,880 tokens". It counts AGENTS.md, CLAUDE.md and the skills' descriptions, which an agent loads when a session starts. It is there so that the user sees when the context overflows. The list of what an agent reads, item by item, is not on this page: it is the map in Deckle Folders.
7. While the readouts are being read, they show the previous values, or nothing on a first read.
8. The readouts and the cards' meta lines are read again when the screen opens, when the window comes back to the front, on Refresh, and when an action ends.

**Open in.** One row of buttons, each an icon and a name.

1. "Deckle Folders" shows the project there, with its files and its map. On a folder that Deckle Folders does not hold yet, it adds it there, then shows it.
2. "File Explorer" opens the project's folder.
3. "Terminal" opens the app's terminal in the project's folder.
4. The remote's web page, named after its host, such as "GitHub", is there only when the project's remote has one.
5. "Deckle Agents" joins the row once Deckle Agents exists, and is not shown before.

### Pinned actions

1. The section shows the entries of the script menu that the user pinned, the main one left out, as cards. Deckle does not list the whole menu: everything else is reached through "Open script menu".
2. "Pin an action…", on the section's title line, lists the entries the project's menu gives. The user pins and unpins entries there, and marks one pinned entry as main. An entry with variants is pinned with one of them. Example: "self-check.ps1 · Debug".
3. A card shows the menu's label, the script's file name and the variant, a state line, and "Run". Example: "Self-check", "self-check.ps1 · Debug", "Not run yet".
4. Under the cards, one line says that actions run in a terminal window, one at a time per project, and that every other script is in the script menu. It names no terminal, since the terminal is a choice with a fallback.
5. "Run" opens the terminal running `scripts/menu.ps1 -Run <id>` in the project's folder. The header's main button and the card's button on Home do the same for the main action. "Open script menu" opens the whole menu in a terminal.
6. Deckle adds no confirmation of its own before an action: what the menu asks, it asks in the terminal.
7. With a script menu and nothing pinned, the section shows "Pin an action…" and the line under the cards, and the header has "Open script menu" alone.
8. A project without the script menu shows, under "Pinned actions", one line saying it has no script menu and so nothing to pin, and the link "Copy the install command". The link copies the install line of deckle-scripts, and its tooltip shows that line. The user runs it in a terminal, and the page finds the menu when the window comes back to the front. Deckle installs nothing itself.
9. A menu that cannot be read shows that the script menu could not be read, and the error's last lines.
10. A pinned entry the menu no longer gives keeps its card, with "Run" disabled and a state line saying the menu no longer has it.

### While an action runs, and after

1. One action runs at a time per project.
2. While a pinned action runs, its state line shows a progress ring and how long it has run ("Running · 1 min 12 s"), and "Run" gives way to "Show terminal", which brings its terminal to the front.
3. While an action runs, each other pinned action of the project says it waits for the running one, named, to end, and its "Run" is disabled. The header's main button is disabled too, and the line under it says the main action waits, in place of the main action's last result, which comes back when nothing runs. "Open script menu" and the links stay usable, and so do the other projects.
4. While the main action runs, the line under the header shows the ring and how long it has run, and the main button gives way to "Show terminal".
5. When a pinned action ends, its state line says it succeeded or failed, how long ago, and the exit code; a failure carries a warning icon. Example: "Failed 4 min ago · exit code 1".
6. The main action's last result shows as one line under the header's buttons, with the same words and a warning icon for a failure; a success has no icon.
7. An action's result stays until that action runs again or Deckle closes. Example: the failure of "Build & run" is kept while "Self-check" runs, though the header's line gives way to the wait.
8. The output stays in the terminal; Deckle shows none of it. After a run that ended by itself, its window waits for a key, and while it is still open a small "Show terminal" button after the action's result line brings it to the front, on the card and in the header. Once the window is closed, the button goes.
9. There is no Stop button: the user stops an action in its terminal. Leaving the page, or closing the Projects window, does not stop a running action.
10. On Home and on a category page, a project with an action running shows a progress ring next to its main action's button, which is disabled; a project whose main action failed last shows the warning icon there.
11. The terminal that runs actions, opens the script menu and serves the "Terminal" link is one choice for the whole app, made in Deckle's settings; Windows Terminal by default. When the chosen terminal is missing from the machine, the action runs in Windows's default console host, and the action's state line says so.

### Removing a project, and a project whose folder is gone

1. "Remove from Deckle" needs no confirmation: it changes nothing on the disk, and adding the project again is one gesture. It cannot be done while an action of that project runs.
2. A project whose folder is gone keeps its card where it was, and its meta line says the folder was not found. Its page shows the name, the path and that sentence; no readouts, no links, no actions. When the folder is back, Refresh brings the page back.

### Rules

- Deckle never runs an action by itself: every run starts with the user's click on that action. Giving a version to other people is one of those clicks, and nothing else triggers it.
- Deckle keeps, per project: its folder, the user's name for it, its category and its place in it, its pinned actions, and which one is main. Everything else on screen is read from the folder each time.
- Everything on screen is found without a model.
- Deckle writes nothing about what an action is: "Build" and "Publish" are whatever the project's scripts are.
- Home is a clean board of cards, not a page of settings.
- Dependencies point one way: Projects uses the map's inventory of Deckle Folders and the project's script menu. Deckle Folders, Deckle Agents and the script menu work the same whether Projects exists or not.
- The interface is in English.

## Limits

- Projects is not part of the Deckle given to other people.
- Projects lists and links; it does not edit a file, a skill or an MCP server.
- It shows one checkout per project. A worktree is a project only if the user adds its folder.
- The token count is an estimate; it serves to compare and to see a drift, not to bill.
- Not covered in this version, each back only on the user's word: branches, the history curve, any Git visualisation; adding a parent folder that holds several projects; reordering the categories on Home; a description under a category's title; a PowerToys Command Palette extension ("Alt+Space build deckle"); the repository's numbers (lines, files, binaries); a terminal embedded in the page, or an action's output shown in Deckle; installing or updating the script menu in a project; editing or rendering a file, managing MCP servers and skills (Deckle Folders and Deckle Agents); an icon or a colour per project; searching the projects; any tie with the Project objects of the Anytype management space; a history of past runs, a schedule, or an action started by an agent; commands for agents over Projects.
- Not verified: whether Deckle can follow a process it starts in Windows Terminal and read its exit code. The running state, "Show terminal" and the result lines depend on it.
- Not verified: which manifest files count as a project's version on the user's repositories. The set above is a first list; a repository with many `package.json` or `*.csproj` files may give more lines than are useful.
- Not verified: whether `menu.ps1 -Run <id>` asks the menu's confirmation for an entry marked dangerous.
- Not verified: whether the user's other projects carry the script menu. One that does not gets the page without actions.
- Known trap: in Deckle's own repository, building on main closes the running Deckle, and so the window that follows the action. See Open 2.
- Deckle is Windows-only.

## Open

1. **At which total does the context overflow?** "An agent always reads" shows a plain number. Options: A, no mark, the user judges the number; B, a figure the user gives, the same for every project, above which the readout says by how much. Built today: A. Depends on it: nothing else.
2. **What happens to an action that closes Deckle itself?** Only for Deckle's own repository. Options: A, no special treatment, the terminal goes on and the result is not shown when Deckle opens again; B, Deckle remembers the run and shows its result when it opens again. Built today: A. Depends on it: whether a run must outlive Deckle, which weighs on how the process is started.
3. **Is a project the same thing as an added folder of Deckle Folders?** The user said the two are linked without saying how. Options: A, two lists; B, one list, every project being an added folder. Built today: A. Depends on it: where "Add a project…" writes, and what "Remove from Deckle" does in Deckle Folders.
4. **What does "Deckle Scripts … c'est Deckle Project" mean?** Read as: Deckle Projects drives the script menu the same way the terminal does. Options: A, that reading only, the deckle-scripts repository keeps its name; B, the repository and its menu are renamed too. Built today: A. Depends on it: the install line the "Copy the install command" link copies, and the words "script menu" on the page.
5. **Where is the window opened from, and is "Deckle Projects" its name?** The name is a working name. Until answered: the window is opened the way Deckle Folders' window is. Depends on it: the window's place among Deckle's entries.
