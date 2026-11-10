# Deckle Atelier (working name)

Draft of 2026-11-10, third revision of the day, after Louis's review of pass 12 (canvas page "Référence"). Every sentence above **Open** restates something Louis said: the notes of 2026-11-08, the dictation and the grill answers of 2026-11-09, his long feedback on the first drawn pass and his feedback on the second drawn pass, both on 2026-11-10, and his dictated review of pass 12 the same day. The section "As drawn" is the one exception: it reads the boards, which Louis accepted as the starting point. A session's proposal never sits above Open; it sits under Open as an option. Where Louis said something with a doubt, the doubt is written.

## Intention

Louis, on his own Windows machine, looks at his files through one app instead of File Explorer plus one tool per job. He opens it when he needs to know where he stands: what is in a folder, what it weighs, what a photo holds, what can be used or run there. It follows the look and the habits of Windows 11 and PowerToys.

It shows the least possible. Something appears on screen because Louis felt the need for it, not because it could be shown. He calls it a tool to stay calm: few things, nothing to manage. The screens are thought for a person who uses them, not for an LLM: a small local model helps in a few places and nothing depends on a large one. Next to the screens, the app is thought for agents from the start (Louis, 2026-11-10): "everyone will have small things running", so what the app can do is also reachable as commands.

The value Louis expects is in the specialised tools (sorting photos, sizes, the map of links), on top of an explorer that feels familiar.

## Journeys

**Sort the photos of a shoot.** Louis adds the folder of the shoot and switches to the photo tool. He sees the photos large. He selects one: the panel shows what the camera recorded and a written description. He marks the photos he keeps and the ones he does not. He can do it with the keyboard or by voice. When he is done, he applies the marks in one step that first shows what will happen. Then he opens darktable on that folder and retouches there. Nothing is retouched in this app.

**Find a photo later.** Louis opens the search and types what he remembers, for example the name of a person. The results come from every folder he added.

**Free some space.** Louis opens a folder and sees the size of every folder beneath it. He finds what weighs the most and cleans. The same tool serves to clean a whole PC.

**Picture a repository.** In a project folder, Louis sees what can be used there: the scripts he can run, the files an LLM reads, the skills, the MCP servers, and how they link to each other. He uses it to picture where he stands in a project and to see where and why it starts to overflow.

## Surfaces

### Window

- The top of the window is one row, in this order: the button that opens the pane of added folders, the app icon, the name "Deckle Atelier", Back, Forward, Up, Refresh, the address bar, the tools, the search button, the window buttons. There is no empty band above it.
- Louis changes tool in the top row, right after the address bar: one button per tool, the active one shows its name. The address bar loses some room for it and Louis accepts that.
- The window buttons are Windows' own (minimise, maximise, close) and take the full height of the row.
- Every button of the row looks the same; search is one of them, an icon.
- The address bar behaves like File Explorer's. It shows the path as segments. A chevron between two segments opens the list of folders at that level. A click on a segment goes there. A click on the empty part of the bar turns it into a text field, with suggestions while typing.
- There are no tabs.
- The address bar shows a path in every tool, the photo tool included.
- The app shows only the folders Louis added. Adding a folder asks nothing about what it holds: the app finds by itself the photos, the documents, the code and the scripts in it.
- The added folders sit in a pane on the left, reduced to their icons. The button at the left end of the top row opens it; opened, it shows their names.
- Each added folder has an icon. The app picks it when the folder is added. Louis changes it once he is in that folder, by a click on its icon; never by a right-click. The choice offers icons and a colour, so that folders reduced to icons can still be told apart. There are no shapes to choose (decided 2026-11-10). Without a background, the icon itself takes the colour. With the background option on, the background takes the colour and is always a square. The picker shows two labels, "Icône" and "Couleur", and no title naming the folder.
- The icon picker is one component shared across Deckle apps: Deckle Mail uses the same one for a mailbox (Louis, in the Deckle Mail review of 2026-11-10, as reported by that session). Its set of icons stays small: from two rows of four to three rows of six.
- Several tools look at the same folder: the file list, the photo tool, the sizes, the map. Changing tool keeps the folder. Louis said the selection should "maybe" stay too; it stays until he says otherwise.
- The content is in the centre. It can be shown three ways: a compact list, large rows, a gallery.
- Panels sit on the sides of the centre, a details panel on the right. A tool changes what the panels hold, not where they are.

### Details panel

