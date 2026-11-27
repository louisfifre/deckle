# See what your mail asks of you and settle it fast, with the rest put away by itself

## Need

Draft, not confirmed

**Context of use.** The user owns several mailboxes and aliases. Every mail arrives at the same level, at the same moment, whatever its importance, and they feel swamped. They come to their mail to see what it asks of them, settle it fast, and leave. They plan their work in a planning tool of their own, and they read at length, answer and write mail elsewhere.

**User need.** The user needs to see at once what their mail asks of them, across all their mailboxes, and settle each request in one gesture, in order to stop being swamped and trust that the rest has put itself away.

**Intended outcome.**

- The mail is already sorted when they look.
- What asks something of them stands apart from what only informs them, and both from what they can ignore.
- They understand each request, who asks it and by when, without opening a mail.
- What presses comes first.
- Settling a request takes one gesture: take it on in their planning, or drop it. Each gesture can be undone.
- When they need the mail itself, they read it whole, as it was written.
- They can check what was done, by them and by the sorting, and correct the sorting.
- No mail is deleted without a gesture of theirs, and a doubtful mail is never put away.
- Nothing that identifies them leaves the machine with the text of a mail.
- What they are about to click never moves under their hand, whatever fails or comes back.

## Journey

| The user wants to | The product must |
| --- | --- |
| see what waits for them | have sorted the mail before they look, and show every request of every mailbox in one place, most pressing first |
| understand a request | say in one sentence what is asked, by whom and by when |
| take it on | turn it into a task in their planning, on a day it proposes from the deadline |
| drop it | put its mails in the mail server's trash, where the provider deletes them after its own delay |
| change their mind | undo the gesture and bring the mails back |
| read the mail itself | show it whole as written, with every mail of the same matter |
| look at one mailbox or alias | show the same requests kept to it, and say beforehand where something waits |
| check what was done | list what they did and what the sorting did alone, and let them undo either |
| find an older mail | search every mailbox from one place |
| add a mailbox | sort its recent mail at once, then each mail as it arrives |

When it goes another way:

- The sorting is not sure a mail can be put away: it waits with the requests.
- A mail cannot be read, because it holds only images or is encrypted: it waits with the requests, with the reason.
- A sender fails the checks: the mail waits, marked as not verified, and can only be dropped.
- A mailbox cannot connect: its mail stays shown, and gestures made meanwhile are kept and sent when it is back.
- The sorting cannot run: what is already shown stays usable, and the page does not claim that nothing waits.
- The planning cannot be reached: a task added meanwhile is kept and created when it is back.
- The planning is not connected: no task can be added, and the user is told why.
- The first gesture that would send a mail to the trash: the user is told where it goes and what the provider does with it, and the gesture happens only if they accept.

## Answer

Words a reader needs:

- A **subject** is one mail, or a run of mails about one matter. Mails of one subject are one object everywhere: one row, one line, one opened page.
- **Triage** is the first page: one list of the subjects that wait, every mailbox mixed.
- A **secondary information** is a mail that asks nothing but states a fact that changes something for the user. It shows as a thin line under the list.
- **Set aside** is where a mail lands that asks nothing and changes nothing, with one reason among newsletter, promotion, receipt and no action needed.
- A subject is **treated** once the user added a task for it or sent it to the trash; a dismissed secondary information is treated too.
- **Ending the day** is the command at the bottom of Triage that removes the treated rows and every secondary information line.
- **Activity** is the page that lists what was done.
- The **task control** is the Add task button of a row.
- The **problems button** is this spec's name for the title-bar button that lists what is not working; it is not its label.
- A **caution dot** is the small mark that says something waits for a mailbox, or for Anytype, to be back.
- **Anytype** is the user's planning app, where tasks are created.

The frames are on canvas pages 15, 18 and 19; sizes, colours and wording are kept there and in the module's strings.

### The window

