# Browse the web in a window of your own, with your sites at hand and the ads gone

> Not reviewed.

## Need

### Context of use

1. The [user](../GLOSSARY.md#user) browses the web every day in another [application](../GLOSSARY.md#application), and it does not satisfy them. They like how it arranges its [tabs](GLOSSARY.md#tab) in a panel on the side and keep a few [pinned sites](GLOSSARY.md#pinned-site) at its top, and want nothing else of it: no spaces, no folders, no extensions. They watch videos on the web, and keep their passwords in another application.

### User need

1. The [user](../GLOSSARY.md#user) needs a simple browser of their own, laid out like the one they leave and dressed like their [system](../GLOSSARY.md#system), in order to browse safely and without ads, with their usual [sites](GLOSSARY.md#site) one [gesture](../GLOSSARY.md#gesture) away.

### Intended outcome

1. The [window](../GLOSSARY.md#window) has the layout of the browser they leave, with the materials of their [system](../GLOSSARY.md#system).
2. Their [pinned sites](GLOSSARY.md#pinned-site) are one [gesture](../GLOSSARY.md#gesture) away and quick to come back to, without costing anything before they first open them.
3. They arrange their [tabs](GLOSSARY.md#tab) in the order they want.
4. [Ads](GLOSSARY.md#ad) do not show.
5. The [address field](GLOSSARY.md#address-field) tells them which [site](GLOSSARY.md#site) they are really on.
6. Little to set: the browser behaves well by itself.
7. Nothing they visit is written anywhere but in what the browser keeps for them.

## Journey

| The user wants to | The product must |
| --- | --- |
| go to a [site](GLOSSARY.md#site) | take an address or words to search in the [address field](GLOSSARY.md#address-field), and show the [web page](GLOSSARY.md#web-page) |
| know where they are | show the site's own domain in the address field |
| open a site they use every day | show their [pinned sites](GLOSSARY.md#pinned-site) as icons at the top of the [navigation](../GLOSSARY.md#navigation), and open one on a click |
| come back to a pinned site | show it again at once, as they left it |
| open several pages | keep each in a [tab](GLOSSARY.md#tab) of the navigation, under « New Tab » |
| put their tabs in order | let them drag a tab to another place in the list |
| find their tabs after a restart | open again the tabs they had |
| listen to something while they do something else | show what plays in a [media card](GLOSSARY.md#media-card), with its controls |
| browse without ads | block the [ads](GLOSSARY.md#ad) of every site |
| give a site more room | let them [close](../GLOSSARY.md#closed-navigation) the navigation, as in every Deckle window |
| use a site as an application of its own | open it in a window of its own, with its own icon |

## Rules

1. [What the product shows is its real state.](../deckle.rules.md)
2. [Everything the product tells the user reaches them with animations off as well.](../deckle.rules.md)
3. [Everything the product tells the user reaches them with colours off as well.](../deckle.rules.md)
4. A [pinned site](GLOSSARY.md#pinned-site) is never loaded before the [user](../GLOSSARY.md#user) opens it.
5. What the user types in the [address field](GLOSSARY.md#address-field) leaves the machine only when they press Enter.
6. The product does not keep passwords and does not fill forms.
7. The product has no spaces, no folders and no extensions.
8. No address and no title of a [web page](GLOSSARY.md#web-page) the user visits is written to the product's log.

## Workflow

### Opening the browser

When it goes well:

1. The [window](../GLOSSARY.md#window) shows, from top to bottom of its [navigation](../GLOSSARY.md#navigation): a row of buttons, the [address field](GLOSSARY.md#address-field), the [pinned sites](GLOSSARY.md#pinned-site), « New Tab » then the [tabs](GLOSSARY.md#tab), the [media card](GLOSSARY.md#media-card) when something plays, and a foot with the settings. The [web page](GLOSSARY.md#web-page) is a card with rounded corners beside it.
2. The tabs the [user](../GLOSSARY.md#user) had when they last closed the window come back.
3. A pinned site shows as its icon only, until they open it.

### Going to a site

When it goes well:

1. The [user](../GLOSSARY.md#user) types an address or words in the [address field](GLOSSARY.md#address-field) and presses Enter: an address opens its [site](GLOSSARY.md#site), words open a search.
2. Once the [web page](GLOSSARY.md#web-page) shows, the field shows the site's own domain alone, such as « example.com ».
3. A new tab is empty until they go somewhere.

### Opening a pinned site

When it goes well:

1. The [pinned sites](GLOSSARY.md#pinned-site) form a grid of icon tiles that fits the width of the [navigation](../GLOSSARY.md#navigation).
2. The first click on one loads it. It then stays open, and showing it again is immediate.
3. While it is hidden it costs almost nothing, unless it plays sound.

### Arranging the tabs

When it goes well:

1. Each [tab](GLOSSARY.md#tab) shows the icon and the title of its [site](GLOSSARY.md#site), and a handle on its edge that says it can be grabbed.
2. The [user](../GLOSSARY.md#user) drags a tab to another place in the list, and it stays there.
3. Closing a tab shows its neighbour.

### Blocking ads

When it goes well:

1. The [ads](GLOSSARY.md#ad) of a [site](GLOSSARY.md#site) do not load and leave no empty frame.

### Opening a site as an application

When it goes well:

1. A [site](GLOSSARY.md#site) opens in a [window](../GLOSSARY.md#window) of its own, with its own icon in the taskbar.
