# Deckle Mail

Proposed on 2026-11-09, revised on 2026-11-10 from what the maintainer said of canvas page 15. Nothing is built and no real mailbox is connected. The sentences below hold what he decided on 10 November and, where he left a choice, the direction canvas page 18 recommends, with the layout of rows that canvas page 19 settled over three reviewed loops; every choice he has not confirmed is listed under Open, with the frames that show the options.

## Intention

Deckle Mail serves one person who owns several mailboxes and aliases and feels swamped because every mail arrives at the same level, at the same moment, whatever its importance. They open it to see what their mail asks of them, to sort it fast, and to trust that the rest has put itself away. It is a hub for sorting. It is not a place to read at length, and it never writes or sends a mail.

## Journeys

**Opening the window.** The mail is already sorted when the window opens. The person lands on Triage and sees, in one list, each subject that waits for them, whatever mailbox it came to. A subject is one mail, or a run of mails about one matter. Under the list they see the few facts worth knowing and the number of mails that were set aside.

**Sorting in a row.** The person reads a row and knows what is asked, by whom and by when, without opening anything. If they will deal with it, they add a task: in one click when the row proposes a day, from a menu of days otherwise. If the matter is settled or is not theirs, they choose the trash. Either way the row greys where it is, with Undo, and they go on to the next one. They end on « Nothing is waiting. » and close the window.

**Reading a mail.** The person clicks a row and its subject opens on a page of its own: the latest mail in full, as it was written, and beside it the list of every mail of the same subject. They go back with the back button of the title bar, Alt+Left or the mouse back button, and find the list as they left it.

**Going mailbox by mailbox.** The person clicks Global under a mailbox, or one of its aliases, and gets the same list as Triage, kept to what arrived there. The number beside each of them tells where something waits before they click.

**Checking what was done.** The person opens Activity and sees, newest first, what they did themselves and, apart from it, what the sorting did alone. They undo a line to bring its subject back to Triage, and open a line to read its mails. To find an older mail they search from the title bar.

**Starting.** On a first launch the person adds a mailbox in Settings, with its address and an app password. Deckle Mail sorts its recent mail while they watch subjects appear, and from then on sorts each mail as it arrives. The first time a gesture of theirs would send a mail to the trash, a notice says where treated mail goes and what the mail provider does with it.

## Surfaces

### The window

- The title bar holds the back button, the button that opens and closes the navigation, and one search field. The back button shows only when there is a page to go back to.
- The search field looks in the senders, the sentences and the text of the mails of every mailbox. No page holds a search field of its own.
- No command is reached by a right-click: Deckle Mail has no context menu.
- A gesture that shows only under the pointer has a double that is always visible. Example: the cross of a secondary information line and Dismiss all; the pencil in the navigation and the pencil on the page.

### Navigation

- The navigation lists Triage and Activity first, then the mailboxes, then Settings at the foot.
- A mailbox that has aliases is a group header: it folds and unfolds, and opens no page. Its first child is Global, the page of the whole mailbox; its aliases follow. The children stand one step in from the header, as Windows draws a group. A mailbox with no alias is a single item that opens its page.
- A mailbox or an alias that has no name shows its address. One that has a name shows the name.
- A folded group header wears the number of subjects waiting in the mailbox, its aliases included. Unfolded, the header wears no number and its children wear theirs. Example: three subjects wait in Perso, one of them through the alias shop; folded, Perso shows 3; unfolded, shop shows 1.
- An item where nothing waits shows no number. No number anywhere counts what was treated or set aside.
- The navigation is as wide as its longest label needs, up to 280 px. A longer label ends with an ellipsis. The number of an item always fits inside that width.
- A pencil to rename shows on a mailbox or an alias when the pointer or the keyboard focus is on it.

### Triage

