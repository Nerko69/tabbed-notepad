# Tabbed Notepad

A simple Windows notepad with **named tabs** — one tab per project, so you never have to
scroll through one long file to find the right place to write.

Everything is **saved automatically** as you type. There is no "Save" prompt, and your tabs,
their names and their text are all back when you open the app again.

## Download and run

1. Download [`download/TabbedNotepad.exe`](download/TabbedNotepad.exe)
   (on GitHub, open the file and click the **Download raw file** button).
2. Put it anywhere you like, e.g. your Desktop or `C:\Tools`, and double-click it.

No installation needed. It runs on Windows 10 and 11, which already include .NET Framework 4.8.

> Windows SmartScreen may say *"Windows protected your PC"* the first time because the
> .exe is not code-signed. Click **More info → Run anyway**.
> Tip: right-click the .exe → **Pin to taskbar** or **Pin to Start**.

## How to use it

| What | How |
|---|---|
| New tab | **Ctrl+T**, or click **+ New Tab** (top right) — you'll be asked for a name |
| Rename a tab | Double-click the tab, or **F2** |
| Close a tab | **Ctrl+W**, middle-click the tab, or right-click → Close Tab |
| Switch tabs | Click, **Ctrl+Tab** / **Ctrl+Shift+Tab**, or **Ctrl+1 … Ctrl+9** |
| Reorder tabs | Drag a tab left or right, or right-click → Move Left / Move Right |
| Insert time and date | **F5** (like Notepad) — handy for a daily log |
| Find | **Ctrl+F**, then **F3** for the next match. "Search all tabs" looks through every tab |
| Word wrap / font | **Format** menu |
| Save | Automatic as you type. **Ctrl+S** saves immediately if you want peace of mind |
| Save all tabs somewhere else | **File → Save All Tabs As…** (**Ctrl+Shift+S**) — see below |
| Open notes from another folder | **File → Open…** (**Ctrl+O**) |
| Save one tab as a separate .txt file | **File → Save This Tab As Text File…** |

## Where are my notes?

The bottom of the window always shows the **notes folder**. Click it to open the folder in
Explorer. By default it is `Documents\TabbedNotepad`.

- Each tab is a plain `.txt` file named after the tab (`Project A.txt`, `Client B.txt`, …), so
  your notes are readable with any editor even without this app. Renaming a tab renames its file.
- `tabs.ini` remembers the tab order, the window position and the font.
- Closing a tab never deletes its text: it is moved to the `Closed tabs` subfolder.

### Saving to a different place: Save All Tabs As

**File → Save All Tabs As…** works like *Save As* in Notepad, but for all tabs at once:

1. Pick a folder, for example a new folder on your Desktop, in OneDrive, or on a USB stick.
2. All tabs are saved there, one `.txt` file per tab.
3. From then on the app keeps saving to that folder, and it opens that folder the next time you start it.

The previous folder is left as it was, so it stays behind as a backup copy. If the folder you pick
already contains files with the same names, the app asks before replacing anything.

**File → Open…** switches to the notes in another folder. You can also open any folder of
ordinary `.txt` files, and each file becomes a tab.

To back up your notes, copy the notes folder. Only one copy of the app runs at a time;
starting it again brings the open window to the front.

## Building from source

The project is a small C# WinForms app in [`src/`](src) targeting .NET Framework 4.8.

```
dotnet build src/TabbedNotepad.csproj -c Release
```

The .exe ends up in `src/bin/Release/net48/`. The GitHub Actions workflow in
`.github/workflows/build.yml` builds it on Windows for every push and attaches it to the run
as a downloadable artifact.
