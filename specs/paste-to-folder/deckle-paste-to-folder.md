# Save what you copied as a file in a folder, with one paste

> Not reviewed.

## Need

### Context of use

1. The [user](../GLOSSARY.md#user), at their computer, has just copied something: a passage of text, an image, or files and folders. They want it kept as a file in a folder of theirs, now, while they go on with what they were doing.

### User need

1. The [user](../GLOSSARY.md#user) needs to turn what they copied into a file in a folder they chose, with one [paste](GLOSSARY.md#paste), in order to keep it without opening an [application](../GLOSSARY.md#application), naming a file or going through a save step.

### Intended outcome

1. What they copied becomes a file in their folder with one [paste](GLOSSARY.md#paste).
2. They see at once whether it worked, and why when it did not.
3. They know where it goes before they paste.
4. Nothing they already had is overwritten.
5. The [window](../GLOSSARY.md#window) takes almost no room and stays out of the way of their work.
6. What they paste does not leave the machine.

## Journey

| The user wants to | The product must |
| --- | --- |
| keep something they copied | take a [paste](GLOSSARY.md#paste) on its [window](../GLOSSARY.md#window) and save what they copied into the [chosen folder](GLOSSARY.md#chosen-folder) at once |
| know where it goes | show the chosen folder before any paste, and let them choose another |
| know it worked | show what each paste saved as soon as it is saved, or say that it was not saved and why |
| go on working | stay small and above their other windows, and move wherever they drag it |

## Rules

1. [What the product shows is its real state.](../deckle.rules.md)
2. [Everything the product tells the user reaches them with animations off as well.](../deckle.rules.md)
3. [Everything the product tells the user reaches them with colours off as well.](../deckle.rules.md)
4. The [product](../GLOSSARY.md#product) only adds to the [chosen folder](GLOSSARY.md#chosen-folder): it never overwrites, renames or removes a file that is already there.
5. What the [user](../GLOSSARY.md#user) pasted is kept nowhere but in the file saved in their folder, and does not leave the machine.

## Workflow

### Opening the window

When it goes well:

1. The [user](../GLOSSARY.md#user) opens the [window](../GLOSSARY.md#window) from the [product](../GLOSSARY.md#product), wherever they are working. There is only one: opening it again brings it to the front.
2. Before any [paste](GLOSSARY.md#paste), the window shows the [chosen folder](GLOSSARY.md#chosen-folder), the one they chose last time, after the product restarts too.

Otherwise:

3. No folder is chosen yet: the window says so, and choosing one is the next step.

### Choosing the folder

When it goes well:

1. The [user](../GLOSSARY.md#user) chooses a folder of their computer, the way the [system](../GLOSSARY.md#system) lets them choose one.
2. The [chosen folder](GLOSSARY.md#chosen-folder) is kept at once, for the next time too, after the [product](../GLOSSARY.md#product) restarts.

Otherwise:

3. They give up the choice: the folder stays as it was.
4. The choice cannot be made: the product tells them the folder did not change, and why.
5. The choice cannot be kept for the next time: the product tells them, and uses the folder until it quits.

### Pasting

When it goes well:

1. A click anywhere in the [window](../GLOSSARY.md#window) makes the next [paste](GLOSSARY.md#paste) go to it.
2. A paste saves what they copied into the [chosen folder](GLOSSARY.md#chosen-folder) at once: there is no step between the paste and the file.
3. Files and folders they copied are copied into the folder, each under its own name; a folder is copied whole.
4. Otherwise, an image they copied is saved as an image file, and otherwise a text as a text file. Each is named for the moment of the paste.
5. A name already taken in the folder gets the next free number, the way the [system](../GLOSSARY.md#system) names a copy, and the file already there is left as it is.
6. While a save runs, the [product](../GLOSSARY.md#product) shows that it is saving.

Otherwise:

7. No folder is chosen yet: nothing is saved, and the product says to choose a folder first.
8. The chosen folder no longer exists: nothing is saved, and the product says so.
9. What they copied holds nothing that can be saved, or cannot be read: nothing is saved, and the product says so.
10. A file cannot be written: nothing of it is left in the folder, and the product says it was not saved and why.
11. A copy of several files stops part way, because a copied file no longer exists or cannot be written: the files copied before it stay in the folder, the product shows them, and it says why it stopped.

### Seeing what was saved

When it goes well:

1. Each [paste](GLOSSARY.md#paste) shows what it saved, newest first: each file by its name, with the image itself for an image, the beginning of the text for a text, and the kind of each file or folder they copied.
2. The [window](../GLOSSARY.md#window) shows what was pasted since it opened, and nothing older.
3. A name too long for the window can still be read whole.
4. What the window shows is read, not used: nothing in it opens, selects or changes a file.
5. With a screen reader, each paste is told to the [user](../GLOSSARY.md#user) as it ends: what was saved and how many, or that it was not saved and why.

### Keeping out of the way

When it goes well:

1. The [window](../GLOSSARY.md#window) is small, keeps its size, and stays above the other windows.
2. It is not listed among the open [applications](../GLOSSARY.md#application) of the [system](../GLOSSARY.md#system).
3. The [user](../GLOSSARY.md#user) moves it by pressing anywhere in it and dragging, except on what closes it or chooses the folder.

### Closing

When it goes well:

1. One [gesture](../GLOSSARY.md#gesture) closes the [window](../GLOSSARY.md#window); the [chosen folder](GLOSSARY.md#chosen-folder) stays chosen for the next time.
2. Closing forgets what the window showed: it opens again on nothing pasted.

## How Deckle answers, a sample on two lines

### 1.3.1 What they copied becomes a file in their folder

1. On a [paste](GLOSSARY.md#paste), the [product](../GLOSSARY.md#product) looks in what the [user](../GLOSSARY.md#user) copied for files first, then for an image, then for a text, and saves the first it finds.
2. It writes straight into the [chosen folder](GLOSSARY.md#chosen-folder): an image or a text under a name made from the moment of the paste, files and folders under their own names.
3. It shows what it saved only once the file is written whole; a write that fails part way removes the incomplete file.

### 1.3.4 Nothing they already had is overwritten

1. Before it writes, the [product](../GLOSSARY.md#product) looks whether the name is taken in the [chosen folder](GLOSSARY.md#chosen-folder), and takes the next free number when it is.
2. It creates the file only if no file of that name exists at the moment it writes, so a file that appeared since the check is not overwritten either: it takes the next number.
3. A folder is first copied apart, hidden, then given its name in one step, so it never merges into a folder that is already there.
