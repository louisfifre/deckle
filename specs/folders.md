# Know where you stand in your folders, and sort, clean and picture them in one familiar place

## Need

Draft, not confirmed

**Context of use.** The user works alone on their own Windows machine. They come to their files when they need to know where they stand: what a folder holds, what it weighs, what a photo shows, what can be used or run there. Today that takes File Explorer plus one tool per job. They are used to the look and habits of Windows 11 and PowerToys, they retouch their photos in darktable, and they have small agents of their own running.

**User need.** The user needs to see where they stand in their folders, and to do the specialised jobs on them (sort photos, free space, picture a project), in one place that feels like the explorer they know, in order to stay calm in their files with few things to manage.

**Intended outcome.**

- One familiar place says what a folder holds, what it weighs, what a photo holds, and what can be used or run there.
- Only what they felt a need for shows; nothing appears just because it could be shown.
- They sort the photos of a shoot by hand or by voice, and nothing changes on the disk until they have seen what will happen and confirmed it.
- Their photos reach their retouching tool already sorted and noted.
- They find a photo later from what they remember of it, a person's name for example, across all their folders.
- They find what weighs the most, in one folder or on a whole PC.
- They picture what a project holds for its tools and agents, and see where and why it starts to overflow.
- No photo and nothing about a photo leaves the machine.
- What a script can find is found without a model, and nothing depends on a large model.
- Their agents can do the same jobs, so a job that would need a screen full of fields needs none.

## Journey

| The user wants to | The product must |
| --- | --- |
| bring a folder in | take it as it is, ask nothing, and find by itself the photos, documents, code and scripts it holds |
| move among their folders and jobs | keep the same folder whatever the job, and behave like the explorer they know |
| sort the photos of a shoot | show them large with what the camera recorded and a description, take a keep or delete mark per photo, and apply all marks in one step that first shows what will happen |
| hand the photos over for retouching | open darktable on that folder, once sorting and notes are done |
| find a photo later | search everything they added from what they remember |
| free some space | show what weighs the most beneath a place, and where it is |
| picture a project | list what can be used there, how the items link to each other, and what they weigh |
| have an agent do a job | offer that job as a command with named inputs, done by script wherever a script can |

When it goes another way:

- No folder is added yet: the only thing to do is add one, and nothing fills itself from the disk.
- A folder holds nothing a project uses: the map says so, and what it looked for.
- The search tool the product plugs into is not installed: the user is told so, and offered to install it or not now.
- An analysis or a description takes time: the user sees how far it is and can stop it, and the other jobs stay usable.

## Answer

Words a reader needs:

- An **added folder** is a folder the user brought into the app. The app shows only these.
- A **tool** is one way to look at the current folder: the file list, the photo tool, the sizes, the map.
- A **mark** is the user's decision on a photo: to keep or to delete.
- The **map** is the tool that lists what can be used in a folder and the links between those items. Its name is not chosen (Open 10).
- The **details panel** is the panel on the right that shows what the user needs of the selected item.
- A **command for agents** is a job of the app offered to a program or a model, with named inputs.

The screens are drawn on the canvas page "Référence" (pass 12), which the user accepted as the starting point; the drawings are in French, and the wording is in the module's strings. Commands are named below by their label in those strings.

### The window

1. The top of the window is one row, in this order: the button that opens the pane of added folders, the app icon, the app's name, Back, Forward, Up, Refresh, the address bar, the tools, the search button, the window buttons. There is no empty band above it.
2. The window buttons are Windows' own (minimise, maximise, close) and take the full height of the row. Every other button of the row looks the same; search is one of them, an icon.
3. The user changes tool in the top row, right after the address bar: one button per tool, and the active one shows its name.
4. There are no tabs.
5. The address bar behaves like File Explorer's, in every tool, the photo tool included. It shows the path as segments. A chevron between two segments opens the list of folders at that level, the current one marked. A click on a segment goes there. A click on the empty part of the bar shows the path as text, with suggestions that complete what is typed.
6. The content is in the centre. It can be shown three ways, chosen at the bottom right: a compact list, large rows, a gallery. The gallery is not reserved to photos: it also shows PDFs and Markdown files large.
7. Panels sit on the sides of the centre, the details panel on the right. A tool changes what the panels hold, not where they are. A new kind of content brings its own panel content; the window around it stays the same.
8. Several tools look at the same folder. Changing tool keeps the folder and the selection.
9. A click selects an item and opens the details panel; it does not open the item.
10. Refresh runs the current tool again on the current folder, with F5, so no tool needs a button of its own for it.

### Added folders