- What the panel shows depends on the kind of item selected, and on the tool: a photo selected in the file list shows its data; in the photo tool the folder is a gallery.
- For a photo: the date it was taken, the camera, the lens, the exposure, the place when the file holds one, the written description, the people recognised, and its mark.
- For a photo, the panel is where Louis asks the model for a description of that photo.
- For an item that has links, the panel separates its details from its links.
- The titles of the panel's sections stand out from what they hold: more room above a title, a stronger weight or size.
- The panel shows what Louis needs at a glance and nothing else. What that is, per kind of item, is asked before it is drawn.
- A new kind of content brings its own panel content; the window around it stays the same.

### File list

- The file tool is the plain view of a folder: a details list with a header row, name, date modified, size. Louis approved it as drawn on 2026-11-10 (artboard 2.C11).
- Every file shows an icon for its type, whatever the type. A PDF and a Markdown file are told apart by their icon. Not urgent: the app can start without it.
- Actions that appear on a row when the pointer is over it: two at most. The room they take is the same on every row, so that opening a sub-folder whose rows have other actions never shifts the columns.

### Photos

- The photo tool shows the photos of the folder Louis is in, as a gallery. It serves to see, sort and select; never to retouch.
- Before any folder is added: nothing is shown except the way to add a folder and three short lines on what happens next (add a folder, ask for descriptions if wanted, search finds them). Photos never fills itself from the disk. Louis approved this screen as drawn on 2026-11-09 (artboard R4).
- A photo can be seen large inside the app.
- A model running on this machine can describe one photo, or go through a whole folder and describe each photo. One click starts it.
- A renamed photo has a name that starts with the date it was taken, year then month then day, so that names sort by time.
- When the sorting is done, Louis opens darktable on the folder from here. He likes that the app leads to darktable: open source, the reference.

### Sizes

- Shows the size of all folders beneath the place in the address bar, as a list: name, a bar whose length is the size, the size. Louis approved the simplicity of this list as drawn on 2026-11-09 (artboard R2).
- No treemap and no ring chart: Louis does not read the charts of WinDirStat and Filelight well. FolderSize is the reference he likes.
- A list of the heaviest files beneath the current place: name and size. The path is not repeated when it only repeats the name; a click on an item selects it in the main list, which shows where it is.
- Louis can run the analysis again.

### Map (name not chosen)

- Shows what can be used in the folder: scripts, context files, skills, MCP servers, the standard files of a repository (README, CONTRIBUTING and the like), and the links between them. It is not only about an LLM.
- The inventory is built by a script, without a model. A small local model only writes a few descriptions and picks among fixed options.
- In a folder with nothing of this kind, it says so.
- The inventory is a details list like the file tool's: a header row, then one row per item, with columns. Columns Louis named: the kind of item, the number of lines, the number of tokens. A value at the end of a row sits at the end of the row, across the full width.
- The kinds of items (context files, skills, MCP servers, scripts) are told apart at a glance. Where they are shown as sections, a section title has more room above it and a stronger weight or size than a row.
- "Always read by an LLM" is a box Louis ticks to filter the list. Other filters may sit next to it.
- Selecting an item shows, in the panel, its details, a graph of its links, and the list of what it links to and what links to it. The lists say in words what the graph shows.
- The graph in the panel is a square as wide as the panel. Beyond a few links it shows "+N".
- The graph is not a tool and not a tab: next to the title of the panel's graph, one button enlarges it. The large graph takes the centre; a button at its top right leaves it. In it, a click on an item shows that item in the panel.
- The large graph exists in 2D. The choice between 2D and 3D sits in the large graph, with its filters. In 3D, the three settings that choose what the X, Y and Z axes show sit on one line.
- The graph serves to picture where things stand and to wander; Louis does not work in it.

### Commands for agents

Said by Louis on 2026-11-10, in a dictation whose transcript wrote "cales"; the session reads "CLI". Not drawn.

- What the app can do is also offered as commands, each with named inputs, that a program or a model can call: from a command line, and through an MCP server that Louis turns on.
- The commands he named: take a screenshot, describe photos, rename, sort.
- A job that would need a screen full of text fields and things to drag, such as a rich renaming, is done through these commands instead: Louis asks an agent, the agent calls the command. The app does not grow a form for it.
- The most is still done without a model: a command does the work by script wherever a script can.

### Search
- A search button sits at the right of the top row, where people expect search.
- Search goes through PowerToys Command Palette, through a Deckle extension: Louis prefers to plug into what people already use, and it makes them discover it.
- Searches everything that was added to the app: files, photos, code, skills, MCP servers.

### Said on 2026-11-08, not detailed yet

Rendered Markdown and light text editing, repository overview (lines, files, binaries), simple Git (history curve, branches, worktrees), MCP and skills management, command menu.

## Behaviours

