# Custom commands on the wheel

## Why

The wheel is already the fastest way to act on something you're dragging. The user wants it to do more than
convert: hold a project folder, shake, and open it in Ghostty (plain, or with Claude Code running); hold a link
from the browser, shake, and save the whole page as an image or send it to Keep for later (not a bookmark, since
these are rarely revisited). Lemon shouldn't know about any of these tools. The user writes the command, and Lemon
runs it with whatever was dropped.

## What it is

- A new **Commands** ring on the wheel. Each wedge is a command the user set up in Settings.
- Each command has a **name** (the wedge label), what it **applies to** (*Folders* or *Links*), and a **command
  line**, a line of shell such as `open -na Ghostty --args --working-directory={path}`.
- When you drop on a wedge, Lemon runs the line once for each dropped item. `{path}` becomes the folder's path and
  `{url}` becomes the link, both shell-quoted.
- The ring shows only the commands that fit what you're holding. A folder still gets its usual conversion rings for
  the files inside it, and Commands is one more ring. A link only has the Commands ring.
- **Links are new.** On Mac, Lemon reads only file URLs from the drag today, so a link drag never opens the wheel.
  On Windows the wheel opens for a link, but nothing happens when you drop it.

## Goals

- Set up, edit, reorder and delete commands in Settings, next to the user's own rings.
- Folder and link drags open the wheel when at least one command fits, even if there's nothing to convert.
- A command that fails (non-zero exit, or it won't start) tells the user, with its error output, instead of
  failing silently.

## Non-goals

- Built-in actions like "save page as image" or "save to Keep". Those are the user's own CLI tools.
- Commands for individual file types (e.g. `.pdf`). Leave it out until someone asks; the "applies to" field can
  grow later.
- Showing a command's output, or a progress view for long-running commands.

## Anti-goals

- Blocking the wheel or the app while a command runs. Commands run in the background and Lemon doesn't wait for
  them.
- Paths or links with spaces, quotes or `$` breaking or injecting into the command. They are always quoted.

## Decisions made (my calls, flag any you disagree with)

- **Mac first.** The user's tools (Ghostty, Claude Code) are on the Mac. Windows gets a **To do** entry in
  `PARITY-LOG.md`.
- **Shell:** the line runs as `/bin/zsh -lc "<line>"` (a login shell) so Homebrew tools and `claude` are on `PATH`,
  with the folder (or, for a link, the home folder) as the working directory.
- **Several items dropped:** the line runs once per item.
- **Links:** read from the drag pasteboard as `http(s)` URLs that aren't file URLs. A drag that has both files and
  a link (some browsers attach a `.webloc`) counts as a link.
- **The Commands ring** is one more ring in the existing ring order and hide/show settings. A new Mac `WheelMode`
  for it, with the user's commands as its wedges.
- **Errors:** a failure shows a notice with the command's name and the last lines of its error output. It goes
  through whatever the app already uses for conversion errors.
- **No starter commands.** The editor shows the Ghostty / Claude examples as hint text. `DETAIL.md` lists them:
  - Terminal: `open -na Ghostty --args --working-directory={path}`
  - Claude: `open -na Ghostty --args --working-directory={path} -e claude`

## Left to the builder

- The exact Settings layout for the command editor (follow the custom ring editor's look).
- The Commands ring's SF Symbol, and its emoji or icon in the hub.
- Updating `README.md` / `DETAIL.md`, the parity log, and building `dist/MacOS/Lemon.app`.
