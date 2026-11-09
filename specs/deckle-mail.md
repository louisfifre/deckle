# Deckle Mail

Proposed on 2026-11-09, from what the maintainer said of the canvas « Tri des mails — scène d'ouverture » and from the review pass that followed (canvas pages 9 to 17). Nothing is built and no real mailbox is connected. The sentences below describe the direction the pass recommends; every choice the maintainer has not confirmed is listed under Open, with the options the canvas shows.

## Intention

Deckle Mail serves one person who owns several mailboxes and aliases and feels swamped because every mail arrives at the same level, at the same moment, whatever its importance. They open it to see what their mail asks of them, to sort it fast, and to trust that the rest has put itself away. It is a hub for sorting. It is not a place to read at length, and it never writes or sends a mail.

## Journeys

**Opening the window.** The mail is already sorted when the window opens. The person lands on Triage and sees, in one list, each thing a mail asks of them, whatever mailbox it came to. Under the list they see the few facts worth knowing. Everything else has been put away and does not show.

**Sorting one thing.** The person reads a tile and knows what is asked, by whom, where it arrived and by when, without opening anything. If the matter is settled or is not theirs, they choose File and the tile leaves. If they will deal with it later, they choose Add task, pick a day, and the thing becomes a task in Anytype that keeps its link to the mail. When they have done it, they tick the task, in Deckle Mail or in Anytype, and the mail is put away.

**Reading a mail.** The person clicks a row and its subject opens on a page of its own: the latest mail in full, as it was written, and beside it the list of every mail of the same subject. They go back with the breadcrumb, Alt+Left or the mouse back button, and find the list as they left it.

**Going mailbox by mailbox.** The person clicks a mailbox or an alias in the navigation and gets the same view as Triage, kept to what arrived there. The number beside each mailbox and alias tells them where something waits before they click.

**Checking what was put away.** The person opens Activity and sees everything that happened, newest first: what the sorting put away and why, and what they did themselves. They search by sender, subject or text, open a line to read the mail, and send it back to Triage when the sorting was wrong.

**Starting.** On a first launch the person adds a mailbox. Deckle Mail sorts its recent mail while they watch things appear, and from then on sorts each mail as it arrives.

## Surfaces

### Navigation

- The navigation lists Triage and Activity first, then each mailbox with its aliases as its children, then Settings at the foot.
- A mailbox or an alias that has no name shows its address. One that has a name shows the name.
- A mailbox wears a badge with the number of things waiting in it, its aliases included. An alias wears a plain number: the part of them that arrived through it. Example: three things wait in Perso, one of them through the alias shop; Perso shows 3 and shop shows 1.
- A mailbox or an alias where nothing waits shows no number.
- No number anywhere counts what was sorted, filed or done.

### Triage

- The page has no title and no heading above the list of things to do: the selected item of the navigation says where the person is.
- Before any mailbox exists, the page says that no mailbox is set and offers to add one.
- While the first sort of a mailbox runs, the page says which mailbox is being sorted, shows a progress bar without a count, and adds each thing at the end of the list as it is found. A thing already shown never moves during the sort.
- With things waiting, the page shows one tile per thing, every mailbox mixed, never grouped by mailbox. Every thing is on the page: none is folded away, however many there are.
- A tile shows the sentence of what is asked, then on one grey line the sender, where the mail arrived, and the deadline when the mail states one. A mailbox name on a tile is text, never a link.
- Under the tiles, a caption « Good to know » introduces the facts, one line each, with no control on them. The caption says that each line leaves by itself. Beyond three lines, one last line gives the number of the others and leads to them.
- With nothing waiting, the page shows a small flat picture in pale grey, the sentence « Nothing is waiting. » and a line saying that new mail is sorted as it arrives. The picture is not an icon, not a skeleton of rows, and nothing on the page moves: the person does not take it for a loading page. Good to know stays under it when there are facts.
- When one mailbox cannot connect, a bar at the top names it and says since when its content is shown. Its tiles stay and keep their controls: a File made meanwhile is kept and sent when the mailbox is back.
- When new mail waits to be sorted because the sorting cannot run, a bar at the top says how many mails arrived since when, and that they will be sorted. The tiles already there stay usable.