- A click selects an item and opens the details panel; it does not open the item.
- No photo and no photo metadata leaves the machine: descriptions and face recognition run locally.
- The gallery view is not reserved to photos: it also shows PDFs and Markdown files large.
- Marking a photo changes nothing on the disk. The marks are applied in one step, after a summary. (Approved in principle on 2026-11-10; the details come later.)
- What can be found without a model is found by a script: the scan of an added folder sorts its content by itself. A model is used as little as possible, and never to sort or show what a script can sort or show.
- Several files can be renamed in one step. (Louis finds PowerToys PowerRename painful for this.)

## Limits

- Developing and retouching a photo is darktable's work, not this app's.
- Resizing or splitting disks is Windows' work.
- File Explorer's tabs and the command band under its address bar are not wanted.
- Saved combinations of place, view, columns and panel ("presets") are not wanted: Louis is "not a fan".
- A side bar that holds the tools is not wanted: one bar would carry two different things, the tools and the folders.
- A floating bar of tools at the bottom is not kept: it passes in front of the content, and this app is not a whiteboard. (What Louis would ask of one is under Ideas.)
- No switch between a list and a graph inside the map: it would make him go back and forth.
- Later, not in a first version: a view of photos on a map; the graph in 3D. The 3D graph stays in the spec as drawn (board 12.10) so that it is not designed twice.
- Known trap: once darktable has imported a photo, its own database wins over the notes written next to the file. Sorting and noting therefore come before the hand-over. (Read in the darktable manual on 2026-11-09.)

## As drawn

The canvas page "Référence" holds the app as drawn in pass 12. On 2026-11-10 Louis said the drawing suits him, that he finds it hard to project himself on a mock-up, and that the rest is judged by using the app. The sentences below read the boards' markup; they are the starting point for a builder, not something Louis said one by one. Board codes are in brackets.

**Window.** With no folder added, every button of the top row is disabled except the one that adds a folder, and the address bar says "Aucun dossier" [12.11]. The folders pane, opened, shows each added folder's name and its path, then "Ajouter un dossier…" [12.2]. A folder just added appears last in the pane and opens in the file tool with nothing selected and no panel [12.20]. A chevron of the address bar opens the list of folders at that level, the current one marked [12.12a]. A click on the empty part of the bar shows the path as text, with suggestions that complete what is typed [12.12b]. The icon picker opens under the folder's icon in the address bar and has no confirm button: a choice applies at once [12.3, 12.3b].

**File list.** Three displays of one folder, chosen at the bottom right: list [12.18], large rows with type, size and date under the name [12.16], gallery with a preview of each file [12.17]. The pointer over a row shows two actions, "Ouvrir dans l'Explorateur" and "Mettre à la Corbeille" [12.1]. The panel of a file shows size, date modified and location [12.1]; the panel of a photo shows the same as in the photo tool, without the folder's counters [12.15].

**Photos.** Above the gallery: three counters (without a mark, to keep, to delete) and three commands, "Décrire le dossier", "Appliquer les marques…", "Ouvrir dans darktable" [12.4]. The panel of a photo: its mark with two buttons and their keys (G to keep, X to delete), what the camera recorded, the people found, the description [12.4]. With no description yet, the panel says so and offers "Décrire cette photo" [12.4]; while one is being written it says "Description en cours…" with a way to stop [12.24]; once written it says a model on this computer wrote it and offers "Décrire à nouveau" [12.5]. While the whole folder is described, "Décrire le dossier" gives way to "126 sur 412 décrites", a progress bar and a way to stop; the other commands stay usable [12.19]. A photo seen large keeps the panel, shows its rank ("13 sur 412"), and moves to the neighbours with the arrow keys [12.5]. "Appliquer les marques…" opens a summary: how many photos go to the Windows Recycle Bin and their weight, that they can be recovered until the bin is emptied, and that kept and unmarked photos do not change; its buttons are "Mettre 37 photos à la Corbeille" and "Annuler" [12.6]. After applying, the deleted photos have left the gallery, the counter of photos to delete is 0, "Appliquer les marques…" is disabled and "Ouvrir dans darktable" becomes the main command [12.22].

**Sizes.** One line says what the bars measure ("Longueur des barres = part des 14,6 Go du dossier"). The first row is the files of the folder itself, then one row per sub-folder [12.7]. The panel is about the folder Louis is in: its name, its total, and "Les plus lourds en dessous", six files with the sub-folder they are in [12.7]. A click on one of them selects it in the main list [12.21]. While the analysis runs, the line says how much is measured so far, a progress bar counts the folders measured, a button stops it, and a folder not measured yet shows "…" in place of its size [12.23]. The pointer over Refresh says "Analyser à nouveau (F5)" and when the last analysis ran [12.21].