1. The title bar holds the back button, the button that opens and closes the navigation, and one search field. It is the same on every page: nothing in it moves from one page to the next.
2. The back button keeps its place on every page. Where there is no page to go back to, it is disabled, not hidden. Alt+Left and the mouse back button do the same.
3. When something does not work, the problems button shows in the title bar. It opens the list of what is not working, each with its useful command. Example: a mailbox that cannot connect, with Open settings. When everything works, the button is absent. Its icon tells that something needs a look without alarming: it is not a bell.
4. The search field looks in the senders, the sentences and the text of the mails of every mailbox. It answers in a menu under the field as the user types. No page holds a search field of its own, and no page lists the results of a search.
5. No command is reached by a right-click: there is no context menu.
6. No bar at the top of a page pushes its content down. Whatever fails or comes back, the rows and lines of a page stay where they were.
7. A gesture that shows only under the pointer has a double that is always visible. Example: the cross of a secondary information line, and Dismiss all. Rename shows on hover or focus only, in the navigation and on the page (Open 37).
8. A row or a line that holds the keyboard focus looks as it does under the pointer, so the eye ties the focused command to its sentence.

### The navigation

1. The navigation lists Triage and Activity first, then the mailboxes, then Settings at the foot.
2. While there is no activity at all, neither by the user nor by the sorting, Activity is absent from the navigation. It shows from the first line.
3. A mailbox that has aliases is a group header: it folds and unfolds, and opens no page. Its first child is Inbox, the page of the whole mailbox; its aliases follow. The children stand one step in from the header, as Windows draws a group. A mailbox with no alias is a single item that opens its page.
4. A mailbox shows the name given when it was added, or its address when it was given none. Example: a mailbox named Perso shows « Perso »; maison@example.com, added without a name, shows « maison@example.com ».
5. Inbox stands for the main address of the mailbox. It is not renamed.
6. By default each alias is named by the part of its address before the @, with a capital. Example: facture@ is named « Facture », shop@ « Shop ». The user can rename an alias by hand.
7. A mailbox is marked by its initial alone, drawn as a glyph like the other icons of the navigation, with no circle around it.
8. A folded group header wears the number of subjects waiting in the mailbox, its aliases included. Unfolded, the header wears no number and its children wear theirs. Example: three subjects wait in Perso, one of them through the alias shop@; folded, Perso shows 3; unfolded, Shop shows 1.
9. An item where nothing waits shows no number. No number anywhere counts what was treated or set aside.
10. The navigation is as wide as its longest label needs, between a minimum and a maximum width. A label longer than the maximum is cut with an ellipsis before its number, so the number of an item always shows.
11. A pencil to rename shows on a mailbox or an alias when the pointer or the keyboard focus is on it. Inbox shows none.
12. A mailbox that cannot connect wears a caution dot on its mark, and its name says so to a screen reader. The dot takes no width: the navigation does not move when a mailbox fails.

### Starting

1. Before any mailbox exists, Triage shows the picture of an envelope, the title « No mailbox yet », one sentence, and Add mailbox.
2. Add mailbox opens a page of its own. It asks for the address and the name of the mailbox side by side on one line, then for its connection with an app password. A name left empty makes the mailbox show its address.
3. While the first sort of a mailbox runs, Triage says which mailbox is being sorted, shows a progress bar without a count, and adds each subject at the end of the list as it is found. A subject already shown never moves during the sort. Nothing on the page moves when the progress shows or when it goes.
4. From then on, each mail is sorted as it arrives.
5. Opening the window, changing page or opening a subject shows what was sorted before, at once, without sorting anything again.
6. The first time a gesture would send a mail to the trash, a notice says so, once, with the provider's delay. The gesture happens only if the user accepts.

### Where a mail lands

1. Each new mail lands in exactly one of three places: a subject in Triage, a secondary information line, or set aside.
2. A mail that asks something of the user is a subject in Triage.
3. A mail that asks nothing but states a fact that changes something for the user is a secondary information. Example: a refund received, a water cut on Thursday. A newsletter never shows as secondary information.
4. A mail that asks nothing and changes nothing is set aside with its reason. Example: a newsletter, the progress of an order on its way, the notice of a delivery the user received in hand.
5. When the sorting is not sure a mail can be set aside, the mail waits in Triage. Example: a mail with a vague subject from a sender whose other mails matter.
6. A mail the sorting could not read, because it holds only images or is encrypted, waits in Triage with its own subject line as its sentence and the reason as a framed label on its row. It is never set aside unread.
7. A mail whose sender failed the checks waits in Triage with the framed label « Sender not verified », shows the address in place of the name the sender gave itself, and offers the trash only: it cannot become a task. Its task column stays empty, so its trash stands where every other trash stands.
8. A state the sorting sets on a row is a framed label, never plain text.