1. The app shows only the folders the user added. Adding a folder asks nothing about what it holds: the app finds by itself the photos, the documents, the code and the scripts in it.
2. With no folder added, every button of the top row is disabled except the one that adds a folder, and the address bar says there is no folder.
3. The added folders sit in a pane on the left, reduced to their icons the first time. The button at the left end of the top row opens and closes it, beside the page, as `navigation-pane/deckle-navigation-pane.md` says. Opened, it shows each added folder's name and where it is on the disk, then "Ajouter un dossier…".
4. A folder just added appears last in the pane and opens in the file list, with nothing selected and no panel.
5. Each added folder has an icon, which the app picks when the folder is added. The user changes it by a click on its icon in the address bar, once inside that folder, or with "Change icon…" in the folder's menu. The picker opens under the address bar's icon, or beside the pane's icon when it comes from the menu, and offers icons and a colour, so that folders reduced to icons can still be told apart. There are no shapes to choose. Without a background, the icon itself takes the colour; with the background option on, the background takes the colour and is always a square. The picker shows two labels, for the icon and the colour, and no title naming the folder. It has no confirm button: a choice applies at once.
6. The icon picker is one component shared across Deckle's apps: Deckle Mail uses the same one for a mailbox. Its set of icons is four rows of six. The first two rows say what a folder holds. The last two serve sorting work: development (instructions, AI, terminal, settings, bug, tests), then marks (important, flag, tag, ideas, private, cloud).
7. Each added folder in the pane has one menu: "Open in File Explorer", "Change icon…", then "Remove from Deckle Folders". In the opened pane, a "..." at the end of the item opens it, shown while the pointer or the keyboard is on the item; it replaces the cross. A right-click on the item opens the same menu, in the opened pane as in the pane reduced to icons. There is no "Open": a click on the item opens the folder. Removing asks nothing: the folder stays on the disk.

### The details panel

1. What the panel shows depends on the kind of item selected and on the tool. Example: a photo selected in the file list shows its data; in the photo tool, the folder is a gallery.
2. For an item that has links, the panel separates its details from its links.
3. The titles of the panel's sections stand out from what they hold.
4. The panel shows what the user needs at a glance and nothing else.

### Looking at files: the file list

1. The file list is the plain view of a folder: a details list with a header row, name, date modified, size.
2. In large rows, the type, size and date sit under the name; in the gallery, each file shows a preview.
3. Every file shows an icon for its type, whatever the type: a PDF and a Markdown file are told apart by their icon. The app can start without it.
4. The pointer over a row shows two actions at most: "Show in File Explorer" and "Move to Recycle Bin". The room they take is the same on every row, so that opening a sub-folder whose rows have other actions never shifts the columns.
5. The panel of a file shows its size, date modified and location. The panel of a photo shows what the photo tool shows, without the folder's counters.

### Sorting photos: the photo tool

1. The photo tool shows the photos of the current folder as a gallery. It serves to see, sort and select; never to retouch.
2. Before any folder is added: nothing is shown except the way to add a folder and three short lines on what happens next (add a folder, ask for descriptions if wanted, search finds them). The photo tool never fills itself from the disk.
3. Above the gallery: three counters (without a mark, to keep, to delete) and three commands, "Describe the folder", "Apply marks…" and "Open in darktable".
4. The panel of a photo shows its mark with two buttons and their keys, K to keep and X to delete; what the camera recorded (the date it was taken, the camera, the lens, the exposure, and the place when the file holds one); the people recognised; and the written description.
5. The user marks a photo with the panel's buttons or the keys. Marking a photo changes nothing on the disk.
6. A model running on this machine can describe one photo, or go through the whole folder and describe each photo. One click starts it, and the panel of a photo is where the user asks for its description.
7. With no description yet, the panel says so and offers "Describe this photo". While one is being written, it says so, with a way to stop. Once written, it says that a model on this computer wrote it, and offers "Describe again". While one photo is being described, "Describe the folder" stays usable.
8. While the whole folder is described, "Describe the folder" gives way to how many photos are described out of how many, a progress bar and a way to stop; the other commands stay usable.
9. A photo can be seen large inside the app. Seen large, it keeps the panel, shows its rank in the folder ("13 sur 412"), and moves to its neighbours with the arrow keys.
10. "Appliquer les marques…" opens a summary: how many photos go to the Windows Recycle Bin and their weight, that they can be recovered until the bin is emptied, and that kept and unmarked photos do not change. Its buttons put that number of photos in the Recycle Bin, or cancel.
11. After applying, the deleted photos have left the gallery, the counter of photos to delete is 0, "Appliquer les marques…" is disabled, and "Ouvrir dans darktable" becomes the main command.
12. "Ouvrir dans darktable" opens darktable on the folder.
13. Several files can be renamed in one step. A renamed photo has a name that starts with the date it was taken, year then month then day, so that names sort by time.

### Finding a photo later: search

