# Move between the places of any Deckle window through a navigation you keep open or closed as you choose

> Not reviewed.

## Need

### Context of use

1. The [user](../GLOSSARY.md#user) works in several [windows](../GLOSSARY.md#window) of the [product](../GLOSSARY.md#product) during a day, and moves from one to another often. Each window lists its [places](../GLOSSARY.md#place) in a [navigation](../GLOSSARY.md#navigation). They resize these windows to fit beside their other work, from wide to very narrow.

### User need

1. The [user](../GLOSSARY.md#user) needs every [window](../GLOSSARY.md#window) to keep its [navigation](../GLOSSARY.md#navigation) the way they set it, as long as the window has room for it, in order to move between [places](../GLOSSARY.md#place) without the navigation hiding the [page](../GLOSSARY.md#page) or changing on its own.

### Intended outcome

1. The [navigation](../GLOSSARY.md#navigation) never covers the [page](../GLOSSARY.md#page) while the [window](../GLOSSARY.md#window) has room to show it beside the page.
2. The [user](../GLOSSARY.md#user) decides whether the navigation is [open](GLOSSARY.md#open-navigation) or [closed](GLOSSARY.md#closed-navigation), and it stays as they left it.
3. Resizing the window does not undo their choice.
4. In a [narrow window](GLOSSARY.md#narrow-window), the page keeps the whole width, and the [places](../GLOSSARY.md#place) are still one [gesture](../GLOSSARY.md#gesture) away.
5. Every window does this the same way, so the user learns it once.

## Journey

| The user wants to | The product must |
| --- | --- |
| see where they can go | show the [places](../GLOSSARY.md#place) of the [window](../GLOSSARY.md#window), with their names when there is room |
| give the [page](../GLOSSARY.md#page) more room | let them [close](GLOSSARY.md#closed-navigation) the [navigation](../GLOSSARY.md#navigation) to its icons, and keep it so |
| read the names again | let them [open](GLOSSARY.md#open-navigation) it beside the page, and keep it so |
| go to another place | take them there and leave the navigation as it was |
| make the window narrow | give the page the whole width, and keep the places one [gesture](../GLOSSARY.md#gesture) away |
| widen the window again | bring the navigation back as they had left it |

## Rules

1. [What the product shows is its real state.](../deckle.rules.md)
2. [Everything the product tells the user reaches them with animations off as well.](../deckle.rules.md)
3. [Everything the product tells the user reaches them with colours off as well.](../deckle.rules.md)
4. Every [window](../GLOSSARY.md#window) with a [navigation](../GLOSSARY.md#navigation) opens, closes and keeps it in the same way; only its [places](../GLOSSARY.md#place) differ.
5. A window whose places are steps in one order shows them all, with their names, at all times.
6. The [user](../GLOSSARY.md#user) does not change the width of the navigation.

## Workflow

### Opening a window

When it goes well:

1. The first time a [window](../GLOSSARY.md#window) opens, its [navigation](../GLOSSARY.md#navigation) is [open](GLOSSARY.md#open-navigation), unless that window's own spec says it starts [closed](GLOSSARY.md#closed-navigation).
2. While the [product](../GLOSSARY.md#product) runs, a window that opens again shows its navigation as the [user](../GLOSSARY.md#user) last left it.

Otherwise:

3. The window has no [place](../GLOSSARY.md#place) to list yet: it shows no navigation, and nothing to open one.

### Opening and closing the navigation

When it goes well:

1. One [gesture](../GLOSSARY.md#gesture) switches the [navigation](../GLOSSARY.md#navigation) between [open](GLOSSARY.md#open-navigation) and [closed](GLOSSARY.md#closed-navigation), beside the [page](../GLOSSARY.md#page). It never covers the page.
2. Opening it makes the page narrower; closing it gives the page that room back.
3. The navigation stays as the [user](../GLOSSARY.md#user) left it until they switch it again.

### Going to a place

When it goes well:

1. Choosing a [place](../GLOSSARY.md#place), whether the [navigation](../GLOSSARY.md#navigation) is [open](GLOSSARY.md#open-navigation) or [closed](GLOSSARY.md#closed-navigation), shows its [page](../GLOSSARY.md#page) and leaves the navigation as it was.
2. While the navigation is closed, a place that holds others offers them beside its icon, on one [gesture](../GLOSSARY.md#gesture), without opening the navigation.

### Resizing the window

When it goes well:

1. As long as the [window](../GLOSSARY.md#window) is not a [narrow window](GLOSSARY.md#narrow-window), resizing it keeps the [navigation](../GLOSSARY.md#navigation) as the [user](../GLOSSARY.md#user) left it.
2. Once the window is wide enough again after being narrow, the navigation comes back [open](GLOSSARY.md#open-navigation) or [closed](GLOSSARY.md#closed-navigation), as they had left it.

### In a narrow window

When it goes well:

1. Nothing of the [navigation](../GLOSSARY.md#navigation) shows: the [page](../GLOSSARY.md#page) has the whole width, and one [gesture](../GLOSSARY.md#gesture) still opens the navigation.
2. Opened here, the navigation covers the page until the [user](../GLOSSARY.md#user) chooses a [place](../GLOSSARY.md#place), clicks elsewhere or makes the same gesture again.
3. What they do here does not change what they chose for a wider [window](../GLOSSARY.md#window).

## How Deckle answers, a sample on two lines

### 1.3.3 Resizing the window does not undo their choice

1. The [product](../GLOSSARY.md#product) keeps, for each [window](../GLOSSARY.md#window), whether the [user](../GLOSSARY.md#user) left its [navigation](../GLOSSARY.md#navigation) [open](GLOSSARY.md#open-navigation) or [closed](GLOSSARY.md#closed-navigation), apart from what the window shows at its present width.
2. At each change of width, the product compares the width with the room the closed navigation needs beside the [page](../GLOSSARY.md#page), and shows the navigation as the user left it when it fits.
3. Only a [gesture](../GLOSSARY.md#gesture) of the user changes the choice the product keeps; a change of width never does.

### 1.3.4 The page keeps the whole width

1. When the closed [navigation](../GLOSSARY.md#navigation) no longer fits beside the [page](../GLOSSARY.md#page), the [product](../GLOSSARY.md#product) hides the navigation and gives its width to the page; what opens the navigation stays in view.
2. Opened there, the navigation lies over the page, and the product closes it as soon as the [user](../GLOSSARY.md#user) chooses a [place](../GLOSSARY.md#place), clicks elsewhere or makes the [gesture](../GLOSSARY.md#gesture) again.
3. Opening it there does not change the choice the product keeps for a wider [window](../GLOSSARY.md#window).