### Subjects

1. Two mails belong to one subject when they are in the same thread of replies, or when they carry the same order, parcel or file number. Example: the order confirmation from a shop and the pickup notice from its carrier are one subject.
2. Two mails that share only their sender are two subjects. Example: two refund statements from the same insurer are two lines; the insurer's request for a document is a third subject.
3. When a new mail joins a subject that still waits, the subject is sorted again from that mail: it can stay in Triage, become a secondary information or be set aside. Example: a parcel on its way is set aside; « at the pickup point until the 18th » makes it a subject in Triage.
4. A mail that arrives for a subject already treated is sorted as a new mail.
5. A row carries the sentence that asks, even when a later mail of the thread only repeats the request.

### Triage

1. The page has no title and no heading above the list: the selected item of the navigation says where the user is.
2. With subjects waiting, the page shows one row per subject in a single list, every mailbox mixed, never grouped by mailbox. Every subject is on the page: none is folded away, however many there are.
3. A row shows one sentence, a paperclip when a mail of the subject carries a real file, the task control and the trash. It shows nothing else: no line under the sentence, and no sender, mailbox or deadline written apart.
4. The controls of a row stand in two fixed columns at its end, the same on every row and every page: the task control, then the trash. A command never changes place from one row to the next, nor from one state of a row to the next.
5. The task control is one button that reads Add task, with the proposed day written in it when there is one, and no separate chevron. It has one shape and one width on every row, is drawn without a frame so the sentences stay what the eye reads first, and keeps its full label in a narrow window. It is the same control wherever it shows: on a row, on an opened subject, and after a gesture beside Undo.
6. Under the list, each secondary information is one thin line, without a frame of its own: its sentence, then the sender in grey right after it, not set against the right edge nor in a column. A cross shows on the line under the pointer or the keyboard focus, in the column of the trash, and a command above the lines, Dismiss all, always visible, dismisses them all.
7. After the lines, one last line gives the number of mails that were set aside without a line, and leads to the part of Activity that lists them.
8. At the bottom right of the page, one command ends the day. When every row is treated, it becomes the one emphasised command of the page. Both it and Dismiss all stay.
9. With nothing waiting, the page shows a small flat picture and the sentence « Nothing is waiting. ». The picture is not an icon and not a skeleton of rows, and nothing on the page moves, so the user does not take it for a loading page. Secondary information stays under it when there is some.
10. When one mailbox cannot connect, the problems button lists that mailbox with its command. Its rows stay and keep their controls: a gesture made meanwhile is kept and sent when the mailbox is back. Until then the treated row shows its result as any other, followed by a caution dot whose name says it waits for that mailbox.
11. When new mail waits to be sorted because the sorting cannot run, the rows already there stay usable, and the problems button lists that the sorting cannot run. With no row left, the page does not say « Nothing is waiting. », which would be false.
12. A mailbox that connects again says nothing: its caution dots go, and that is the sign.

### What a row says

1. The sentence of a row is explicit by itself and telegraphic: it says what is asked or what happened, names the sender when the sentence needs it to be understood, and carries the date or the time slot the mail states. Example: « Facture Infomaniak de novembre disponible », not « Votre facture de novembre est disponible ».
2. The sentence is written once, when the mail is sorted, in the language of the mail, and does not change between two openings of the window.
3. Two subjects on one page never show the same sentence: the sentence carries the amount, the date or the number that tells them apart. Example: two refund statements of one insurer each give their amount.
4. A deadline is never written apart from the sentence. It is kept as a piece of data: it orders the list and proposes the day of the task.
5. A deadline exists only when the text of the mail states one. The date a mail arrived never stands for a deadline. When the mail gives a time slot, the deadline is the end of the slot.
6. Rows are ordered so that what presses comes first: the subjects with a deadline, the nearest first, then the others, the oldest first.

### Adding a task