1. A search button sits at the right of the top row, where people expect search.
2. Search goes through PowerToys Command Palette, through a Deckle extension, so that the user plugs into what people already use.
3. It searches everything that was added to the app: files, photos, code, skills, MCP servers.
4. When Command Palette is not installed, the search button opens a small panel that says so and offers "Installer PowerToys…" and "Plus tard".

### Freeing space: the sizes

1. The sizes tool shows the size of all folders beneath the place in the address bar, as a list: name, a bar whose length is the size, the size. One line says what the bars measure: their share of the folder's total. The first row is the files of the folder itself, then one row per sub-folder.
2. The panel is about the folder the user is in, even with a file selected: its name, its total, and the heaviest files beneath it, six of them, each with the sub-folder it is in. The path is not repeated when it only repeats the name.
3. A click on one of the heaviest files selects it in the main list, which shows where it is.
4. While the analysis runs, the line says how much is measured so far, a progress bar counts the folders measured, a button stops it, and a folder not measured yet shows "…" in place of its size.
5. The user runs the analysis again with Refresh; the pointer over Refresh says so, with its key F5, and when the last analysis ran.
6. The same tool serves to clean a whole PC.

### Picturing a project: the map

1. The map shows what can be used in the folder: scripts, context files, skills, MCP servers, the standard files of a repository (README, CONTRIBUTING and the like), and the links between them. It is not only about an LLM.
2. The inventory is built by a script, without a model. A small local model only writes a few descriptions and picks among fixed options.
3. The inventory is a details list like the file list's: a header row, then one row per item, with the columns name, links, lines, tokens and date modified. A value at the end of a row sits at the end of the row, across the full width.
4. The kinds of items are told apart at a glance. By sections, each kind has a title with its count, and a section title stands out from a row. Flat, a column gives the kind and one box per kind filters the list.
5. "Toujours lu par un LLM" is a box the user ticks to filter the list: only those rows stay, and a last row gives their total of tokens. Other filters may sit next to it.
6. Selecting an item shows in the panel what it is and where, one line on what it does, a small graph of its links, then the list of what it cites and of what cites it, each with its count. The lists say in words what the graph shows, and a link in both directions is marked as such. The small graph is a square as wide as the panel; beyond four links it shows "+N".
7. The graph is not a tool and not a tab: next to the title of the panel's graph, one button enlarges it. The large graph takes the centre, with the same filters, and a button at its top right leaves it. In it, a click on an item shows that item in the panel, and the chosen item and its neighbours stand out.
8. The large graph is in 2D. The choice between 2D and 3D sits in the large graph, with its filters; 3D is disabled and says it comes later. In 3D, the three settings that choose what the X, Y and Z axes show sit on one line.
9. The graph serves to picture where things stand and to wander; the user does not work in it.
10. In a folder with nothing of this kind, the map says there is nothing to show in that folder, then lists what it looked for.

### Through agents: commands

1. What the app can do is also offered as commands, each with named inputs, that a program or a model can call: from a command line, and through an MCP server that the user turns on.
2. The commands are: take a screenshot, describe photos, rename, sort.
3. A job that would need a screen full of text fields and things to drag, such as a rich renaming, is done through these commands instead: the user asks an agent, and the agent calls the command. The app does not grow a form for it.
4. A command does the work by script wherever a script can.

### Rules

- No photo and no photo metadata leaves the machine: descriptions and face recognition run locally.
- What can be found without a model is found by a script: the scan of an added folder sorts its content by itself. A model is used as little as possible, and never to sort or show what a script can sort or show.
- The screens are made for a person who uses them, not for an LLM: a small local model helps in a few places, and nothing depends on a large one.

## Limits

- Developing and retouching a photo is darktable's work, not this app's.
- Known trap: once darktable has imported a photo, its own database wins over the notes written next to the file. Sorting and noting therefore come before the hand-over.
- Resizing or splitting disks is Windows' work.
- Sizes show as a list only: no treemap and no ring chart, since the user does not read those charts well.
- Not wanted: File Explorer's tabs and the command band under its address bar; saved combinations of place, view, columns and panel ("presets"); a side bar that holds the tools, since one bar would carry two different things, the tools and the folders; a floating bar of tools at the bottom, since it passes in front of the content; a switch between a list and a graph inside the map, since it would make the user go back and forth.
- Not in a first version: a view of photos on a map; the graph in 3D. The 3D graph stays drawn (board 12.10) so that it is not designed twice.

## Open

Pass 12 drew one option for most questions below. Until the user says otherwise, a builder takes the option drawn, and the user settles it in use.