- The page has no title and no heading above the list: the selected item of the navigation says where the person is.
- Before any mailbox exists, the page says that no mailbox is set and offers to add one.
- While the first sort of a mailbox runs, the page says which mailbox is being sorted, shows a progress bar without a count, and adds each subject at the end of the list as it is found. A subject already shown never moves during the sort.
- With subjects waiting, the page shows one row per subject in a single list, every mailbox mixed, never grouped by mailbox. Every subject is on the page: none is folded away, however many there are.
- A row shows one sentence, a paperclip when a mail of the subject carries a real file, the task control and the trash. It shows nothing else: no line under the sentence, and no sender, mailbox or deadline written apart.
- The controls of a row stand in two fixed columns at its end, the same on every row and every page: the task control, then the trash. A command never changes place from one row to the next, nor from one state of a row to the next.
- The task control has one shape and one width on every row: a split button that reads Add task, with the proposed day written in it when there is one, and a chevron that opens the choice of days. It is drawn without a frame, so the sentences stay what the eye reads first.
- Under the list, each secondary information is one thin line, without a frame of its own: its sentence, and the sender in grey in the column of the task controls above. A cross shows on the line under the pointer or the keyboard focus, in the column of the trash, and a command above the lines, always visible, dismisses them all.
- A row or a line that holds the keyboard focus takes the fill it has under the pointer, so the eye ties the focused command to its sentence.
- After the lines, one last line gives the number of mails that were set aside without a line, and leads to the part of Activity that lists them.
- At the bottom right of the page, one command ends the day: it removes the rows already treated and the secondary information lines. When every row is treated, it becomes the one emphasised command of the page.
- With nothing waiting, the page shows a small flat picture in pale grey and the sentence « Nothing is waiting. ». The picture is not an icon, not a skeleton of rows, and nothing on the page moves: the person does not take it for a loading page. Secondary information stays under it when there is some.
- When one mailbox cannot connect, a bar at the top names it, says since when its content is shown, counts the gestures that wait for it once there is one, and offers to open Settings. Its rows stay and keep their controls: a gesture made meanwhile is kept and sent when the mailbox is back. Until then the treated row shows its result as any other, followed by a caution dot whose name says it waits for that mailbox.
- A mailbox that cannot connect wears a caution dot on its mark in the navigation, and its name says so to a screen reader. The dot takes no width: the navigation does not move when a mailbox fails.
- When new mail waits to be sorted because the sorting cannot run, a bar at the top says how many mails arrived since when, and that they will be sorted. The rows already there stay usable. With no row left, the page shows the bar and not « Nothing is waiting. », which would be false.

### A Global page, an alias page

- The page is titled by a breadcrumb: the mailbox, then Global or the name of the alias. A pencil to rename stands beside it, always visible, and the address is written under it.
- A name being entered or changed is edited in place, at the size of the title, and the placeholder of an empty name fits inside the field.
- Under the header the page is Triage kept to that mailbox or alias: its rows, then its secondary information.
- A Global page then holds what belongs to the mailbox itself. A switch sorts the mailbox by alias; it is on as soon as the mailbox has aliases, and off makes the mailbox a single item in the navigation. A card gives the number of mails in the trash of the mailbox and a command that empties it.
- Emptying the trash asks first, in a dialog that names the mailbox and the number of mails, and whose default button cancels.
- These pages list no mail that was set aside: Activity does.

### A subject opened

- The page opens from a row or from an Activity line. Its title is the sentence of the row.
- Beside the title stand the task control and the trash of the row, in the same two columns and at the same right edge as on a row, so a gesture stays where the hand already goes.
- After a gesture made on the page, Undo and the result stand in the task column, as on a treated row. The title keeps its colour: on a page it says where the person is. The end column is empty, since the page has no line to remove; the back button returns to the list.
- Under the title, one grey line gives the sender with their real address and the date and time of reception.
- Then comes the text of the latest mail, whole and as written: greeting, request, signature. Deckle Mail restyles no part of it and rewrites nothing. The text column stays narrow enough to read, about sixty characters a line.
- After the text come the files really attached to that mail, each as a tile with its name, kind and size, which opens the file. An image that belongs to the body of the mail, a logo or the icons of a signature, is never listed as a file.
- Beside the text, a list shows every mail of the subject, newest first, each with its date, its sender, one short sentence of what it says, and a mark when it carries a file. The mail shown is marked as selected. Clicking another shows that one in place of the first, and the list stays, so the person can always come back.
- A mail of the person's own, sent from elsewhere in the same thread, appears in that list under « You ».
- A mail with no text shows a bar saying so instead of the text. A mail whose sender failed the checks shows a bar saying so above the text, and its grey line gives the address alone.
- A page that cannot show its mail because the mailbox is unreachable says so in a bar that offers to open Settings, and keeps the header and the list.

### Activity