1. When Anytype is connected, a row offers Add task, which opens a menu of days: Today, Tomorrow, Pick a day and No day yet (Open 42).
2. A click on Add task always opens that menu, and a click on a day of the menu creates the task. A click on Add task never creates a task at once, so the row never changes under the click that opened the menu.
3. When the sorting found a deadline, Add task carries the proposed day, and the menu opens with that day as its first line, already selected. Example: a row proposing Fri 13 opens on Fri 13, then Today and Tomorrow.
4. The proposed day follows one fixed rule, without a setting: two days before the deadline, and never before today. A deadline of today or already past proposes today.
5. The task created in Anytype holds the sentence of the row, the text of the mail and its sender. It does not need a link back to the mail.
6. Once the task exists, the subject leaves the list: its row greys in place, says « Task for » and the day of the task, and the subject becomes a line of Activity.
7. The app shows no box to tick and never completes a task: a task is done in Anytype.
8. When Anytype cannot be reached, the problems button lists it, in the words used for a mailbox. A task added meanwhile is kept and created when Anytype is back. Until then its row shows its result followed by the same caution dot as a gesture waiting on a mailbox: one sign for "done here, not yet arrived", whatever the cause.
9. When Anytype is not connected, Add task is absent from the rows, as Windows hides what cannot work: the trash is the only command on a row, and a notice that Anytype is not connected says why. The notice can be dismissed, so a user who does not want Anytype does not keep it in sight.

### The trash

1. The second command of a row is the trash, drawn as its symbol, without a word. It says that nothing is to be done.
2. When a subject is treated, by a task or by the trash, its mails go to the trash of the mail server of their mailbox. The inbox then holds only what waits, on the phone and in the webmail too.
3. The app never deletes a mail and never empties a trash on its own. The mail provider deletes what stays in its trash after its own delay. Example: Infomaniak deletes a mail thirty days after it reached the trash.
4. Emptying the trash of a mailbox is neither an option of its page nor a setting (Open 36). It asks first, in a dialog that names the mailbox and the number of mails, and whose default button cancels. It also removes from Activity the lines of the mails it deletes.
5. A mail the sorting set aside goes to the trash when the user ends the day, not before.

### After a gesture

1. After a gesture the row stays where it was, greyed. In the task column, where the task control was, stands the word Undo, followed by what happened in words: « Task for today », « Task for Fri 13 » or « Moved to trash ». Undo with the longest result, such as « Task for Wed 30 Dec », fits in the task column in a wide and a narrow window. In the end column, where the trash was, stands a cross that removes the line. No short timer removes it. This form awaits the user's word (Open 39).
2. A click that lands on a row in the instant after a gesture changed it does nothing: a double click on Add task never undoes the task it has just created.
3. A treated row leaves when the user clicks its cross or ends the day, when the window is closed or refreshed, or after a long fixed time.
4. Undo brings the mails back from the trash to the inbox, removes the task when one was created, and gives the row its controls back. Ctrl+Z undoes the last gesture.

### Secondary information and ending the day

1. The cross of a line dismisses it, and Dismiss all dismisses every line. A dismissed line is treated: its mail goes to the trash as for a subject.
2. Dismiss all also takes the line that counts the mails set aside off the page. Those mails are not dismissed: they still go to the trash when the user ends the day, and Activity still lists them.
3. A line carries no other command and opens nothing: its detail is read in Activity.
4. A line stays until it is dismissed or the day is ended, or until a later mail of its subject replaces it.
5. Ending the day removes the rows already treated and every secondary information line.

### An Inbox page, an alias page

1. The page is titled by a breadcrumb: the mailbox, then Inbox or the name of the alias. The address, the main one for Inbox, is written under it. On an alias page, a pencil to rename shows on the line of the name when the pointer or the keyboard focus is on it.
2. A name being entered or changed is edited in place, at the size of the title, and the placeholder of an empty name fits inside the field.
3. Under the header the page is Triage kept to that mailbox or alias: its rows, then its secondary information.
4. These pages carry no option of the mailbox: the options are on its page in Settings. In the navigation, a mailbox or an alias only filters Triage.
5. These pages list no mail that was set aside: Activity does.

### A subject opened

