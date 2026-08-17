# Behavior Specification

Technology-neutral rules both desktop and mobile must obey.

1. Typed input that looks like a host (a dot, no spaces) navigates over HTTPS. Everything else is a search with the selected engine.
2. `aphelion://` URLs are application pages. Unknown hosts are not searched.
3. A blank tab and an internal page are not engine history entries. Back must restore them from a local stack.
4. Pinning a tab moves it to the left of unpinned tabs and keeps that cluster contiguous.
5. A split pair is one tab in every list. Closing either half leaves the other as an ordinary tab.
6. A group is a contiguous run. Dragging the group chip moves the whole run. Dragging a tab into a run joins that group.
7. Private browsing must not write history, session or download records, and must not share cookie storage with ordinary windows.
8. Closing a tab pushes it onto a reopen stack. Ctrl+Shift+T pops the stack.
9. Downloads can be paused, resumed and cancelled while they are live. Clearing the list does not abort live transfers.
10. Theme is user-chosen: system, light or dark. The choice survives restart.
11. Saved passwords stay on the device and are never sent to a page until a later autofill decision says otherwise.
