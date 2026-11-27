# See what your mail asks of you and settle it fast, with the rest put away by itself

> Not reviewed.

## Need

### Context of use

1. The [user](../GLOSSARY.md#user) owns several [mailboxes](GLOSSARY.md#mailbox) and [aliases](GLOSSARY.md#alias). Every [mail](GLOSSARY.md#mail) arrives at the same level, at the same moment, whatever its importance, and they feel swamped. They come to their mail to see what it asks of them, settle it fast, and leave. They plan their work in a [planning](GLOSSARY.md#planning) of their own, and they read at length, answer and write mail in another [application](../GLOSSARY.md#application).

### User need

1. The [user](../GLOSSARY.md#user) needs to see at once what their [mail](GLOSSARY.md#mail) asks of them, across all their [mailboxes](GLOSSARY.md#mailbox), and [settle](GLOSSARY.md#settle) each [request](GLOSSARY.md#request) in one [gesture](../GLOSSARY.md#gesture), in order to stop being swamped and trust that the rest has put itself away.

### Intended outcome

1. The [mail](GLOSSARY.md#mail) is already sorted when they look.
2. What asks something of them stands apart from what only informs them, and both from what they can ignore.
3. They understand each [request](GLOSSARY.md#request), who asks it and by when, without opening a mail.
4. What presses comes first.
5. [Settling](GLOSSARY.md#settle) a request takes one [gesture](../GLOSSARY.md#gesture): take it on in their [planning](GLOSSARY.md#planning), or drop it. Each gesture can be undone.
6. When they need the mail itself, they read it whole, as it was written.
7. They can check what was done, by them and by the [sorting](GLOSSARY.md#sort), and correct the sorting.
8. No mail is deleted without a gesture of theirs, and a doubtful mail is never [put away](GLOSSARY.md#put-away).
9. Nothing that identifies them leaves the machine with the text of a mail.
10. What they are about to touch never moves under their hand, whatever fails or comes back.

## Journey

| The user wants to | The product must |
| --- | --- |
| see what waits for them | have [sorted](GLOSSARY.md#sort) the [mail](GLOSSARY.md#mail) before they look, and show every [request](GLOSSARY.md#request) of every [mailbox](GLOSSARY.md#mailbox) together, the most pressing first |
| understand a request | say in one sentence what is asked, by whom and by when |
| take it on | turn it into a [task](GLOSSARY.md#task) in their [planning](GLOSSARY.md#planning), on a day it proposes from the [deadline](GLOSSARY.md#deadline) |
| drop it | put its mails in the [trash](GLOSSARY.md#trash) of their mailbox, where the [provider](GLOSSARY.md#provider) deletes them after its own delay |
| change their mind | undo the [gesture](../GLOSSARY.md#gesture) and bring the mails back |
| know what only informs them | show each [notice](GLOSSARY.md#notice) apart from the requests, in one sentence, until they dismiss it |
| read the mail itself | show it whole, as written, with every mail of the same [matter](GLOSSARY.md#matter) |
| look at one mailbox or [alias](GLOSSARY.md#alias) | show the same requests kept to it, and say beforehand where something waits |
| check what was done | list what they did and what the sorting did alone, and let them undo either |
| find an older mail | search every mailbox at once |
| end their day of mail | clear what they settled and every notice, in one gesture |
| add a mailbox | sort its recent mail at once, then each mail as it arrives |

## Rules

1. [What the product shows is its real state.](../deckle.rules.md)
2. [Everything the product tells the user reaches them with animations off as well.](../deckle.rules.md)
3. [Everything the product tells the user reaches them with colours off as well.](../deckle.rules.md)
4. The [product](../GLOSSARY.md#product) never deletes a [mail](GLOSSARY.md#mail) and never empties a [trash](GLOSSARY.md#trash) by itself.
5. A mail that the [sorting](GLOSSARY.md#sort) is not sure it can [put away](GLOSSARY.md#put-away), or cannot read, waits with the [requests](GLOSSARY.md#request).
6. No text of a mail leaves the machine with a value that identifies the [user](../GLOSSARY.md#user).
7. The product changes nothing in a [mailbox](GLOSSARY.md#mailbox) but moving a mail to its trash and back.
8. The product does not write, answer or forward a mail.
9. The product loads nothing that a mail points to outside itself.
10. What the user is about to touch never moves, whatever fails or comes back.

## Workflow

### Adding a mailbox

When it goes well:

1. Before any [mailbox](GLOSSARY.md#mailbox) exists, the only thing to do is to add one.
2. The [user](../GLOSSARY.md#user) gives its address, a name if they want one, and what lets the [product](../GLOSSARY.md#product) connect to it. Without a name, the mailbox shows its address.
3. The product [sorts](GLOSSARY.md#sort) the recent [mail](GLOSSARY.md#mail) of the mailbox at once. While it sorts, it says which mailbox it sorts, and each [request](GLOSSARY.md#request) it finds joins the others without moving those already shown.
4. From then on, each mail is sorted as it arrives.
5. Each [alias](GLOSSARY.md#alias) of the mailbox shows apart, named from its address until the user renames it.
6. What lets the product connect is never shown again once it is kept: the product says only whether it keeps one.

Otherwise:

7. The mailbox cannot connect: the product says so, and nothing of it is sorted until it can.

### Sorting the mail

When it goes well:

1. Each new [mail](GLOSSARY.md#mail) goes to exactly one place: a [request](GLOSSARY.md#request), a [notice](GLOSSARY.md#notice), or [put away](GLOSSARY.md#put-away) with its reason.
2. A newsletter is never a notice.
3. The mails of one [matter](GLOSSARY.md#matter) are sorted, shown and [settled](GLOSSARY.md#settle) as one. Two mails that share only their sender are two matters.
4. When a new mail joins a matter that still waits, the matter is sorted again from that mail.
5. A mail that arrives for a matter already settled is sorted as a new mail.
6. Opening the [window](../GLOSSARY.md#window) or a matter shows what was sorted before, at once, without sorting anything again.

Otherwise:

7. The [sorting](GLOSSARY.md#sort) is not sure a mail can be put away: the mail waits with the requests.
8. The sorting cannot read a mail, because it holds only images or is encrypted: the mail waits with the requests, with the reason.
9. A sender fails the checks of who they are: their mail waits with the requests, says that its sender is [not verified](GLOSSARY.md#unverified-sender), shows the address in place of the name the sender gave, and can only be dropped.
10. The sorting cannot run: what is already shown stays usable, the [product](../GLOSSARY.md#product) says that the sorting cannot run, and it never says that nothing waits.

### Seeing what waits

When it goes well:

1. Every [request](GLOSSARY.md#request) of every [mailbox](GLOSSARY.md#mailbox) shows together, the mailboxes mixed, none folded away, however many there are.
2. What presses comes first: the requests with a [deadline](GLOSSARY.md#deadline), the nearest first, then the others, the oldest first.
3. Each request shows one sentence, whether a mail of it carries a real file, and the two [gestures](../GLOSSARY.md#gesture) that [settle](GLOSSARY.md#settle) it.
4. The [notices](GLOSSARY.md#notice) show apart from the requests, each in one sentence with its sender.
5. The [product](../GLOSSARY.md#product) says how many [mails](GLOSSARY.md#mail) it [put away](GLOSSARY.md#put-away), and that number leads to them.
6. With nothing waiting, the product says that nothing is waiting, in a way the [user](../GLOSSARY.md#user) cannot take for something still loading.

Otherwise:

7. A mailbox cannot connect: its requests stay shown and keep their gestures, and the product says which mailbox cannot connect and what to do. A gesture made meanwhile is kept and sent when the mailbox is back; until then, the request says that it waits for it.
8. A mailbox connects again: the product says nothing more than that its requests no longer wait for it.

### Understanding a request

When it goes well:

1. The sentence of a [request](GLOSSARY.md#request) is explicit by itself and short: it says what is asked or what happened, names the sender when the sentence needs it to be understood, and carries the date or the time the [mail](GLOSSARY.md#mail) states.
2. The sentence is written once, when the mail is [sorted](GLOSSARY.md#sort), in the language of the mail, and it does not change from one time to the next.
3. Two requests never show the same sentence: each carries what tells it apart.
4. A request carries the sentence that asks, even when a later mail of its [matter](GLOSSARY.md#matter) only repeats it.
5. A request has a [deadline](GLOSSARY.md#deadline) only when the text of its mail states one. The day a mail arrived never stands for a deadline; when a mail gives a span of time, the deadline is its end.

### Taking it on

When it goes well:

1. When their [planning](GLOSSARY.md#planning) is connected, one [gesture](../GLOSSARY.md#gesture) on a [request](GLOSSARY.md#request) offers the days of the [task](GLOSSARY.md#task): today, tomorrow, a day they pick, or no day yet. The task is created only once they choose.
2. When the request has a [deadline](GLOSSARY.md#deadline), the [product](../GLOSSARY.md#product) proposes a day before it, never before today, and offers it first. The day follows one fixed rule, with nothing to set.
3. The task holds the sentence of the request, the text of the [mail](GLOSSARY.md#mail) and its sender.
4. Once the task exists, the mails of the [matter](GLOSSARY.md#matter) go to the [trash](GLOSSARY.md#trash) of their [mailbox](GLOSSARY.md#mailbox), and the matter stays where it was, saying which day the task is for, until the [user](../GLOSSARY.md#user) clears it or ends their day.
5. The product never completes a task: a task is done in the planning.

Otherwise:

6. The planning cannot be reached: a task added meanwhile is kept and created when it is back; until then, the request says that it waits for it, in the same way as a gesture that waits for a mailbox.
7. The planning is not connected: no task can be added, and the product says why, in a way the user can dismiss.

### Dropping it

When it goes well:

1. One [gesture](../GLOSSARY.md#gesture) says that nothing is to be done: the mails of the [matter](GLOSSARY.md#matter) go to the [trash](GLOSSARY.md#trash) of their [mailbox](GLOSSARY.md#mailbox), and the mailbox then holds only what waits, in every other [application](../GLOSSARY.md#application) too.
2. The matter stays where it was, saying that it went to the trash, until the [user](../GLOSSARY.md#user) clears it or ends their day.
3. The [provider](GLOSSARY.md#provider) deletes what stays in its trash after its own delay.

Otherwise:

4. A gesture would send a [mail](GLOSSARY.md#mail) to the trash for the first time: the [product](../GLOSSARY.md#product) says once where it goes and what the provider does with it, and the gesture happens only if they accept.

### Changing their mind

When it goes well:

1. Every [gesture](../GLOSSARY.md#gesture) that [settles](GLOSSARY.md#settle) a [matter](GLOSSARY.md#matter) can be undone, on the matter itself or from what was done.
2. Undoing brings the [mails](GLOSSARY.md#mail) back from the [trash](GLOSSARY.md#trash) to their [mailbox](GLOSSARY.md#mailbox), removes the [task](GLOSSARY.md#task) when one was created, and gives the matter its gestures back.
3. A touch that lands on a matter the instant after a gesture changed it does nothing: a gesture is never undone by the touch that follows it by mistake.

### Reading the mail itself

When it goes well:

1. The [user](../GLOSSARY.md#user) opens a [matter](GLOSSARY.md#matter) from where it waits or from what was done. It shows the sentence of the matter, with the two [gestures](../GLOSSARY.md#gesture) that [settle](GLOSSARY.md#settle) it, where they stand on the matter.
2. It gives the sender with their real address, and the day and time the [mail](GLOSSARY.md#mail) arrived.
3. It shows the text of the latest mail whole, as written. The [product](../GLOSSARY.md#product) restyles nothing of it and rewrites nothing.
4. It shows the files really attached to that mail, and one gesture opens each in the [application](../GLOSSARY.md#application) the [system](../GLOSSARY.md#system) uses for its kind. An image that belongs to the text of the mail is never shown as a file.
5. Every mail of the matter shows beside it, the newest first, each with its day, its sender, one short sentence, and whether it carries a file. One gesture shows another of them in its place, and the product says when the mail shown is not the latest, with a way back to it.
6. A mail of the user's own, sent from elsewhere in the same matter, shows among them as theirs.
7. Going back finds what waits as they left it.

Otherwise:

8. A mail has no text: the product says so where the text would be.
9. The [mailbox](GLOSSARY.md#mailbox) cannot connect and the mail cannot be shown: the product says so where the text would be, and keeps the rest.

### Looking at one mailbox

When it goes well:

1. Each [mailbox](GLOSSARY.md#mailbox) shows the name the [user](../GLOSSARY.md#user) gave it, or its address, and its [aliases](GLOSSARY.md#alias) show with it.
2. Each mailbox and each alias says how many [matters](GLOSSARY.md#matter) wait in it, and says nothing when none waits. Nothing counts what was [settled](GLOSSARY.md#settle) or [put away](GLOSSARY.md#put-away).
3. Choosing a mailbox or an alias shows the same [requests](GLOSSARY.md#request) and [notices](GLOSSARY.md#notice), kept to it.
4. The user renames a mailbox or an alias where it shows.

Otherwise:

5. A mailbox cannot connect: it says so where it shows, without moving anything around it.

### Checking what was done

When it goes well:

1. What was done shows in two parts, one at a time: what the [user](../GLOSSARY.md#user) did, and what the [sorting](GLOSSARY.md#sort) did alone. In each part, what was done is grouped by day, the newest first.
2. In the user's part, each line says in words what happened, and can be undone.
3. In the sorting's part, each [mail](GLOSSARY.md#mail) has its own line, with the reason it was [put away](GLOSSARY.md#put-away), and can be undone.
4. Undoing what the sorting did brings the mail back as a [request](GLOSSARY.md#request), and the sorting takes it as a correction: the next mails of the same kind from the same sender are sorted with it.
5. A line that was undone stays, and says that its [matter](GLOSSARY.md#matter) waits again.
6. Every line opens its matter.

Otherwise:

7. Nothing was done yet: each part says that what will be done shows there.

### Finding an older mail

When it goes well:

1. One search looks in the senders, the sentences and the text of the [mails](GLOSSARY.md#mail) of every [mailbox](GLOSSARY.md#mailbox), and answers as the [user](../GLOSSARY.md#user) types.
2. The search is the same wherever they are.

### Ending the day

When it goes well:

1. The [user](../GLOSSARY.md#user) dismisses one [notice](GLOSSARY.md#notice), or all of them in one [gesture](../GLOSSARY.md#gesture). A dismissed notice is [settled](GLOSSARY.md#settle): its [mail](GLOSSARY.md#mail) goes to the [trash](GLOSSARY.md#trash).
2. A notice stays until it is dismissed or the day is ended, or until a later mail of its [matter](GLOSSARY.md#matter) replaces it.
3. One gesture ends the day: the matters already settled and every notice go. When every [request](GLOSSARY.md#request) is settled, ending the day is what the [product](../GLOSSARY.md#product) puts forward.
4. A mail the [sorting](GLOSSARY.md#sort) [put away](GLOSSARY.md#put-away) goes to the trash when the user ends the day, not before.

### Emptying a trash

When it goes well:

1. The [user](../GLOSSARY.md#user) empties the [trash](GLOSSARY.md#trash) of a [mailbox](GLOSSARY.md#mailbox) themselves. The [product](../GLOSSARY.md#product) asks first, names the mailbox and how many [mails](GLOSSARY.md#mail) it deletes, and empties nothing unless they confirm.
2. What was done no longer lists the mails it deleted.

### Keeping a mailbox

When it goes well:

1. Each [mailbox](GLOSSARY.md#mailbox) has its options in one place: its name, its connection, whether its [aliases](GLOSSARY.md#alias) show apart, and its removal.
2. The [product](../GLOSSARY.md#product) says whether each mailbox is connected, and whether the [planning](GLOSSARY.md#planning) is.
3. The [user](../GLOSSARY.md#user) removes a mailbox after a confirmation.

## How Deckle answers, a sample on two lines

### 1.3.1 Already sorted when they look

1. The [product](../GLOSSARY.md#product) [sorts](GLOSSARY.md#sort) each [mail](GLOSSARY.md#mail) once, as it arrives, and keeps what it found: where the mail goes, its sentence, its [deadline](GLOSSARY.md#deadline) and its [matter](GLOSSARY.md#matter).
2. When a [mailbox](GLOSSARY.md#mailbox) is added, it sorts the recent mail of that mailbox first, then each mail as it arrives.
3. Opening the [window](../GLOSSARY.md#window) shows what was kept, at once; the product sorts again only a matter that a new mail joins.

### 1.3.9 Nothing that identifies them leaves the machine

1. Only the text of a [mail](GLOSSARY.md#mail) leaves the machine to be [sorted](GLOSSARY.md#sort), cut to what the sorting needs; a file attached to it never does.
2. Before the text leaves, the [product](../GLOSSARY.md#product) replaces in it every value that identifies the [user](../GLOSSARY.md#user).
3. What comes back is shown with those values put back, so that the user reads them whole.