1. The page opens from a row or from an Activity line. Its title is the sentence of the row.
2. Beside the title stand the task control and the trash of the row, in the same two columns and at the same right edge as on a row, so a gesture stays where the hand already goes.
3. After a gesture made on the page, Undo and the result stand in the task column, as on a treated row. The title keeps its look: on a page it says where the user is. The end column is empty, since the page has no line to remove; the back button returns to the list.
4. Under the title, one grey line gives the sender with their real address, and the date and time of reception.
5. Then comes the text of the latest mail, whole and as written: greeting, request, signature. The app restyles no part of it and rewrites nothing. The text column stays narrow enough to read, about sixty characters a line.
6. After the text come the files really attached to that mail, each as a tile with its name, kind and size, which opens the file in the app Windows uses for its kind. An image that belongs to the body of the mail, a logo or the icons of a signature, is never listed as a file.
7. Beside the text, a list shows every mail of the subject, newest first, each with its date, its sender, one short sentence of what it says, and a mark when it carries a file. The mail shown is marked as selected. Clicking another shows that one in place of the first, and the list stays, so the user can always come back.
8. While an earlier mail of the subject is shown, a mention says so and leads back to the latest, so the user knows it even when the list stands under the text. Example: « Earlier mail · Show the latest ».
9. A mail of the user's own, sent from elsewhere in the same thread, appears in that list under « You ».
10. A mail with no text shows a bar saying so instead of the text. A mail whose sender failed the checks shows no bar: its grey line gives the address alone, with a small mark at the address whose explanation shows on demand. A bar above the text is kept for a mail found to be a probable scam, which a later spec covers.
11. A page that cannot show its mail because the mailbox is unreachable says so where the text would be, and keeps the header and the list.
12. Going back, by the back button, Alt+Left or the mouse back button, finds the list as the user left it.

### Activity

1. The page is titled Activity and has two parts, one shown at a time: what the user did, and what the sorting did alone.
2. In each part, lines are grouped by day, newest first. A line shows the time, the sentence, Undo, what happened, and a chevron that opens the subject. It holds the columns of a treated row in Triage: Undo at the left edge of the task column, what happened right after it, the chevron in the end column.
3. In the user's part, what happened is written in words, as on a treated row: « Task for Fri 13 » or « Moved to trash ».
4. In the sorting's part, each mail has its own line, even several from one sender, and what happened is a framed word among newsletter, promotion, receipt and no action needed.
5. A line that was undone stays, says that its subject is back in Triage, and keeps its chevron only.
6. Undo on a line of the sorting's part brings the mail back to Triage as a subject, whatever it was before. That Undo tells the sorting it was wrong: the next mails of the same kind from the same sender are sorted with that correction.
7. The page holds no search field: an older mail is found with the search of the title bar.
8. With nothing yet, a part says that what will be done will be listed there.

### Settings

1. The page lists the mailboxes, one card each, which opens the page of that mailbox. The card gives the name, the address, the number of aliases, and whether the mailbox is connected, by a dot and in words.
2. Under the list of mailboxes, Add mailbox opens its own page.
3. The page of a mailbox holds all its options: its name, its connection, the sorting by alias with the options of its aliases, and its removal.
4. The connection shows the address, the mail server, and the app password with one command to save it and one to forget it. A saved password is never shown again: a line under the field says only whether one is saved. With no password saved, the command to forget it is hidden, not greyed.
5. A switch sorts the mailbox by alias; it is on as soon as the mailbox has aliases, and off makes the mailbox a single item in the navigation. The options of the aliases unfold under it. One of them, on by default, names an alias from its address with a capital. Example: the alias shop@ is named « Shop ».
6. The page of a mailbox offers to remove it, after a confirmation. Remove mailbox is a standard button at rest and takes the look of a destructive command under the pointer, as the Close button of the Windows 11 title bar does.
7. A mailbox that cannot connect is one condition with one severity wherever it is named, in Triage and here: a caution, since its mail is still shown and nothing is lost.
8. The page shows, by the Anytype logo and in words, whether Anytype is connected.
9. The page has no Reset all.

### A narrow window

