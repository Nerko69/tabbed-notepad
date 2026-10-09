# Tabbed Notepad – User Guide

Tabbed Notepad is a simple notepad with **named, colored tabs**: one tab per project, so you
never have to scroll through one long file to find the right place to write. Everything you type
is **saved automatically**.

- [Getting started](#getting-started)
- [Tabs](#tabs)
- [Writing](#writing)
- [Searching](#searching)
- [Files and folders](#files-and-folders)
- [Keyboard shortcuts](#keyboard-shortcuts)

## Getting started

Download [`TabbedNotepad.exe`](../download/TabbedNotepad.exe) and double-click it. Nothing to
install; it runs on Windows 10 and 11.

> The first time, Windows SmartScreen may say *"Windows protected your PC"* because the app isn't
> code-signed. Click **More info → Run anyway**.

![The main window: colored tabs in two rows, the search box, the move arrows and "+ New Tab"](images/01-main.png)

Across the top, from left to right:

- **Menus:** File, Edit, Format and Help.
- **Search box:** search this tab or all tabs.
- **◄ ►:** move the current tab left or right.
- **+ New Tab:** add a tab.

At the bottom, the **status bar** shows when your notes were last saved, **where they are saved**
(click it to open that folder) and the cursor position.

## Tabs

| To… | Do this |
|---|---|
| Add a tab | Click **+ New Tab** or press **Ctrl+T**, then type a name (e.g. a project name) |
| Rename a tab | Double-click it, or press **F2** |
| Switch tabs | Click a tab, **Ctrl+Tab** / **Ctrl+Shift+Tab**, or **Ctrl+1 … Ctrl+9** |
| Move a tab | Drag it, or use the **◄ ►** buttons (**Ctrl+Shift+Page Up / Page Down**) |
| Change a tab's color | Right-click the tab → **Tab Color** |
| Close a tab | **Ctrl+W**, middle-click the tab, or right-click → **Close Tab** |

**Colors.** Every new tab gets a random color, different from the tabs next to it. To change it,
right-click the tab → **Tab Color**. Pick a color, **Random Color**, or **Custom Color…** for any
color you like. The same menu is under **Format → Tab Color**.

![Right-click a tab to rename it, change its color, move it or close it](images/05-tab-color.png)

**Rows.** When there are more tabs than fit across the window, they wrap onto more rows, so every
tab stays visible. If you prefer a single row, turn off **Format → Tabs in Multiple Rows**. The
row then scrolls with the small arrows at its right end.

![Format → Tabs in Multiple Rows turned off: one row of tabs that scrolls](images/08-single-row.png)

**Closing a tab never deletes your text.** It is moved to the `Closed tabs` folder inside your
notes folder, so you can get it back.

## Writing

- **Saving is automatic.** A moment after you stop typing, the tab is saved. **Ctrl+S** saves
  immediately if you want to be sure.
- **F5** inserts the current time and date, handy for a daily log.
- **Undo / Redo:** **Ctrl+Z** and **Ctrl+Y**, as many steps back as you need.
- **Pasting** always pastes plain text, so text copied from a web page or Word doesn't bring its
  fonts and colors along.
- **Links** (`https://…`, `www.…`) are underlined. **Click** one to open it in your web browser.
  **Hover** over one and a small **copy icon** appears at its end: click it to copy the link,
  without having to select it. You can also right-click a link → **Copy Link**.

![Hovering over a link shows the copy icon at its end](images/04-link-copy.png)

## Searching

**The search box** (top right, or **Ctrl+F**):

1. Choose **This tab** or **All tabs** in the box next to it.
2. Type what you are looking for. Every match is highlighted in yellow, and the number of matches
   is shown (e.g. *7 matches in 4 tabs*).
3. Press **Enter** to jump to the next match (*2 of 7*), **Shift+Enter** for the previous one.
   **Esc** clears the search.

![Searching all tabs for "client": every match is highlighted and the count shows "2 of 7"](images/02-search.png)

**The Find dialog** (**Edit → Find…** or **Ctrl+Shift+F**) adds **Match case**. It also has
**Select All**, which highlights every match and shows how many there are, in this tab or in all
tabs. **F3** / **Shift+F3** find the next / previous match.

![Find dialog: Select All highlights every match and shows the count](images/03-find-select-all.png)

## Files and folders

![The File menu](images/06-file-menu.png)

**Where are my notes?** Each tab is saved as a normal `.txt` file named after the tab
(`Project Alpha.txt`, …) in your **notes folder**: `Documents\TabbedNotepad`, unless you choose
another one. The status bar always shows it, and **File → Show Notes Folder** opens it. You can
read your notes with any editor, even without this app. To back them up, copy that folder.

| Menu item | What it does |
|---|---|
| **Open Text File…** (Ctrl+O) | Opens one or more `.txt` files from anywhere. Each opens in a new tab, or in the current tab if that one is empty. |
| **Open Folder…** (Ctrl+Shift+O) | Switches to the notes in another folder. You can also open a folder of ordinary `.txt` files: each file becomes a tab. |
| **Save** (Ctrl+S) | Saves everything now (it's automatic anyway). |
| **Save All Tabs As…** (Ctrl+Shift+S) | Like *Save As*, but for all tabs: pick a folder, and all tabs are saved there and keep being saved there. The old folder is left as a backup. |
| **Save a Copy of This Tab As…** | Saves a copy of the current tab as a `.txt` file wherever you like. |
| **Save a Copy in Notes Folder** | For a tab opened with *Open Text File*: adds a copy of it as a normal tab in your notes folder. |

**Text files opened with Open Text File** stay where they are. Their tab is shown in *italics*,
the status bar shows the file's location, and your changes are saved back into that same file,
in the format it had. If another program changes the file while it's open, you are asked which
version to keep.

![A text file opened from another folder: its tab is in italics and the status bar shows where it is saved](images/07-text-file-tab.png)

## Keyboard shortcuts

| Keys | Action |
|---|---|
| Ctrl+T | New tab |
| F2 | Rename tab |
| Ctrl+W | Close tab |
| Ctrl+Tab / Ctrl+Shift+Tab | Next / previous tab |
| Ctrl+1 … Ctrl+9 | Go to tab 1 … 9 (Ctrl+9: last tab) |
| Ctrl+Shift+Page Up / Page Down | Move tab left / right |
| Ctrl+Z / Ctrl+Y | Undo / Redo |
| F5 | Insert time and date |
| Ctrl+F | Search box |
| Ctrl+Shift+F | Find dialog |
| F3 / Shift+F3 | Next / previous match |
| Ctrl+O | Open text file |
| Ctrl+Shift+O | Open folder |
| Ctrl+S | Save |
| Ctrl+Shift+S | Save all tabs as… |
| F1 | This guide |