### A mailbox page, an alias page

- The page shows the name, a control to rename it, and the address under the name. An alias shows its mailbox before its own name, as a breadcrumb.
- A name being entered or changed is edited in place, at the size of the title, and the placeholder of an empty name fits inside the field.
- Under the header the page is Triage kept to that mailbox or alias: its tiles, then its Good to know lines.
- On a mailbox page a tile names the alias only when the mail arrived through one. On an alias page no tile repeats the alias.
- Under them, a caption « Sorted away » introduces, in small text and without controls, the latest mails the sorting put away there: for each, the mail's own subject, its sender, where it arrived and the one-word reason. A link leads to Activity kept to that mailbox.

### A subject opened

- The page opens from a tile, a Good to know line or an Activity line. Its breadcrumb names the page the person came from. Its title is the sentence of the row they clicked.
- Under the title, grey lines give the mail's own subject, the sender with their real address, the mailbox or alias with its address, and the date and time of reception.
- Then comes the text of the latest mail, whole and as written: greeting, request, signature. Deckle Mail restyles no part of it and rewrites nothing. The text column stays narrow enough to read, about sixty characters a line.
- After the text come the files really attached to that mail, each as a row with its name, kind and size, which opens the file. An image that belongs to the body of the mail, a logo or the icons of a signature, is never listed as a file.
- Beside the text, a list shows every mail of the subject, newest first, each with its date, its sender and its first words, and a mark when it carries a file. The mail shown is marked as selected. Clicking another shows that one in place of the first, and the list stays, so the person can always come back.
- A mail of the person's own, sent from elsewhere in the same thread, appears in that list under « You ».
- The page has no button: nothing files, plans or answers from here.
- A mail with no text shows a bar saying so instead of the text. A mail whose sender failed the checks shows a bar saying so above the text, and its grey line gives the address alone.
- A page that cannot show its mail because the mailbox is unreachable says so and keeps the header and the list.

### Activity

- The page is titled Activity and opens with a search field that looks in senders, subjects and the text of mails.
- Under it, lines are grouped by day, newest first. A line shows the time, the sentence or subject of the mail, then on a grey line the sender, where it arrived and what happened: sorted away with its reason, shown in Good to know, filed by the person, a task added.
- A line whose mail is not in Triage offers one command, To Triage. A line whose mail is in Triage says so instead.
- The text of a line opens the subject, so the person reads before deciding.
- Reached from a mailbox page, the page opens kept to that mailbox, and says so with a filter the person can remove.
- With nothing yet, the page says that what the sorting does will be listed here.

### Settings

- The page lists the mailboxes, each leading to its own page where it is named and removed, and offers to add one.
- It holds a switch for tasks in Anytype and a switch for filing on the mail server too.
- The page has no Reset all.

### A narrow window

- In a narrow window the navigation folds to its rail of icons and the mailbox badges stay visible on it.
- A tile goes on two levels: its text, then its controls.
- On an opened subject the list of its mails goes under the text.

## Behaviours

**Where a mail lands**

- Each new mail lands in exactly one of three places: a thing to do in Triage, a line in Good to know, or sorted away.
- A mail that asks something of the person is a thing to do.
- A mail that asks nothing but states a fact that changes something for the person is a Good to know line. Example: a refund received, a water cut on Thursday.
- A mail that asks nothing and changes nothing is sorted away with one reason among newsletter, promotion, receipt and nothing to do. Example: a newsletter, the progress of an order on its way, the notice of a delivery the person received in hand.
- When the sorting is not sure a mail can be put away, the mail waits in Triage. Example: a mail with a vague subject from a sender whose other mails matter.
- A mail the sorting could not read, because it holds only images or is encrypted, waits in Triage with its own subject as its sentence and the reason as a framed label on its grey line. It is never sorted away unread.
- A mail whose sender failed the checks waits in Triage with the framed label « Sender not verified », shows the address in place of the name the sender gave itself, and offers File only: it cannot become a task.
- A state the sorting sets on a row is a framed label, never plain text: the darker grey of a grey line means a deadline and nothing else.

**Subjects**