**Map.** Columns: name, links, lines, tokens, date modified [12.8a]. By sections, each kind has a title with its count [12.8a]; flat, a column gives the kind and one box per kind filters the list [12.8b]. With "Toujours lu par un LLM" ticked, only those rows stay and a last row gives their total of tokens [12.8c]. The panel of an item: what it is and where, one line on what it does, the small graph with "+N" beyond four links, then "Cite (n)" and "Est cité par (n)", a link in both directions being marked as such [12.8a]. The large graph has the same filters, a 2D/3D choice whose 3D is disabled and says "plus tard", and "Quitter le grand graphe"; the chosen item and its neighbours stand out [12.9]. In a folder with nothing of the kind: "Rien à montrer dans Factures", then the list of what was looked for [12.14].

**Search.** When Command Palette is not installed, the search button opens a small panel that says so and offers "Installer PowerToys…" and "Plus tard" [12.13].
## Open

On 2026-11-10 Louis reviewed pass 12 without answering the questions one by one: "pour moi ça va", the rest "à l'utilisation". Until he says otherwise, a builder takes the option pass 12 drew, written "Drawn:" on each question below, and Louis settles it by using the app. This reading is the session's interpretation of his words.

Settled since the last revision, kept for the record. Where Louis changes tool: the top row (2026-11-10, after seeing three drawn variants). Whether the large graph is 3D from the start: no, later.

1. **How does Deckle Atelier fit into Deckle?** Louis: "it will be the first application, maybe"; this has to be decided before anything is built. Depends on it: where the repository lives, what is shared with Deckle (design system, settings, the animation rule), how it is installed.
2. **What does the photo tool look at?** Louis said first "a special photo view inside a folder", then wondered about a global Photos with its own folders. He did not come back to it on the second pass, which drew A. Drawn: A.
   - A. The folder he is in, like every other tool.
   - B. Its own list of photo folders, apart from the other added folders.
   - C. B, plus a switch to include the photos found in the other added folders. (He said "I don't think so".)
3. **What happens to a photo that is not kept?** Louis went back and forth on 2026-11-10.
   - A. Two marks only, keep and delete: "it would do me good not to keep everything". His lean for a first version; the second pass drew it and he did not object.
   - B. Three marks: the "just in case" photos stay as a strongly compressed JPEG in an archive, the raw is deleted.
   - C. B in two steps: the raw first moves to the archive as it is; when the job is over, the archive is compressed and the raws are deleted. The raw stays available while a client can still ask.
   - Drawn: A. Depends on it: question 4, and the summary of the apply step. No photographer was found who does B (search of 2026-11-09).
4. **Where does the archive live, and how are its folders named?** Only if 3 is B or C. By event, or by year and month.
5. **What follows the date in a file name?** The original name kept, or a new one.
6. **What may a Claude session read of the photos?** Not answered. Until it is: nothing, neither images nor metadata.
7. **How does a face get its name?** Who recognises, and where Louis names a person.
8. **Does the app reach every disk, or only added folders?** Louis: adding folders "surely suffices for now", and wonders whether something more would be good.
9. **What does search do when Command Palette is not installed?**
   - A. The search button carries a small "to install" badge and leads to PowerToys. (Drawn in 2.C10; Louis commented its yellow, not the principle.)
   - B. A small search of its own inside the app.
   - Drawn: A, as a small panel under the search button (12.13).
   - Louis also considered rebuilding the palette from its open source, and found the upkeep heavy.
10. **What is the map tool called?** "Ce qu'un LLM lit ici" is not a good name (Louis). The drawings say "Carte" and Louis called it "la carte" while commenting them; not confirmed as its name. Other candidates of the session: Liens, Contexte. Drawn: "Carte".
11. **How are the kinds of items told apart in the map's list?** Louis: "we still have to find the list a bit".
   - A. Sections: one titled group per kind, each with its rows.
   - B. One flat list with a column for the kind, and filters per kind.
   - Drawn: both (12.8a and 12.8b). Louis on 2026-11-10: "avec l'option là, c'est top", which reads as liking the filters; not said as a choice.
12. **What does the map look at?** Louis hesitated: the context only (context files, skills, MCP servers, scripts), or also the code of the repository (languages, libraries, at which level), or everything at once. He does not want to go as far as per-module views. In Deckle's own repository the question was looked at in several ways.
   - A. Everything at once.
   - B. One choice at the top of the map: context, or code.
   - Drawn: the context only, plus the standard files of a repository.