1. The navigation stays as the user left it, open or as its rail of icons, until the window is too narrow for the rail (`navigation-pane/deckle-navigation-pane.md`). On the rail, the numbers of the mailboxes stay visible. A click on a mailbox that has aliases opens its pages as a menu, which names the mailbox: the rail shows its mark only.
2. A mailbox that cannot connect keeps its caution dot on the rail, away from the number, so both can show.
3. The search field becomes a button in the title bar.
4. Each row holds on one level: its sentence, then its controls on the same line. Add task keeps its full label.
5. On an opened subject, the list of its mails goes under the text.
6. A window made narrower never brings back a layout it had already left. Example: the list of mails, once under the text, stays there when the user closes the navigation to its rail and the page gains width.

### Rules

- The app reads the inbox of each mailbox and ignores the folders of the server: it sorts by the alias a mail arrived through. It creates no folder and moves a mail only to the trash and back.
- No text of a mail leaves the machine with a value that identifies the user. Such values are replaced before the text goes to a remote model, and stay readable in the window.
- The interface is in English, in sentence case. Sentences taken from mails stay in their language.
- The interface follows Windows' greys and the user's accent colour, on Deckle's design system, in the light and the dark theme. No cue rests on the accent alone: a link is also underlined.
- A date is written one way everywhere: the weekday and the day (« Fri 13 »), with the month when it is not the current one (« Tue 1 Dec »).
- One role of text keeps one look on every page: a sentence, a sender beside it, the result of a gesture after Undo, a state the sorting sets as a framed label, a section title.
- The interface holds no sentence of explanation on a row, a line or a page header. What a symbol or a place already says is not written.

## Limits

- The app does not write, answer or forward a mail. To answer, the user uses their own mail app. It is not a place to read at length.
- It holds no calendar. An appointment a mail fixes is a subject like another, and reaches the user's planning as a task.
- It does not open a folder of the server as a list. A treated mail is found through Activity and the search, for as long as the provider keeps it in the trash.
- A treated mail is gone once the provider has emptied it from the trash. What the user will need later has to be in the task: the text of the mail is, a pickup code carried by an image or a file is not.
- It does not load the remote content of a mail.
- It does not notify. Alerts for a mail that matters belong to a later spec, and so does the warning for a probable scam.
- A wrong sort is silent: a mail set aside by mistake is deleted by the provider after its delay, thirty days at Infomaniak. The rule that a doubtful mail waits in Triage is the only guard, and it holds as well as the sorting knows its own doubt.
- The sentence of a row is written by a model and carries the sender and the date alone: nothing beside it can contradict a wrong sentence.
- A task that is late does not show here. Only Anytype says so.
- Joining mails into one subject by a number found in their text can join two matters or split one. A subject wrongly joined shows mails of another matter in the list of an opened page.
- The earlier mails of a subject are listed on its opened page only while the provider keeps them.

## Open

Each question gives its options, what the spec holds today, and what depends on it.