- Mails of one subject are one object everywhere: one tile, one line, one opened page.
- Two mails belong to one subject when they are in the same thread of replies, or when they carry the same order, parcel or file number. Example: the order confirmation from a shop and the pickup notice from its carrier are one subject.
- Two mails that share only their sender are two subjects. Example: two refund statements from the same insurer are two lines; the insurer's request for a document is a third subject.
- When a new mail joins a subject, the subject is sorted again from that mail: it can become a thing to do, a Good to know line or be sorted away. Example: a parcel on its way is sorted away; « at the pickup point until the 18th » makes it a thing to do.
- A tile carries the sentence that asks, even when a later mail of the thread only repeats the request.
- When the person answers in the thread from another mail app, the thing leaves Triage as if filed.

**What a tile says**

- The sentence of a tile is short enough to read at a glance and says what the person has to do. It is written once when the mail is sorted and does not change between two openings of the window.
- A deadline shows only when the text of the mail states one. The date a mail arrived never stands for a deadline and never shows in a list.
- A deadline is written in two forms only: « Due » with its day while it is ahead, « Overdue · » with its day once it is behind. When the mail gives a time slot, the deadline is the end of the slot, with its hour.
- Tiles are ordered so that what presses comes first: the things with a deadline, the nearest first, then the others, the oldest first.

**File**

- File puts the mail away, whatever the reason, and the thing leaves Triage.
- Right after File, the tile is replaced by a line that says where the mail went and offers Undo. The line stays until the person's next gesture. Ctrl+Z undoes too.
- A right-click on a tile, or its keyboard equivalent, offers the tile's own commands, then Open in webmail, Copy the sender's address and Not something to do. Not something to do files the mail and lets the person say why in one optional sentence.
- No command appears on hover alone.

**Tasks in Anytype**

- When Anytype is connected, a tile offers Add task with the choices Today, Tomorrow, Pick a day and No day yet.
- Add task creates in Anytype a task that holds the tile's sentence, the lines of the mail that say it, the sender, where it arrived, and a link that opens the subject in Deckle Mail.
- A tile that is a task shows the task's round tick and its day, or « no day yet », in place of Add task and File.
- A day set or changed in Anytype shows on the tile.
- Ticking the task, on the tile or in Anytype, completes the task and puts the mail away. The tile is replaced by a line that says so, with Undo, which reopens both.
- A task whose day has passed without a tick stays in Triage and says it is overdue. The mail and the task stay linked.
- A thing that is a task never leaves by itself. When a later mail says the matter is over, the tile shows that sentence and waits for the tick. Deckle Mail never completes a task on its own.
- A thing that is not a task leaves by itself when a later mail of its subject says the matter is over, and Activity says so.
- When the task is deleted in Anytype, the tile gets Add task and File back.
- When Anytype cannot be reached, a bar says so, tiles keep the last day they knew, and a tick made meanwhile is kept and sent when Anytype is back.
- When Anytype is not connected, nothing about tasks is drawn: File is the only command on a tile.

**Good to know**

- A Good to know line carries no command. Clicking it opens its subject.
- A line leaves by itself the day after the person first saw it, or earlier when a later mail of its subject replaces it.
- A newsletter never shows in Good to know.

**Corrections**

- To Triage on an Activity line brings the mail back to Triage as a thing to do, whatever it was before.
- To Triage and Not something to do tell the sorting it was wrong: the next mails of the same kind from the same sender are sorted with that correction, and with the person's sentence when they gave one.

**Outside the window**

- When filing on the server is on, a mail that is filed or sorted away leaves the inbox of its mailbox for the folder of its alias, so the webmail and the phone show the same order. Things still waiting stay in the inbox.
- When it is off, Deckle Mail changes nothing on the server.
- Opening the window, changing page or opening a subject shows what was sorted before, at once, without sorting anything again.
- No text of a mail leaves the machine with a value that identifies the person. Such values are replaced before the text goes to a remote model, and stay readable in the window.

**Look and wording**

- The interface is in English, in sentence case. Sentences taken from mails stay in their language.
- The interface uses the greys of Windows and the person's accent colour, on Deckle's design system, in the light and the dark theme.
- A date is written one way everywhere: the weekday and the day for the coming days, then the day and the month.
- One role of text keeps one position and one colour on every page: the sentence first, then the sender, where it arrived, and the deadline in the darker grey.

