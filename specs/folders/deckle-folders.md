# Know where you stand in your folders, and sort, clean and picture them in one familiar place

> Not reviewed.

## Need

### Context of use

1. The [user](../GLOSSARY.md#user) works alone on their own computer. They come to their files when they need to know where they stand: what a folder holds, what it weighs, what a photo shows, what can be used or run there. Today that takes the file browser of the [system](../GLOSSARY.md#system), and one [application](../GLOSSARY.md#application) per job. They know the look and the habits of that file browser well. They retouch their photos in an application of their own, and small [agents](../GLOSSARY.md#agent) of theirs run on their computer.

### User need

1. The [user](../GLOSSARY.md#user) needs to see where they stand in their folders, and to do the special jobs on them, such as sorting photos, freeing space and picturing what a folder of their work holds, in one place that feels like the file browser they know, in order to stay calm in their files with few things to manage.

### Intended outcome

1. One familiar place says what a folder holds, what it weighs, what a photo holds, and what can be used or run there.
2. Only what they felt a need for shows; nothing shows just because it could be shown.
3. They sort the photos of a shoot by hand or by voice, and nothing changes on the disk until they have seen what will happen and confirmed it.
4. Their photos reach their retouching application already sorted and noted.
5. They find a photo later from what they remember of it, such as the name of a person in it, across all their folders.
6. They find what weighs the most, in one folder or on a whole computer.
7. They picture what a folder of their work holds for its tools and its [agents](../GLOSSARY.md#agent), and see where and why its [context](../GLOSSARY.md#context) starts to overflow.
8. No photo, and nothing about a photo, leaves the machine.
9. What can be found without a [model](GLOSSARY.md#model) is found without one, and nothing depends on a large model.
10. Their agents can do the same jobs, so that no job needs a screen full of fields to fill in.

## Journey

| The user wants to | The product must |
| --- | --- |
| bring a folder in | take it as it is, ask nothing, and find by itself the photos, the documents, the code and the [scripts](../GLOSSARY.md#script) it holds |
| move among their folders and their jobs | keep the same folder whatever the job, and behave like the file browser they know |
| sort the photos of a shoot | show them large with what the camera recorded and a [description](GLOSSARY.md#description), take one [mark](GLOSSARY.md#mark) per photo, and apply all the marks in one step that first shows what will happen |
| hand the photos over for retouching | open their retouching [application](../GLOSSARY.md#application) on that folder, once the sorting and the notes are done |
| find a photo later | search everything they added from what they remember |
| free some space | show what weighs the most beneath a folder, and where it is |
| picture what a folder of their work holds | list what can be used or run there, its [links](GLOSSARY.md#link), and what it weighs |
| have an [agent](../GLOSSARY.md#agent) do a job | offer that job to the agent as a command with named inputs, done without a [model](GLOSSARY.md#model) wherever it can be |

## Rules

1. [What the product shows is its real state.](../deckle.rules.md)
2. [Everything the product tells the user reaches them with animations off as well.](../deckle.rules.md)
3. [Everything the product tells the user reaches them with colours off as well.](../deckle.rules.md)
4. No photo, and nothing about a photo, leaves the machine: what describes a photo or recognises a face in it runs on the machine.
5. What can be found without a [model](GLOSSARY.md#model) is found without one: a model is used as little as possible, and never to sort or to show what can be sorted or shown without it.
6. What the [product](../GLOSSARY.md#product) shows is made for the [user](../GLOSSARY.md#user) who reads it, not for a model: a small model on the machine helps in a few places, and nothing depends on a large one.
7. The product sorts and notes photos; it never retouches them.

## Workflow

### Adding a folder

When it goes well:

1. The [user](../GLOSSARY.md#user) adds a folder, and the [product](../GLOSSARY.md#product) asks nothing about what it holds: it finds by itself the photos, the documents, the code and the [scripts](../GLOSSARY.md#script) in it.
2. The product shows only the [added folders](GLOSSARY.md#added-folder): each one is a [place](../GLOSSARY.md#place) of the [window](../GLOSSARY.md#window), with its name and where it is on the disk.
3. A folder just added comes last among the others, and opens on its files, with nothing selected.
4. Each added folder has an icon and a colour, which the product picks when the folder is added and which the user changes at any time, so that folders shown by their icons alone can still be told apart. A choice applies at once.
5. From an added folder, the user opens it in the file browser of the [system](../GLOSSARY.md#system), changes its icon, or removes it from the product. Removing asks nothing: the folder stays on the disk.

Otherwise:

6. No folder is added yet: the only thing to do is to add one, and nothing fills itself from the disk.

### Moving among folders

When it goes well:

1. The [user](../GLOSSARY.md#user) moves among their folders as in the file browser they know: back, forward, up, and by the path of the folder they are in, one part of it at a time.
2. From any part of the path, they reach the folders beside it; they can also type a path, and the [product](../GLOSSARY.md#product) completes what they type.
3. Every job looks at the same folder: changing job keeps the folder and what is selected.
4. They choose how the content of a folder shows: a compact list, large rows, or large previews. Large previews are not only for photos: documents show large too.
5. Selecting a file or a folder shows what they need of it at a glance, and nothing else; it does not open it. What it shows depends on what is selected and on the job.
6. For a file that has [links](GLOSSARY.md#link), what it is and what it links to are shown apart.
7. One [gesture](../GLOSSARY.md#gesture) runs the current job again on the current folder, whatever the job.

### Looking at files

When it goes well:

1. The plain view of a folder lists its files with their name, the date they were last changed and their size.
2. Every file shows its type by its icon, whatever the type.
3. On a file, the [user](../GLOSSARY.md#user) shows it in the file browser of the [system](../GLOSSARY.md#system), or moves it to the bin of the system, from which it can be recovered.
4. A file selected shows its size, the date it was last changed and where it is. A photo selected shows what the sorting of photos shows of it.

### Sorting photos

When it goes well:

1. The [user](../GLOSSARY.md#user) sees the photos of the folder they are in, large, to see, sort and select them.
2. They see how many photos have no [mark](GLOSSARY.md#mark), how many are to keep and how many are to delete.
3. A photo selected shows its mark, what the camera recorded (the date it was taken, the camera, the lens, the exposure, and the place when the file holds one), the people recognised in it, and its [description](GLOSSARY.md#description).
4. They mark a photo with one [gesture](../GLOSSARY.md#gesture), to keep it or to delete it. Marking a photo changes nothing on the disk.
5. On one gesture, a [model](GLOSSARY.md#model) on the machine describes one photo, or every photo of the folder.
6. A description says that a model on this computer wrote it, and the user can ask for it again.
7. While a description is written, the [product](../GLOSSARY.md#product) says so, with how many photos are described out of how many when it describes the folder, and the user can stop it. Every other job stays usable meanwhile, the description of another photo included.
8. They see a photo large inside the product, with what it shows of it, its rank in the folder, and its neighbours one gesture away.
9. Before the marks are applied, the product says how many photos go to the bin of the [system](../GLOSSARY.md#system) and what they weigh, that they can be recovered until the bin is emptied, and that the kept and unmarked photos do not change. The user confirms or cancels.
10. Once the marks are applied, the deleted photos have left the folder, nothing is left to delete, and the next thing offered is to open the folder in their retouching [application](../GLOSSARY.md#application).
11. They rename several files in one step. A renamed photo has a name that starts with the date it was taken, year then month then day, so that the names sort by time.

### Finding a photo later

When it goes well:

1. The [user](../GLOSSARY.md#user) searches where they expect a search, through the search they already use on their computer.
2. The search goes through everything they added to the [product](../GLOSSARY.md#product): files, photos, code, skills, servers for [agents](../GLOSSARY.md#agent).

Otherwise:

3. The search the product goes through is missing from their computer: the product says so, and offers to install it now or later.

### Freeing space

When it goes well:

1. The [user](../GLOSSARY.md#user) sees the size of every folder beneath the folder they are in, as a list, each with its share of the total: first the files of the folder itself, then each folder beneath it.
2. Whatever is selected, they see the total of the folder they are in, and its heaviest files, each with the folder beneath it that holds it.
3. One [gesture](../GLOSSARY.md#gesture) on one of the heaviest files shows where it is.
4. They measure again with the gesture that runs any job again, and see when the last measure ran.
5. The same job serves to clean a whole computer.

Otherwise:

6. Measuring takes time: they see how much is measured so far and can stop it, a folder not measured yet says so in place of its size, and the other jobs stay usable.

### Picturing what a folder holds

When it goes well:

1. The [user](../GLOSSARY.md#user) sees what can be used or run in the folder: [scripts](../GLOSSARY.md#script), files of [context](../GLOSSARY.md#context), skills, servers for [agents](../GLOSSARY.md#agent), the standard files of a repository, and the [links](GLOSSARY.md#link) between them. It is not only about what a [model](GLOSSARY.md#model) reads.
2. What the folder holds is found without a model.
3. Each file listed shows its name, its links, its length, what it weighs for a model, and the date it was last changed. The kinds of files are told apart at a glance.
4. They keep in view only the files that an agent always reads, and see what these weigh in total.
5. A file selected shows what it is and where, what it does, a small picture of its links, then what it cites and what cites it, each with how many. The words say what the picture shows, and a link both ways is marked as such.
6. They enlarge the picture of the links, and come back from it. In it, a file chosen shows what it is, and it and its neighbours stand out.
7. The picture of the links serves to see where things stand and to wander; the user does not work in it.

Otherwise:

8. The folder holds nothing of this kind: the [product](../GLOSSARY.md#product) says so, and what it looked for.

### Through agents

When it goes well:

1. What the [product](../GLOSSARY.md#product) can do is also offered as commands with named inputs, which an [agent](../GLOSSARY.md#agent) or another program can call from a command line, or through a server for agents that the [user](../GLOSSARY.md#user) turns on.
2. The commands take a picture of the screen, describe photos, rename and sort.
3. A job that would need a screen full of fields to fill in and things to drag, such as a rich renaming, is done through these commands: the user asks an agent, and the agent calls the command. The product grows no form for it.
4. A command does its work without a [model](GLOSSARY.md#model) wherever it can.

## How Deckle answers, a sample on two lines

### 1.3.3 Nothing changes on the disk until they have seen what will happen

1. The [product](../GLOSSARY.md#product) keeps each [mark](GLOSSARY.md#mark) in its own store, never beside the photo, so that marking touches nothing on the disk.
2. When the [user](../GLOSSARY.md#user) asks to apply the marks, the product counts the photos marked to delete, adds up what they weigh, and shows it before anything moves.
3. Only once they confirm does it move those photos to the bin of the [system](../GLOSSARY.md#system), so that each one can still be recovered; the kept and unmarked photos are not touched.

### 1.3.6 They find what weighs the most

1. The [product](../GLOSSARY.md#product) walks every folder beneath the one the [user](../GLOSSARY.md#user) is in, and adds up the size of what each one holds.
2. It shows each folder as soon as it is measured, so that the user reads the first sizes while the rest is measured.
3. While it walks, it keeps the heaviest files it meets, each with the folder that holds it.