1. **When does a mail that was set aside go to the trash?** Options: when the user ends the day, or as soon as it is set aside. Today: when the day is ended, which follows from the user's word that the trash is always a gesture of theirs, without their having said it. Depends on it: the moment the provider's delay starts for a wrong sort.
2. **Does a secondary information line open something under a click?** Options: it opens nothing and its detail is read in Activity, or it opens its subject. The user said no, then "a little clickable". Today: it opens nothing.
3. **Commands on an opened subject.** Options: the two gestures of the row plus a link to answer in the webmail, or no command at all. Today: the two gestures of the row. Depends on it: with no command and no right-click, there is no path to answer.
4. **The order of the list.** Options: deadlines first then the oldest, the oldest first only, or the order of the navigation. The user leaned to "by date, maybe the oldest on top". Today: deadlines first, then the oldest.
5. **The proposed day of a task.** Options: two days before the deadline, or three. Today: two.
6. **How long a treated row stays.** A long fixed time, between one and four hours; the value is not set.
7. **The number on Inbox.** Options: the total of the mailbox, the same number the folded header wears, or none. With none, a mail that arrived at the main address is counted nowhere while the group is unfolded. With the total, readers added Inbox's total and an alias's number together, reading the total as a count of its own. Today: the total is drawn.
8. **The width of the navigation.** Its minimum and maximum widths are not set.
9. **The navigation on Triage.** Options: open by default, or closed to its rail; and whether Activity needs to stay in it. Today: open.
10. **The user answered from another mail app.** The subject leaves Triage by itself and is listed in the sorting's part of Activity. Open: whether its mails then go to the trash without a gesture of the user.
11. **Words.** The caption of secondary information (« No action needed »), the command that ends the day (« Done for today »), the two parts of Activity (« By you », « By Deckle »), the proposal on a row (« Add task · Fri 13 », or the day alone, « Fri 13 », faster to scan and less clear to click; « Task · today » was read as a task already created), and whether the first page keeps the name Triage.
12. **Where a new task goes in Anytype.** Which space and which project receive it, and whether the user chooses each time. Add task cannot be built before this. Depends on it: Open 30.
13. **The first sort of a mailbox.** Options: the last thirty days, or the whole history.
14. **The colour of a warning for an unverified sender.** Options: Windows' yellow, or grey.
15. **Read and unread on the server.** Whether opening a subject or treating it marks the mail read for the other mail apps.
16. **Settings in the navigation.** Options: at the foot, as Windows does, or on top with Triage and Activity. Today: at the foot.
17. **A passage of the dictation read by interpretation.** The short sentence of each mail in the list of an opened subject, said "shared", read as the same on every screen; it is not a setting.
18. **"Say whether one can go back or not".** Whether this means going back to an earlier mail of the subject, which the list allows, or telling a step that cannot be undone.
19. **Ending the day while a first sort runs.** Options: the command is disabled until the sort ends, or stays live. Live, it would send to the trash mails the sort set aside before it finished judging the mailbox.
20. **The order of the list once a first sort ends.** During the sort a subject never moves. Options: the list is put in its usual order in one step, or stays in the order of arrival until the next opening.
21. **The result of a task that has no day.** Options: « Task, no day » or « Task added ». The spec words only « Task for » and a day. Falls if No day yet goes (Open 42).
22. **A gesture that waits: in words or by a sign.** Options: the result followed by the caution dot, or words such as « Waiting… », in place of the result or after it. Whatever the form, it never moves the row. Reviewers asked for a word. Today: the dot.
23. **What the problems button's list says.** Not said: since when the content of a failed mailbox is shown and how many gestures wait for it; how many mails wait while the sorting cannot run; whether the list offers to try the sorting again, and whether it names what stops it; and, with a mailbox that cannot connect while the sorting cannot run, whether the list gives two entries, the one that offers a command first, or one entry that says both.
24. **What a search shows.** Its form is a menu under the field. Not said: what a line of the menu shows (the sender, the sentence, the date, where the mail stands: in Triage, treated or set aside), whether a subject still waiting keeps its two gestures there, and what choosing a line opens. The model is the search of Deckle's Settings window.
25. **A sign on Settings when Anytype cannot connect.** Options: the caution dot on the Settings item, or none. Today: the dot is for mailboxes only.
26. **The search field before any mailbox exists.** Options: shown as everywhere, or hidden until there is mail to search.
27. **The default button when removing a mailbox.** Options: Remove, since the mail stays on the server and the mailbox can be added again, or Cancel, as for emptying a trash.
28. **The keyboard on the list.** Options: each command of a row is a stop for Tab, or the list is one stop where arrows move from row to row and Tab enters the row, as Windows documents for a list whose items hold controls. And where the focus goes after a gesture: on Undo of the same row, or on the next row. Which keys make the two gestures from a focused row is not said; the spec gives Ctrl+Z only.
29. **Gestures that wait when their mailbox is removed.** Drawn: they are dropped, and the confirmation names them. Other ways: they are sent first, or the removal is refused until the mailbox is back.
30. **Connecting Anytype.** Today: Settings says whether Anytype is connected and offers no command. Other way: a command to connect it, which depends on Open 12.
31. **Triage when Anytype was never connected.** Nothing about tasks shows, and the notice that Anytype is not connected can be dismissed; readers did not understand why a row that asks for an action only offers the trash. Not said: what form the notice takes, where it stands, and whether it leads to Settings.
32. **The moment between a saved password and a mailbox that is back.** Not said: what the connection on the page of the mailbox shows while the mailbox tries to connect, and what it says when the new password is refused too.
33. **The moment a first mailbox is added.** Drawn: the page of Add mailbox says it connects, with its command greyed; a refusal in the words of a failed mailbox; then Triage while the first sort runs. Not said: whether the user is taken to Triage or stays in Settings.
34. **The end of a first sort.** Drawn: its progress line goes and the list stays. Not said: whether the end of the sort is told in any other way.
35. **An earlier mail of an opened subject.** Whether « Reply in webmail », if it exists (Open 3), opens that mail or the latest.
36. **Where emptying the trash of a mailbox lives.** It is an action, not a setting. The user's lead: a small trash at the top right of the page a mailbox opens from the navigation. Its wording is not said either. Depends on it: Open 41.
37. **Commands shown on hover only.** The rule of the window wants every gesture shown under the pointer to have a double always visible. The user dislikes "hovers everywhere", yet asks for Rename on hover only, and for the explanation of an unverified sender on demand. Not said: whether the rule gives way for these, or each gets a visible double.
38. **Open settings from a subject whose mailbox fails.** Options: the opened subject that cannot show its mail offers to open Settings, or offers nothing. The user doubts the first: it sends them to a page where there may be nothing to do.
39. **Where Undo stands after a gesture, and in what form.** Today: the word Undo followed by the result in the task column, and a cross that removes the line in the end column. The user refuses an icon alone, and refuses Undo beside the cross that removes the line, which is not the same level of action. The trash and Add task stay available on a row at all times.
40. **The words of Triage before any mailbox exists.** The sentence under « No mailbox yet » is not written.
41. **Emptying the trash of a mailbox that cannot connect.** Options: greyed, with the reason on its line, since an emptying cannot be undone and would run later on a count that is no longer true; or kept and sent later like a gesture on a row.
42. **What the menu of days holds.** The spec lists Today, Tomorrow, Pick a day and No day yet; the user listed only Today, Tomorrow and « Pick a date ». Open: whether No day yet stays, and whether the word is day or date. Depends on it: Open 21.
43. **Add task when a connected Anytype cannot be reached for a while.** The user said Add task disappears when Anytype is not available. Today: the spec reads it as not connected, and keeps Add task while a connected Anytype cannot be reached, the task being kept and created later. Other way: Add task disappears then too.
44. **An icon picked for a mailbox.** Later, the user picks the icon of a mailbox in place of its initial, from a small set of about 8 to 18 icons, through the icon picker shared with Deckle Folders; the page of Add mailbox then asks for the icon too. Not said: the set, and where else the picker opens. Depends on it: the picker's own spec.
45. **The narrow window.** To be redone whole and made compact: the user refused a command that drops under its sentence and stays at the right edge, and a search line that spends a line on the date alone. Not said: what gives way when the sentence does not fit beside the controls, and whether a secondary information line also holds on one level. The sentences of the narrow window above stand until it is redrawn.
46. **The icon of the problems button.** Not chosen. Proposed: a circle with an exclamation mark, in outline, in the ink of the other commands of the title bar, with the badge of the count as the only colour. Other ways: Info, which reads as "about" or help rather than something not working; Warning, whose triangle alarms even for a mailbox that only waits for a password.
47. **The words of the problems button.** The visible title of the list it opens and the accessible name of the button are not decided. The frames say « Notifications »; the user rejects it in spirit: "we notify, we do not alarm", but also "it is not a bell".
48. **The cross of a secondary information line.** Options: each line keeps its cross, or only ending the day clears them, since it clears them all. Dismiss all depends on the answer too.
49. **The layout of a row.** The user finds the row weak and thought of moving Add task to the far left of the row. Today: the row as written.
50. **Primary buttons in a grey accent.** With a grey accent, a primary button reads as disabled. Open: whether Deckle's design system draws it otherwise.
51. **A name for the main address.** The user mentioned naming the main address « Moi », from the part of moi@ before the @. Today: Inbox stands for the main address and is not renamed. Open: whether the main address gets a name of its own, and where it would show.
52. **A summary of the day.** It may count the dismissed lines beside the tasks and the mails moved to the trash. Example: « Today · 2 tasks · 2 moved to trash · 3 dismissed ». Open: whether it exists.