1. **Does the app stay a module of Deckle, or later become an app of its own?** Built today: a module inside the Deckle app. Depends on it: where the code lives, what is shared with Deckle, how it is installed.
2. **What does the photo tool look at?** Options: A, the folder the user is in, like every other tool; B, its own list of photo folders, apart from the other added folders; C, B plus a switch to include the photos found in the other added folders (the user doubts it). Drawn: A.
3. **What happens to a photo that is not kept?** Options: A, two marks only, keep and delete, the user's lean for a first version; B, three marks, the "just in case" photos stay as a strongly compressed JPEG in an archive and the raw is deleted; C, B in two steps, the raw first moves to the archive as it is, and when the job is over the archive is compressed and the raws deleted, so the raw stays available while a client can still ask. Drawn: A. Depends on it: Open 4, and the summary of the apply step.
4. **Where does the archive live, and how are its folders named?** Only if Open 3 is B or C. Options: by event, or by year and month.
5. **What follows the date in a renamed file's name?** Options: the original name, or a new one. Depends on it: Open 26.
6. **What may a Claude session read of the photos?** Until answered: nothing, neither images nor metadata.
7. **How does a face get its name?** Who recognises it, and where the user names a person.
8. **Does the app reach every disk, or only added folders?** The user finds that added folders surely suffice for now, and wonders whether more would be good. Built today: added folders only.
9. **What does search do when Command Palette is not installed?** Options: A, the search button leads to PowerToys; B, a small search of the app's own. Drawn: A, as a small panel under the search button. Rebuilding the palette from its open source was considered and found heavy to keep up.
10. **What is the map tool called?** "Ce qu'un LLM lit ici" is not a good name. Candidates: Carte, Liens, Contexte. Drawn: "Carte".
11. **How are the kinds of items told apart in the map's list?** Options: A, sections, one titled group per kind; B, one flat list with a column for the kind and filters per kind. Drawn: both. The user seemed to like the filters, without choosing.
12. **What does the map look at?** Options: A, the context and the code of the repository (languages, libraries) at once; B, one choice at the top of the map, context or code. Drawn: the context only, plus the standard files of a repository. Not wanted either way: views per module.
13. **Which colours can an added folder take?** Options: A, five tiles of fixed colours, which costs a palette to keep, since Deckle's design system has only signal colours and a grey accent; B, one tile that takes Windows' accent colour, next to the neutral one. Drawn: five tiles, Windows' accent first.
14. **"Ouvrir dans darktable", or the app that opens images by default, set by the user?** Drawn: "Ouvrir dans darktable".
15. **Where do videos go?** In the photo tool, in a tool of their own next to it, or nowhere. The user does not shoot video.
16. **What do the two row actions do, per kind of item?** The user is not sure "open in File Explorer" is needed. Drawn: open in File Explorer, move to the Recycle Bin.
17. **What comes first in a first version?** Read, not confirmed: photos and renaming.
18. **The name of the app.** "Deckle Dossiers" is rejected. Built today: the interface shows "Deckle Folders".
19. **Do the active tool, the main command and a pressed button share one fill?** The design system's accent is a grey, so they look alike. Drawn: yes.
20. **Does a click on a counter of the photo tool filter the gallery?** Drawn: no, plain text.
21. **Does a line of what an item cites, or of what cites it, lead to that item?** Drawn: no, plain text.
22. **Is the sizes panel about what is selected, or always about the folder?** Drawn: the folder, even with a heavy file selected.
23. **Does a row of the map show that it is always read, before the box is ticked?** Drawn: no.
24. **After applying the marks, does the app say where the photos went?** Drawn: nothing says it.
25. **While one photo is being described, does "Describe the folder" stay usable?** Drawn: yes.
26. **What does the renaming in one step offer on screen, if anything?** The rich cases go through the commands for agents. Open: whether a simple case, such as the date in front of the name, has a button in the app. Not drawn.
27. **How is a photo marked by voice?** Through Deckle's dictation, or otherwise. Not drawn.
28. **Does the file list need its three displays?** The user had not thought of it for files and finds it not bad. Drawn: the three.
29. **Where do the file-type icons come from?** Microsoft's, as File Explorer uses them, if they can be taken. Not looked up yet.
30. **What may a command for agents hand to the model that calls it?** A model that is not on this machine could call "describe photos" or read a file list, while no photo and no photo metadata may leave the machine. Until answered: a command acts on photos and returns that it is done; it returns neither an image nor a photo's metadata. Also open: whether the dictated word read as "CLI" meant a command line.
31. **Which states that no board draws need a drawing?** Until answered, a builder follows Windows' conventions for: an empty folder; a folder that no longer exists or cannot be read; a name or a path too long for its place; several items selected; a description or an analysis that failed, or was stopped; a folder with no photo in the photo tool; a photo that cannot be read; darktable missing; nothing to delete when applying; a map item with no link; a filter of the map that leaves no row; the search when Command Palette is installed.
