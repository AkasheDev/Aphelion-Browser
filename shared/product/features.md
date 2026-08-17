# Product Features

Desktop is the reference client. Mobile must match these behaviours without sharing source.

## Tabs and windows

- New tab, close tab, reopen closed tab (stack), pin, mute, duplicate, copy URL.
- Tab groups as a contiguous run with a draggable chip.
- Split view: one visible strip entry, two pages, one toolbar.
- Tear-off and drop onto another window. Tear-off reloads; switching tabs does not.
- Ordinary session restore across every open window. Private windows are not restored.

## Navigation

- Address bar resolves hosts vs search. Internal pages use `aphelion://`.
- Back/Forward include New Tab, Downloads, Settings and History, which the engine never sees.
- Find in page, print, reload. DevTools on Windows.

## Internal pages

- `aphelion://settings` — theme, startup, search engine, New Tab widgets, download folder, default-app settings, update channel, saved passwords (device-local, no autofill).
- `aphelion://history` — visits grouped by day, search, delete, clear. Not recorded in private windows.
- `aphelion://downloads` — live transfers with pause/resume/cancel.

## Privacy

- Private windows use a separate engine profile directory, deleted on close.
- Weather and search suggestions are opt-out New Tab network features.
- History, session and download lists are profile files under the user data directory.

## Chrome

- Command palette (Ctrl+K).
- Site information popover from the address-bar lock.
- Page context menu: back, forward, reload, open/save link, copy/search selection.
- Light, dark or system theme.