- The page is titled Activity and has two parts, one shown at a time: what the person did, and what the sorting did alone.
- In each part, lines are grouped by day, newest first. A line shows the time, the sentence, Undo, what happened, and a chevron that opens the subject. It holds the columns of a treated row in Triage: Undo at the left edge of the task column, what happened right after it, the chevron in the end column.
- In the person's part, what happened is written in words, as on a treated row: « Task for Fri 13 » or « Moved to trash ».
- In the sorting's part, each mail has its own line, even several from one sender, and what happened is a framed word among newsletter, promotion, receipt and no action needed.
- A line that was undone stays, says that its subject is back in Triage, and keeps its chevron only.
- The page holds no search field.
- With nothing yet, a part says that what will be done will be listed here.

### Settings

- The page lists the mailboxes, one fold each. The header of a fold gives the name, the address, the number of aliases, and whether the mailbox is connected, by a coloured dot and in words.
- An open fold shows the address, the mail server, and the app password with one command to save it and one to forget it. A saved password is never shown again: a line under the field says only whether one is saved. With no password saved, the command to forget it is hidden, not greyed.
- A fold offers to remove its mailbox, after a confirmation.
- When a mailbox cannot connect, a bar at the top of the page names it and says what to do. It stays as long as the mailbox cannot connect: it has no cross.
- A mailbox that cannot connect is one condition with one severity wherever a bar names it, in Triage and here: a caution, since its mail is still shown and nothing is lost.
- Under the folds, a command adds a mailbox.
- The page shows, by the Anytype logo and in words, whether Anytype is connected.
- The page has no Reset all.

### A narrow window

- In a narrow window the navigation folds to its rail of icons and the numbers of the mailboxes stay visible on it. A click on a mailbox that has aliases opens its pages as a menu, which names the mailbox: the rail shows its mark only.
- A mailbox that cannot connect keeps its caution dot on the rail. The dot sits at the bottom of the mark, away from the number, so both can show.
- The search field becomes a button in the title bar.
- A row goes on two levels: its sentence, then its controls, which keep their two columns at the right edge. A secondary information line does the same: its sentence, then its sender.
- On an opened subject the list of its mails goes under the text.

## Behaviours

**Where a mail lands**

- Each new mail lands in exactly one of three places: a subject in Triage, a secondary information line, or set aside.
- A mail that asks something of the person is a subject in Triage.
- A mail that asks nothing but states a fact that changes something for the person is a secondary information. Example: a refund received, a water cut on Thursday.
- A mail that asks nothing and changes nothing is set aside with one reason among newsletter, promotion, receipt and no action needed. Example: a newsletter, the progress of an order on its way, the notice of a delivery the person received in hand.
- When the sorting is not sure a mail can be set aside, the mail waits in Triage. Example: a mail with a vague subject from a sender whose other mails matter.
- A mail the sorting could not read, because it holds only images or is encrypted, waits in Triage with its own subject as its sentence and the reason as a framed label on its row. It is never set aside unread.
- A mail whose sender failed the checks waits in Triage with the framed label « Sender not verified », shows the address in place of the name the sender gave itself, and offers the trash only: it cannot become a task. Its task column stays empty, so its trash stands where every other trash stands.
- A state the sorting sets on a row is a framed label, never plain text.

**Subjects**

- Mails of one subject are one object everywhere: one row, one line, one opened page.
- Two mails belong to one subject when they are in the same thread of replies, or when they carry the same order, parcel or file number. Example: the order confirmation from a shop and the pickup notice from its carrier are one subject.
- Two mails that share only their sender are two subjects. Example: two refund statements from the same insurer are two lines; the insurer's request for a document is a third subject.
- When a new mail joins a subject that still waits, the subject is sorted again from that mail: it can stay in Triage, become a secondary information or be set aside. Example: a parcel on its way is set aside; « at the pickup point until the 18th » makes it a subject in Triage.
- A mail that arrives for a subject already treated is sorted as a new mail.
- A row carries the sentence that asks, even when a later mail of the thread only repeats the request.

**What a row says**

- The sentence of a row is explicit by itself and telegraphic: it says what is asked or what happened, names the sender when the sentence needs it to be understood, and carries the date or the time slot the mail states. Example: « Facture Infomaniak de novembre disponible », not « Votre facture de novembre est disponible ».
- The sentence is written once, when the mail is sorted, in the language of the mail, and does not change between two openings of the window.
- Two subjects on one page never show the same sentence: the sentence carries the amount, the date or the number that tells them apart. Example: two refund statements of one insurer each give their amount.
- A deadline is never written apart from the sentence. It is kept as a piece of data: it orders the list and proposes the day of the task.
- A deadline exists only when the text of the mail states one. The date a mail arrived never stands for a deadline. When the mail gives a time slot, the deadline is the end of the slot.
- Rows are ordered so that what presses comes first: the subjects with a deadline, the nearest first, then the others, the oldest first.

