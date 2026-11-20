# Move between the places of any Deckle window through a navigation you keep open or closed as you choose

## Need

Draft, not confirmed

**Context of use.** The user works in several Deckle windows during a day: Settings, Mail, Projects, Folders. Each window lists its places in a navigation on its left. The user resizes these windows to fit beside their other work, from wide to very narrow, and moves from one window to another often.

**User need.** The user needs every window to keep its navigation the way they set it, as long as the window has room for it, in order to move between places without the navigation hiding the page or changing on its own.

**Intended outcome.**

- The navigation never covers the page while the window has room to show it beside the page.
- The user decides whether the navigation is open or closed, and it stays as they left it.
- Resizing the window does not undo their choice.
- In a window too narrow for anything beside the page, the page keeps the whole width, and the places are still one gesture away.
- Every window does this the same way, so the user learns it once.

## Journey

| The user wants to | The product must |
| --- | --- |
| see where they can go | show the places of the window, with their names when there is room |
| give the page more room | let them close the navigation to its icons, and keep it so |
| read the names again | let them open it beside the page, and keep it so |
| go to another place | take them there and leave the navigation as it was |
| make the window narrow | give the page the whole width, and keep the places one gesture away |
| widen the window again | bring the navigation back as they had left it |

When it goes another way:

- The window has nothing to list yet: there is no navigation and no button for it.
- The user opens the navigation in a very narrow window: it covers the page until they choose a place or click elsewhere, and their choice for a wider window does not change.

## Answer

Words a reader needs:

- The **navigation** is the list of a window's places, on its left.
- The **rail** is the navigation closed: a column of icons beside the page, one per place.
- The **open navigation** shows each place's icon and name beside the page.
- The **navigation button** is the button with three lines at the left of the title bar.
- A **narrow window** is a window too narrow to hold the rail beside the page.

### A window wide enough for the rail

1. The navigation is beside the page, as the rail or open. It never covers the page.
2. The navigation button switches it between the rail and open. Opening it makes the page narrower; closing it gives the page that room back.
3. Choosing a place, from the rail or from the open navigation, shows its page and leaves the navigation as it was.
4. A place that holds others, in the rail, shows them as a menu beside its icon.
5. Resizing the window, as long as it stays wide enough for the rail, keeps the navigation as the user left it.

### A narrow window

1. Nothing of the navigation shows: the page has the whole width. The navigation button stays in the title bar.
2. The navigation button opens the navigation over the page. Choosing a place, clicking on the page or pressing the button again closes it.
3. What the user does here does not change what they chose for a wider window: once the window is wide enough again, the navigation comes back as the rail or open, as they had left it.

### Opening a window

1. A window opens with its navigation as the user left it last time, while Deckle runs.
2. The first time, the window's own spec says whether it opens as the rail or open. When it says nothing, it opens open.

### Rules

- Every Deckle window with a navigation follows this spec: Settings, Mail, Projects and Folders. A window's own spec adds what its navigation lists, never another way to open or close it.
- A window whose steps follow one order, such as the setup wizard, keeps its list open and is not covered here.
- The width from which a window counts as narrow is kept in the code, per window when its page needs more room.

## Limits

- The width of the open navigation is each window's: Settings fits it to its longest name.
- The user cannot drag the edge of the navigation to resize it.

## Open

1. **After Deckle restarts.** Options: the navigation opens as the user left it before the restart, or as the window's spec says for the first time. Built today: as the window's spec says; the choice is kept while Deckle runs only. Depends on it: where the choice is written.