13. **Which colours can an added folder take?**
   - A. Five small tiles of fixed colours. Costs a palette to keep; Deckle's design system has only signal colours (success, caution, critical) and a grey accent.
   - B. One coloured tile that takes Windows' accent colour, next to the neutral one. Nothing to keep; only two looks.
   - Drawn: five tiles, Windows' accent first (12.3). Louis approved the picker as a whole on 2026-11-10 without naming the colours.
14. **What does an added folder show of itself?** Louis asked of the drawn "Archives": "what is it, where is this folder?". Session's proposal: opened, the pane shows under each name where the folder is on the disk. Drawn so (12.2).
15. **"Open in darktable", or "Open in…"?** Louis: it could take the app that opens images by default, and be set. "To be seen." Drawn: "Ouvrir dans darktable".
16. **Where do videos go?** Inside the photo tool, in a tool of their own next to it, or nowhere. Louis does not shoot video and asked what other tools do.
17. **How is an analysis run again?** Session's proposal: the Refresh button of the top row re-runs the current tool on the current folder, so no tool needs its own button. Drawn so, with F5 (12.21); Louis did not comment.
18. **What do the two row actions do, per kind of item?** In the drafts: "open in File Explorer" and "move to the Recycle Bin". Louis is not sure the first is needed. Drawn: those two.
19. **What comes first in a first version?** Louis said he is ready to code the sorting and improve it by using it. On 2026-11-10 he recalled where the app comes from: sorting his photos and his old files, renaming them well, automating part of it; "not urgent". The session reads this as: photos and renaming first; not confirmed.
20. **The name of the app.** Louis wrote "Deckle Atelier" in the title bar on 2026-11-10; not confirmed as final. Later that day: "Deckle Dossiers" is rejected ("nul"); no name is chosen and another has to be found. "Deckle Atelier" stays the working name.

21. **Do the active tool, the main command and a pressed button share one fill?** The design system's accent is a grey, so "Appliquer les marques…", the active tool and "À garder" look alike. Drawn: yes, on almost every board.
22. **Does a click on a counter of the photo tool filter the gallery?** (160 to keep, 37 to delete, 215 without a mark.) Drawn: plain text.
23. **Does a line of "Cite" or "Est cité par" lead to that item?** Drawn: plain text.
24. **Is the sizes panel about what is selected, or always about the folder?** Drawn: the folder, even with a heavy file selected (12.21).
25. **Does a row of the map show that it is always read, before the box is ticked?** Drawn: no.
26. **After applying the marks, does the app say where the photos went?** Drawn: nothing says it (12.22).
27. **While one photo is being described, does "Décrire le dossier" stay usable?** Drawn: yes (12.24).
28. **What does the renaming in one step offer on screen, if anything?** Louis wants the renaming (2026-11-10) and does not want a form full of fields for it: the rich cases go through the commands for agents. Open: whether a simple case, such as the date in front of the name, has a button in the app. No board draws it. Depends on it: question 5.
29. **How is a photo marked by voice?** Through Deckle's dictation, or otherwise. No board.
30. **Does the file tool need its three displays?** Louis on 2026-11-10: he "did not think of it" for files, finds it "pas mal", and says photos are "toujours comme en mode photo". Drawn: the three (12.18, 12.16, 12.17).
31. **Where do the file-type icons come from?** Louis: see what Microsoft uses in File Explorer and whether they can be taken. Not looked up yet.
32. **What may a command for agents hand to the model that calls it?** Louis answered what the commands are (see "Commands for agents"). Left open by that answer: a model that is not on this machine could call "describe photos" or read a file list, while the spec says no photo and no photo metadata leaves the machine. Until Louis answers, as for question 6: a command acts on photos and returns that it is done; it returns neither an image nor a photo's metadata. Also open: whether "cales" in the dictation meant CLI.
33. **Which states that no board draws need a drawing?** A builder follows Windows' conventions for them unless Louis says otherwise: an empty folder; a folder that no longer exists or cannot be read; a name or a path too long for its place; several items selected; a description or an analysis that failed, or was stopped; a folder with no photo in the photo tool; a photo that cannot be read; darktable missing; nothing to delete when applying; a map item with no link; a filter of the map that leaves no row; the search when Command Palette is installed.
## Ideas heard, not in the spec

- Group the photos of a burst and the near-duplicates, so that one decision covers them. (He keeps the three or four frames of a posed crowd to combine faces later.)
- A local model proposes the icon of an added folder from what it holds.
- A tool to manage disk space between volumes.
- A floating bar of tools, if it ever comes back: centred in the window whatever panels are open, always reachable, and the list scrolls far enough that its last row clears the bar.
- In the map's list, per item: the number of tokens and of lines, to see what weighs in a context.