**Adding a task**

- When Anytype is connected, a row offers Add task with the choices Today, Tomorrow, Pick a day and No day yet.
- When the sorting found a deadline, the row offers instead the task on a proposed day: one click creates it, and the same control still opens the choice of another day.
- The proposed day follows one fixed rule, without a setting: two days before the deadline, and never before today. A deadline of today or already past proposes today.
- The task created in Anytype holds the sentence of the row, the text of the mail and its sender. It does not need a link back to the mail.
- When no deadline was found, one click on Add task creates the task with no day, and the chevron opens the choice of days.
- Once the task exists, the subject leaves the list: its row greys in place, says « Task for » and the day of the task, and the subject becomes a line of Activity.
- Deckle Mail shows no box to tick and never completes a task: a task is done in Anytype.
- When Anytype cannot be reached, a bar says so in the words used for a mailbox, counts the tasks that wait and offers to open Settings; a task added meanwhile is kept and created when Anytype is back. Until then its row shows its result followed by the same caution dot as a gesture waiting on a mailbox: one sign for "done here, not yet arrived", whatever the cause.
- When Anytype is not connected, nothing about tasks is drawn: the trash is the only command on a row.

**The trash**

- The second command of a row is the trash, drawn as its symbol, without a word. It says that nothing is to be done.
- When a subject is treated, by a task or by the trash, its mails go to the trash of the mail server of their mailbox. The inbox then holds only what waits, on the phone and in the webmail too.
- Deckle Mail never deletes a mail and never empties a trash on its own. The mail provider deletes what stays in its trash after its own delay. Example: Infomaniak deletes a mail thirty days after it reached the trash.
- The first time a gesture would send a mail to the trash, a notice says so, once, with the provider's delay. The gesture happens only if the person accepts.
- After a gesture the row stays where it was, greyed. Undo stands at the left edge of the task column, where the task control was, followed by what happened in words: « Task for Fri 13 » or « Moved to trash ». The words fit the column with the longest date, « Task for Wed 30 Dec »: « Task added · Wed 30 Dec » did not. A cross stands where the trash was and removes the line. No short timer removes it.
- A click that lands on a row in the instant after a gesture changed it does nothing: a double click on Add task never undoes the task it has just created.
- A treated row leaves when the person clicks its cross or ends the day, when the window is closed or refreshed, or after a long fixed time.
- Undo brings the mails back from the trash to the inbox, removes the task when one was created, and gives the row its controls back. Ctrl+Z undoes the last gesture.
- A mail the sorting set aside goes to the trash when the person ends the day, not before.

**Secondary information**

- The cross of a line dismisses it, and Dismiss all dismisses every line. A dismissed line is treated: its mail goes to the trash as for a subject.
- A line carries no other command and opens nothing: its detail is read in Activity.
- A line stays until it is dismissed or the day is ended, or until a later mail of its subject replaces it.
- A newsletter never shows as secondary information.

**Corrections**

- Undo on a line of the sorting's part brings the mail back to Triage as a subject, whatever it was before.
- That Undo tells the sorting it was wrong: the next mails of the same kind from the same sender are sorted with that correction.

**Outside the window**

- Deckle Mail reads the inbox of each mailbox and ignores the folders of the server: it sorts by the alias a mail arrived through. It creates no folder and moves a mail only to the trash and back.
- Opening the window, changing page or opening a subject shows what was sorted before, at once, without sorting anything again.
- No text of a mail leaves the machine with a value that identifies the person. Such values are replaced before the text goes to a remote model, and stay readable in the window.

**Look and wording**

- The interface is in English, in sentence case. Sentences taken from mails stay in their language.
- The interface uses the greys of Windows and the person's accent colour, on Deckle's design system, in the light and the dark theme. No cue rests on the accent alone: the maintainer's accent is grey, so a link is also underlined.
- A date is written one way everywhere: the weekday and the day (« Fri 13 »), with the month when it is not the current one (« Tue 1 Dec »).
- One role of text keeps one style on every page: a sentence in the main text colour, a sender beside it in grey, the result of a gesture in grey words after Undo, a state the sorting sets as a framed label, a section title (« No action needed », a day in Activity, a group of settings) in semibold in the main text colour, at the edge of its list.
- The interface holds no sentence of explanation on a row, a line or a page header. What a symbol or a place already says is not written.

