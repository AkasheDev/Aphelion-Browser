# Privacy Rules

- Ordinary profile data lives in the platform application-data directory (`%APPDATA%\Aphelion`, `~/.config/Aphelion`, `~/Library/Application Support/Aphelion`).
- Private windows use a throwaway engine profile under `private/<id>` in that directory. The folder is deleted when the window closes.
- History, session, bookmarks, downloads, settings and passwords are ordinary-profile files. Private windows do not write them.
- Weather on New Tab uses an approximate public-IP location. It is off by default in private windows and never writes that choice to the shared preference file.
- Search suggestions send the typed query to the selected engine. They can be turned off.
- Saved passwords are stored in the profile directory, readable only by this user on Unix. They are not synced and not injected into pages.
- Cookies in a private window are isolated when the host engine exposes a private profile or non-persistent store; they are also cleared on close as a second line.
- No telemetry is specified. Do not add it without a new privacy decision.