## Limits

- Deckle Mail does not write, answer or forward a mail. To answer, the person uses their own mail app; Open in webmail leads there.
- It holds no calendar. An appointment a mail fixes is a thing to do like another, and reaches the person's planning as a task.
- It does not open a folder of the server as a list. A mail put away is found through Activity.
- It does not load the remote content of a mail. An attachment opens in the app Windows uses for its kind.
- It does not notify. Alerts for a mail that matters belong to a later spec.
- A wrong sort is silent: nothing warns the person that a mail was put away by mistake. The rule that a doubtful mail waits in Triage is the only guard, and it holds as well as the sorting knows its own doubt.
- Joining mails into one subject by a number found in their text can join two matters or split one. A subject wrongly joined shows mails of another matter in the list of an opened page.
- The person sees no way to answer from an opened subject. Every cold reader of the pass looked for one.

## Open

Each question names the option the pass recommends first, the canvas frames that show the options, and what depends on the answer.

1. **What puts a mail away.** A File button, with the tick kept for tasks (15A, 11D), or a tick on every tile (11A). The tile's controls, the right-click menu and the page without Anytype (11E) depend on it.
2. **How many things show.** Every thing (15B), or five then « 8 more » (9B). The maintainer said « 8 more » is fine when the list is long; both expert reviewers refuse to hide a thing to do. The order rule and the height of Triage depend on it.
3. **The order of the list.** Deadlines first then the oldest (15B), the oldest first only, or the order of the navigation, mailbox then alias. The maintainer leaned to « by date, maybe the oldest on top » without settling.
4. **How a task is created.** A menu of days on the tile (15C), one click with the day set in Anytype (11B), or a page to write the task (11C).
5. **A task whose matter is over.** The tile stays until the person ticks (15M, moment 5), or Deckle Mail completes the task itself. The second writes into Anytype on the word of a mail.
6. **Where a new task goes in Anytype.** Which space and which project receive it, and whether the person chooses each time. Add task cannot be built before this.
7. **Good to know lines.** They leave the day after they were seen and cannot be removed (15A), or the person can remove one. Every cold reader expected to remove a line; the maintainer asked for no action.
8. **The sentence of a tile.** A short sentence written when the mail is sorted (15A), or the mail's own sentence quoted word for word (12E). The first reads faster; the second writes nothing and cannot invent.
9. **The opened subject.** The latest mail with the list of the subject beside it (15I, 15J), every mail in one text (10C), or the latest mail alone (10D).
10. **« Say whether one can go back or not ».** Whether this means going back to an earlier mail of the subject, which the list allows, or telling a step that cannot be undone. The maintainer's sentence can be read both ways.
11. **The title of an opened subject.** The sentence of the row clicked (15I), or the mail's own subject (13D).
12. **Settings in the navigation.** At the foot, as Windows does (13F, variant 1), or on top with Triage and Activity (variant 3).
13. **Folders of the server in the navigation.** Not listed (variant 1), or listed under the mailboxes with a folder opening as a list (variant 2, 13G). Both expert reviewers reject the second.
14. **Filing on the server.** On by default with one folder per alias (13H), or off. What the phone and the webmail show depends on it, and so does the right to write to the mailbox at all.
15. **The first sort of a mailbox.** The last thirty days (14A), or the whole history.
16. **The colour of a warning.** The yellow of Windows for an unverified sender, or grey (14B, both drawn). The rule « grey and accent only » and the speed at which a danger is seen pull apart here.
17. **The navigation in a narrow window.** The rail of icons with its badges (15E), or the pane folded behind the menu button (8C, which the maintainer liked).
18. **Where a mail arrived, in words.** « to shop » (15A) was read by two cold readers as the place Deckle Mail filed the mail. « received at shop » is the other wording.
19. **Read and unread on the server.** Whether opening a subject, filing it or sorting it away marks the mail read for the other mail apps.
20. **A visible way to answer.** None beyond the right-click (15D), or one on the opened page, against the rule of no button in the reading.