## Limits

- Deckle Mail does not write, answer or forward a mail. To answer, the person uses their own mail app.
- It holds no calendar. An appointment a mail fixes is a subject like another, and reaches the person's planning as a task.
- It does not open a folder of the server as a list. A treated mail is found through Activity and the search, for as long as the provider keeps it in the trash.
- A treated mail is gone once the provider has emptied it from the trash. What the person will need later has to be in the task: the text of the mail is, a pickup code carried by an image or a file is not.
- It does not load the remote content of a mail. An attachment opens in the app Windows uses for its kind.
- It does not notify. Alerts for a mail that matters belong to a later spec.
- A wrong sort is silent, and now has a term: a mail set aside by mistake is deleted by the provider thirty days after it reached the trash. The rule that a doubtful mail waits in Triage is the only guard, and it holds as well as the sorting knows its own doubt.
- The sentence of a row is written by a model and carries the sender and the date alone: nothing beside it can contradict a wrong sentence.
- A task that is late does not show in Deckle Mail. Only Anytype says so.
- Joining mails into one subject by a number found in their text can join two matters or split one. A subject wrongly joined shows mails of another matter in the list of an opened page.
- The earlier mails of a subject are listed on its opened page only while the provider keeps them.

## Open

Each question names the option the canvas recommends first, the frames that show the options, and what depends on the answer.

