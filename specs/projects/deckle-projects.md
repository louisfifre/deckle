# Find any of your projects and reach its folders, its version and its scripts from one place

> Not reviewed.

## Need

### Context of use

1. The [user](../GLOSSARY.md#user) works alone at their computer on several [projects](GLOSSARY.md#project) that they build and publish. They run the [scripts](GLOSSARY.md#script) of each project from its [script menu](GLOSSARY.md#script-menu), in a [terminal](GLOSSARY.md#terminal) they keep open for it. They want to go on running their scripts themselves, without an [agent](GLOSSARY.md#agent).

### User need

1. The [user](../GLOSSARY.md#user) needs to find a [project](GLOSSARY.md#project) fast and reach, from one [window](../GLOSSARY.md#window), its folder, its [version](GLOSSARY.md#version) and the [scripts](GLOSSARY.md#script) they run often, in order to build and publish all their projects.

### Intended outcome

1. They group their [projects](GLOSSARY.md#project) into [categories](GLOSSARY.md#category) of their own, sort them, and find one fast.
2. They see a project's version, and whether it holds [uncommitted changes](GLOSSARY.md#uncommitted-change), without digging.
3. They reach a project's folder, and the other tools that serve it, in one [gesture](../GLOSSARY.md#gesture).
4. They run a project's [main script](GLOSSARY.md#main-script), and any other of its [scripts](GLOSSARY.md#script), without an [agent](GLOSSARY.md#agent) and without a terminal kept open for it.
5. No script runs unless they start it.
6. They see when the [context](GLOSSARY.md#context) of a project grows too big.
7. Adding a project is simple, and removing one leaves its folder exactly as it was.
8. Few things show, and there is nothing to manage.

## Journey

| The user wants to | The product must |
| --- | --- |
| add a [project](GLOSSARY.md#project) when it starts | take one folder, and ask nothing about it |
| sort their projects | let them create [categories](GLOSSARY.md#category) as they go, and move projects between them |
| find a project and open it | show each project's name and [version](GLOSSARY.md#version), by category, and show its [page](../GLOSSARY.md#page) on one [gesture](../GLOSSARY.md#gesture) |
| run the [main script](GLOSSARY.md#main-script) without opening the project | run it from where the project shows among the others |
| go and work on it | open it where the [product](../GLOSSARY.md#product) shows folders, in the file browser of the [system](../GLOSSARY.md#system), in a [terminal](GLOSSARY.md#terminal), or on the site that keeps it online |
| run any other [script](GLOSSARY.md#script) | open the project's [script menu](GLOSSARY.md#script-menu) as it is, so that nothing needs updating when its scripts change |
| know where a run stands | say whether it runs, succeeded or failed, and bring its terminal to the front |
| stop following a project | remove it, and leave its folder exactly as it was |

## Rules

1. [What the product shows is its real state.](../deckle.rules.md)
2. [Everything the product tells the user reaches them with animations off as well.](../deckle.rules.md)
3. [Everything the product tells the user reaches them with colours off as well.](../deckle.rules.md)
4. The [product](../GLOSSARY.md#product) never starts a [script](GLOSSARY.md#script) by itself: every run starts with a [gesture](../GLOSSARY.md#gesture) of the [user](../GLOSSARY.md#user).
5. Nothing the user does to a [project](GLOSSARY.md#project) in the product changes its folder; only the scripts they run do.
6. Besides what the user chose, everything the product shows of a project is read from its folder each time, without an [agent](GLOSSARY.md#agent).
7. The product gives no meaning of its own to a script: building or publishing a project is whatever its scripts do.

## Workflow

### Opening the window

When it goes well:

1. The [window](../GLOSSARY.md#window) opens on all the [user](../GLOSSARY.md#user)'s [projects](GLOSSARY.md#project), by [category](GLOSSARY.md#category), each time.
2. Its [navigation](../GLOSSARY.md#navigation) lists all their projects, then each category with its projects, then the projects in no category.
3. The first time the window opens, the navigation is [closed](../GLOSSARY.md#closed-navigation); after that, it is as they left it.
4. From the [page](../GLOSSARY.md#page) of a category or of a project, one [gesture](../GLOSSARY.md#gesture) takes them back to where they came from.

Otherwise:

5. No project is added yet: the window shows no navigation, and the only thing to do is to add one.

### Adding a project

When it goes well:

1. The [user](../GLOSSARY.md#user) chooses one folder, and the [product](../GLOSSARY.md#product) asks nothing about it.
2. The [project](GLOSSARY.md#project) takes its folder's name and joins no [category](GLOSSARY.md#category), unless they added it from the [page](../GLOSSARY.md#page) of a category: then it joins that category.

Otherwise:

3. The folder is already a project: nothing is added, and the product shows that project's page.

### Sorting into categories

When it goes well:

1. The [user](../GLOSSARY.md#user) creates a [category](GLOSSARY.md#category) as they go and names it. Categories come in the order they created them.
2. They move a [project](GLOSSARY.md#project) into a category, into another, or out of any category, by dragging it or from the project itself, without opening it.
3. On the [page](../GLOSSARY.md#page) of a category, they put its projects in the order they want, and the category shows them in that order everywhere.
4. A category with no project stays, and says to put a project in it.
5. The projects in no category come after the categories, with how many they are, out of sight until the user opens them.
6. Until they create a category, their projects show alone, with nothing around them.
7. Deleting a category asks first, names it, and says that its projects go back to no category. Once they confirm, no project is removed. It asks for an empty category too.

### Finding a project

When it goes well:

1. Each [project](GLOSSARY.md#project) shows its name, its [version](GLOSSARY.md#version), and how many [uncommitted changes](GLOSSARY.md#uncommitted-change) it holds when it holds any.
2. Each [category](GLOSSARY.md#category) shows as many of its projects as its space holds, and how many more it has; its [page](../GLOSSARY.md#page) shows them all.
3. One [gesture](../GLOSSARY.md#gesture) on a project shows its page.

Otherwise:

4. A project has no version yet: it says so.
5. The versions declared in a project differ: it says how many it found, in words.
6. A project's folder is gone: the project stays where it was, and says that its folder was not found. It comes back when the folder does.

### Reading a project

When it goes well:

1. The [page](../GLOSSARY.md#page) of a [project](GLOSSARY.md#project) shows its name, its category and the path of its folder; then its [version](GLOSSARY.md#version), how many files hold [uncommitted changes](GLOSSARY.md#uncommitted-change), and how much its [context](GLOSSARY.md#context) holds; then the tools that open it; then its [pinned scripts](GLOSSARY.md#pinned-script).
2. The tools that open it come before its scripts.
3. What the page shows is read again when the [user](../GLOSSARY.md#user) opens it, comes back to the [window](../GLOSSARY.md#window) or asks for it, and when a [script](GLOSSARY.md#script) of the project ends.

Otherwise:

4. The versions declared in the project differ: the [product](../GLOSSARY.md#product) says how many it found, gives each one with where it is declared, and says that different versions can be normal when a project holds several parts.
5. The project has no version yet: the product says so, and still gives what its files declare.
6. The folder keeps no history: the product says so, and the project has no version yet.
7. The folder is gone: the page shows only the name, the path, and that the folder was not found. When the folder is back, asking to read the project again brings the page back.

### Opening it elsewhere

When it goes well:

1. From its [page](../GLOSSARY.md#page), one [gesture](../GLOSSARY.md#gesture) shows the [project](GLOSSARY.md#project) where the [product](../GLOSSARY.md#product) shows folders, with its files. A folder the product does not show there yet is added there first.
2. Other gestures open its folder in the file browser of the [system](../GLOSSARY.md#system), a [terminal](GLOSSARY.md#terminal) in its folder, and the site that keeps it online, when it has one.
3. From where the project shows among the others, without opening it, they open its folder in the file browser or in a terminal.

Otherwise:

4. The project's folder is gone: none of these can open it.

### Pinning scripts

When it goes well:

1. Among the [scripts](GLOSSARY.md#script) that the [script menu](GLOSSARY.md#script-menu) of a [project](GLOSSARY.md#project) gives, the [user](../GLOSSARY.md#user) chooses the ones they keep at hand, and one of them as the [main script](GLOSSARY.md#main-script). A script the menu offers in several ways is kept in one of them.
2. The [page](../GLOSSARY.md#page) of the project shows its main script once, at its head, and its other [pinned scripts](GLOSSARY.md#pinned-script) below. Everything else is reached by opening the whole script menu.
3. Each pinned script shows its name in the menu, its file, and where its last run stands.

Otherwise:

4. Nothing is pinned yet: the page offers to pin a script, and to open the script menu.
5. The project has no script menu: nothing can be run from here. The [product](../GLOSSARY.md#product) says so, and lets them copy the command that installs one. It installs nothing itself, and finds the menu when they come back to the [window](../GLOSSARY.md#window).
6. The script menu cannot be read: the product says so, with the last lines of the error.
7. A pinned script that the menu no longer gives keeps its place, cannot be started, and says that the menu no longer has it.

### Running a script

When it goes well:

1. One [gesture](../GLOSSARY.md#gesture) on a [pinned script](GLOSSARY.md#pinned-script), or on the [main script](GLOSSARY.md#main-script) from the [page](../GLOSSARY.md#page) of the [project](GLOSSARY.md#project) or from where it shows among the others, runs it in a [terminal](GLOSSARY.md#terminal), in the project's folder. One gesture opens the whole [script menu](GLOSSARY.md#script-menu) in a terminal too.
2. The [product](../GLOSSARY.md#product) asks nothing before a [script](GLOSSARY.md#script) runs: what the script asks, it asks in its terminal.
3. One script runs at a time in a project; the other projects stay free.
4. While a script runs, the product shows that it runs and for how long, and one gesture brings its terminal to the front.
5. While a script runs, the other scripts of the project say that they wait for it, by its name, and cannot be started. The script menu and the tools that open the project stay usable.
6. What the script writes stays in its terminal: the product shows none of it.
7. The [user](../GLOSSARY.md#user) stops a script in its terminal. Leaving the page, or closing the [window](../GLOSSARY.md#window), does not stop it.
8. When a script ends, the product says whether it succeeded or failed, how long ago, and with which exit code, until it runs again or the product closes. While its terminal is still open, one gesture brings it to the front.
9. Among the projects, a project whose main script runs, or failed last, shows it beside that script.
10. Every script runs in the terminal the user chose for the whole product.

Otherwise:

11. The chosen terminal is missing from the computer: the script runs in the terminal of the [system](../GLOSSARY.md#system), and the product says so.

### Renaming

When it goes well:

1. The [user](../GLOSSARY.md#user) renames a [project](GLOSSARY.md#project) or a [category](GLOSSARY.md#category) in place, on its [page](../GLOSSARY.md#page), or a project from where it shows among the others.
2. Confirming keeps the new name, cancelling brings back the earlier one, and an empty name brings back the folder's name.
3. The name lives in the [product](../GLOSSARY.md#product) only: the folder keeps its name, and its path does not change.

### Removing a project

When it goes well:

1. Removing a [project](GLOSSARY.md#project) asks nothing: it changes nothing on the computer, and adding the project again is one [gesture](../GLOSSARY.md#gesture).

Otherwise:

2. A [script](GLOSSARY.md#script) of the project runs: the project cannot be removed until it ends.

## How Deckle answers, a sample on two lines

### 1.3.2 They see a project's version

1. The [product](../GLOSSARY.md#product) takes as the [version](GLOSSARY.md#version) of a [project](GLOSSARY.md#project) the highest version marked in the project's history, and reads too the version that each part of the project declares in its own files.
2. A version written with a leading "v" or without one is the same version; when the versions it found differ, the product keeps each one with the file it came from.
3. It counts the files that hold [uncommitted changes](GLOSSARY.md#uncommitted-change).
4. It reads all of this again from the folder whenever the [user](../GLOSSARY.md#user) looks: nothing of it is kept from one time to the next.

### 1.3.4 Without a terminal kept open for it

1. On the [user](../GLOSSARY.md#user)'s [gesture](../GLOSSARY.md#gesture), the [product](../GLOSSARY.md#product) opens a [terminal](GLOSSARY.md#terminal) in the [project](GLOSSARY.md#project)'s folder and asks the [script menu](GLOSSARY.md#script-menu) to run the chosen [script](GLOSSARY.md#script) at once, so that the user goes through no menu.
2. It follows what it started, to know while the script runs and, at its end, whether it succeeded and with which exit code.
3. Once the script ends, the terminal waits for the user before it closes, so that they can read what the script wrote.
