# Installation reference

Back to the [README](../README.md).

No admin rights are needed: everything is per user. **RoboCopyTo.exe is its own installer.**

## Double-click to install

1. Copy the `publish` folder (`RoboCopyTo.exe` and its five DLLs) to the target machine, anywhere (Downloads is fine).
2. Double-click `RoboCopyTo.exe` and answer **Yes** to "Install RoboCopyTo for your user account?"

It copies the exe and DLLs to `%LocalAppData%\Programs\RoboCopyTo\`, removes the "downloaded from the internet" mark so SmartScreen does not block later launches, and adds **RoboCopyTo...** to the context menu. Right-click any file or folder and choose **Show more options → RoboCopyTo...**

If SmartScreen blocks that first double-click ("Windows protected your PC"), choose **More info → Run anyway**; the exe is not code-signed.

To **update** to a newer build: double-click the installed copy (or the new one) to remove the old version, then double-click the new `RoboCopyTo.exe` to install it.

## By hand (without the double-click installer)

1. Copy everything in the `publish` folder (`RoboCopyTo.exe` and the five DLLs) to `%LocalAppData%\Programs\RoboCopyTo\`.
2. From that folder, run:
   ```powershell
   .\RoboCopyTo.exe --register
   ```
3. Right-click any file or folder and choose **Show more options → RoboCopyTo...**

Registration writes only to `HKCU`, so no admin rights are needed. If you move the folder, run `--register` again from the new location; registration always uses the current exe path. Always move the exe together with its DLLs.

## Command-line switches

| Switch | Effect |
|---|---|
| `--register` | Creates the two context-menu keys pointing at this exe. |
| `--unregister` | Deletes exactly those two key trees and nothing else. |
| `--status` | Prints whether each key exists and which exe it points to (exit code 0 if both exist, 1 otherwise). |
| *(no arguments)* | Double-click: install for the current user, or remove if already installed (asks first). |
| `<path>` | Opens the dialog for that file or folder. This is what Explorer runs. |
| `--job <file> <sha256>` | Internal: used by the elevated relaunch (see [Elevation](USAGE.md#elevation)). |

RoboCopyTo.exe is a Windows (GUI) program, so shells do not wait for it. To see `--status` output before the next prompt and get its exit code, wait for it explicitly:

```powershell
(Start-Process .\RoboCopyTo.exe -ArgumentList '--status' -NoNewWindow -Wait -PassThru).ExitCode
```

```bat
start /wait RoboCopyTo.exe --status & echo %ERRORLEVEL%
```

The registry entries:

```
HKCU\Software\Classes\*\shell\RoboCopyTo            (files)
HKCU\Software\Classes\Directory\shell\RoboCopyTo    (folders)
    (Default)        = RoboCopyTo...
    Icon             = <exe path>
    MultiSelectModel = Player
    command\(Default) = "<exe path>" "%1"
```

`MultiSelectModel = Player` is required. Without it, Explorer hides the item when more than 15 items are selected.

## Uninstalling

Double-click `RoboCopyTo.exe` again (the installed copy in `%LocalAppData%\Programs\RoboCopyTo\`, or any other copy). Because RoboCopyTo is already installed, it asks **"Remove it and its context-menu entry?"**, then whether to also delete your logs, presets and settings (default: keep them). When the installed copy removes itself, the files disappear a moment after it closes.

By hand: run `RoboCopyTo.exe --unregister`, delete `%LocalAppData%\Programs\RoboCopyTo\`, and optionally delete `%LocalAppData%\RoboCopyTo\` (logs, temp) and `%AppData%\RoboCopyTo\` (presets, settings).

## Everything the app writes

| Location | Contents |
|---|---|
| `%LocalAppData%\Programs\RoboCopyTo\` | The installed exe and its five DLLs (written by the double-click installer) |
| `HKCU\Software\Classes\*\shell\RoboCopyTo` | File context-menu entry |
| `HKCU\Software\Classes\Directory\shell\RoboCopyTo` | Folder context-menu entry |
| `%AppData%\RoboCopyTo\presets.json` | Custom presets |
| `%AppData%\RoboCopyTo\settings.json` | Recent destinations, default and last-used preset |
| `%LocalAppData%\RoboCopyTo\Logs\` | Run logs, kept 30 days |
| `%LocalAppData%\RoboCopyTo\Temp\` | Elevation job files, deleted after use |

The app writes nothing outside these locations, apart from the copies you ask for.