1. **When a mail that was set aside goes to the trash.** When the person ends the day (18M, reading A), or as soon as it is set aside (reading C). The maintainer said the trash is always a gesture of his; the first reading follows from it without his having said it. The thirty days a wrong sort has to be caught start from that moment.
2. **A secondary information line under a click.** It opens nothing and its detail is read in Activity (18A), or it opens its subject. The maintainer said no, then « a little clickable ».
3. **One command or two to clear the page.** Dismiss all above the lines and the end of the day at the bottom right, as asked (18M, reading A), or the end of the day alone (reading B). The two overlap when no row was treated.
4. **Commands on an opened subject.** The two gestures of the row and a link to answer in the webmail (18K), or no command at all, as approved in 15I (18L). Without the right-click, the second leaves no path to answer.
5. **The order of the list.** Deadlines first then the oldest (18B), the oldest first only, or the order of the navigation. The maintainer leaned to « by date, maybe the oldest on top » without settling.
6. **The proposed day of a task.** Two days before the deadline (18A), or three. He said « two or three ».
7. **How long a treated row stays.** He said a long fixed time, between one and four hours.
8. **The number on Global.** The total of the mailbox, the same number the folded header wears (19A, 19E; 18E, Global A), or none (Global B). With none, a mail that arrived at the main address is counted nowhere while the group is unfolded. With the total, three cold reviewers of page 19 added Global 3 and shop 1 into 4: the total reads as a count of its own.
9. **The mark of a mailbox in the navigation.** Its initial (18E, mark A), the same envelope for all (mark B), or none (mark C). On the rail, three envelopes cannot be told apart.
10. **The navigation on Triage.** Open by default (18A), or closed to its rail (18T), and whether Activity needs to stay in it.
11. **The person answered from another mail app.** The subject leaves Triage by itself and is listed in the sorting's part of Activity; whether its mails then go to the trash without a gesture of the person is not said.
12. **Words.** The caption of secondary information (« No action needed »), the command that ends the day (« Done for today »), the two parts of Activity (« By you », « By Deckle »), the proposal on a row (« Add task · Fri 13 », 19B; the maintainer had said « Task · today », which a cold reader took for a task already created; or the day alone, « Fri 13 », 19F, which reviewers found faster to scan and less clear to click), and whether the first page keeps the name Triage.
13. **Where a new task goes in Anytype.** Which space and which project receive it, and whether the person chooses each time. Add task cannot be built before this.
14. **The first sort of a mailbox.** The last thirty days, or the whole history.
15. **The colour of a warning.** The yellow of Windows for an unverified sender, or grey (14B, both drawn).
16. **Read and unread on the server.** Whether opening a subject or treating it marks the mail read for the other mail apps.
17. **Settings in the navigation.** At the foot, as Windows does (18A), or on top with Triage and Activity (13F, variant 3).
18. **Two passages of the dictation read by interpretation.** The short sentence of each mail in the list of an opened subject, « shared » (read: the same on every screen); and « capitals or not » for aliases (read: a display setting of the name). Neither is drawn as a setting.
19. **« Say whether one can go back or not ».** Whether this means going back to an earlier mail of the subject, which the list allows, or telling a step that cannot be undone. Still unanswered from 9 November.
20. **One click on Add task when no day is proposed.** It creates the task with no day (19A), or it opens the choice of days as the chevron does. The first keeps one behaviour for the main part of the control on every row; the second never creates a task without a day the person chose.
21. **Ending the day while a first sort runs.** The command is disabled until the sort ends (27S), or stays live. Live, it would send to the trash mails the sort set aside before it finished judging the mailbox.
22. **The order of the list once a first sort ends.** During the sort a subject never moves; whether the list is then put in its usual order in one step, or stays in the order of arrival until the next opening.
23. **The result of a task that has no day.** « Task, no day » (28U) or « Task added » (26U). The spec words only « Task for » and a day.
24. **A gesture that waits, in words or by its dot.** The result followed by the caution dot, with the count in the bar (27T, 27B), or the words « Task waiting » in place of the result, which loses the day (28TW). Six reviewers over four loops asked for a word.
25. **What the person can do when the sorting cannot run.** Nothing: the bar only informs (26Q), or a command to try again (27Q). What stops the sorting, and whether the bar names it, is not said.
26. **What a search shows.** The canvas draws the plainest answer (28SR): a page titled by the word searched, one line per mail found, in the form of an Activity line, with where the mail stands, in Triage, treated or set aside. Not said: whether a subject still waiting keeps its two gestures there.
27. **A sign on Settings when Anytype cannot connect.** The caution dot of a failed mailbox, on the Settings item (26T), or none. The spec gives the dot to mailboxes only.
28. **The search field before any mailbox exists.** Shown as everywhere (26Z), or hidden until there is mail to search.
29. **The default button when removing a mailbox.** Remove (28RM), since the mail stays on the server and the mailbox can be added again, or Cancel as for emptying a trash.
30. **Two failures at once.** A mailbox that cannot connect while the sorting cannot run: two bars, the one that offers a command first (31BB), or one bar that says both (32BM), which takes 66 px less before the first row. Two reviewers of three prefer one bar.
31. **The keyboard on the list.** Each command of a row is a stop for Tab (30FK), or the list is one stop where arrows move from row to row and Tab enters the row, as Windows documents for a list whose items hold controls (31FR). And where the focus goes after a gesture: on Undo of the same row (31FG) or on the next row (32FN). Which keys make the two gestures from a focused row is not said; the spec gives Ctrl+Z only.
32. **Gestures that wait when their mailbox is removed.** The canvas draws that they are dropped and that the confirmation names them (32RM). Not said: whether they could be sent first, or the removal refused until the mailbox is back.
33. **Emptying the trash of a mailbox that cannot connect.** Greyed, with the reason on its line (31GB), since an emptying cannot be undone and would run later on a count that is no longer true; or kept and sent later like a gesture on a row.
34. **Connecting Anytype.** Settings says whether Anytype is connected (31MA) and offers no command; a command to connect it (32MC) depends on Open 13.
35. **Triage when Anytype was never connected.** Nothing about tasks is drawn (31NA), as written above; three reviewers did not understand why a row that asks for an action only offers the trash. Option: an information bar, not a warning, that says it once and leads to Settings (32NB).
36. **The moment between a saved password and a mailbox that is back.** The canvas draws before (32JP) and after (32JC). Not said: what the fold shows while the mailbox tries to connect, and what it says when the new password is refused too.
37. **A mailbox that comes back.** Silently: its bar and its dots go, and that is the sign (32JT); or a bar says it is back and counts the changes sent (33JT), and then what makes that bar go. Two reviewers found the silent return too little, one found the bar too much.
38. **The moment a first mailbox is added.** The canvas draws the fold saying it connects, the command greyed (34LC), a refusal in the words of a failed mailbox (34LR), then Triage shown by itself while the first sort runs (34S). Not said: whether the person is taken to Triage or stays in Settings.
39. **The end of a first sort.** Its progress line goes and the list stays (33LF), or a bar says the mailbox is sorted and how many subjects wait (34LG).
40. **An earlier mail of an opened subject.** The canvas keeps the title and the two gestures, which act on the whole subject, and changes only the line of the sender and the text (34KS). Not said: whether « Reply in webmail » then opens that mail or the latest.
