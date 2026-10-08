# Usage and switch reference

Back to the [README](../README.md).



## Selected items and destination

Each selected **folder** becomes its own robocopy call, landing at `<destination>\<folder name>`. Robocopy copies a folder's *contents*, so the name is appended for you. **Loose files** are grouped by parent folder into one call per folder, landing directly in `<destination>`. Duplicates, and items inside another selected folder, are dropped. Use the ✕ button to remove an item.

The **Recent** list holds your 10 most recent destinations.

## Presets

| Built-in preset | Options |
|---|---|
| Quick copy | `/E /MT:8`, defaults otherwise |
| Mirror backup | `/MIR /MT:8` |
| Large files over network | `/E /Z /MT:4`, Network-friendly on |

Built-ins cannot be changed or deleted, but **Save as** copies one. Switching presets while *(modified)* is showing asks before discarding your changes. If `presets.json` or `settings.json` cannot be read, the dialog says so and the file is kept as `<name>.bad-<timestamp>` rather than overwritten. **Save** updates a custom preset, **Delete** removes it, and **Revert** discards changes. When the options differ from the selected preset, *(modified)* appears. Tick **Default** to open the dialog with that preset; without a default, the last-used preset opens. Presets store options only, never sources or a destination.

## Safety checks (run on Start and Dry run)

| Check | Result |
|---|---|
| Destination is inside a selected source folder | Blocked |
| Destination equals a source, or a folder would be copied onto itself | Blocked |
| `/MIR` or `/PURGE` (including in Extra arguments, in any spelling robocopy accepts: `"/MIR"`, `-mir`) with any loose file selected | Blocked |
| Mirror whose target folder contains the source (e.g. `C:\Proj\App\App` → `C:\Proj`) | Blocked: mirror would delete the source |
| Two selected items land at the same destination path (same-named folders or files) | Blocked with Mirror; otherwise a warning that they will overwrite each other |
| A robocopy command line longer than Windows allows (huge exclude lists or Extra arguments) | Blocked |
| Destination is empty or invalid | Blocked |
| A drive root (e.g. `E:\`) selected as a source | Blocked |
| `/MIR` or `/PURGE` used at all | Confirm, listing the exact folders whose extra files will be deleted |
| `/MOV` or `/MOVE` (any spelling) in Extra arguments | Confirm: source files will be deleted |
| Free space at the destination is less than the scanned total | Warning |
| Destination folder does not exist | Offer to create it (Dry run never creates it) |

Paths are compared after resolving mapped drives to UNC and removing `\\?\` prefixes, so `Z:\x` and `\\server\share\x` count as the same folder.

## Running, results, and logs

- A pre-scan counts files and bytes (honoring the exclude patterns) so progress can be shown against a total.
- Calls run one after another, each shown as pending, running, done, failed, or cancelled.
- **Cancel** kills the robocopy process tree. Without `/Z`, a partially copied file may remain, and the results screen says so.
- **Retry failed** reruns the same calls; robocopy skips files that were already copied.
- The output panel keeps the last 5,000 lines and follows new output unless you scroll up to read.
- If something unexpected stops a run (for example a network share disappearing during the scan), the results screen shows the error instead of leaving the window on the progress screen.
- **Dry run** runs the same calls with `/L` and is labeled "Dry run: nothing was copied".
- Each run writes one log: `%LocalAppData%\RoboCopyTo\Logs\yyyy-MM-dd_HHmmss.log` (`_dryrun` suffix for dry runs). The app writes a UTF-16LE header with date, version, sources, destination, and every exact command line, and robocopy appends via `/UNILOG+`. Logs older than 30 days are deleted at launch.
- Exit codes are translated to plain language. Values below 8 are never reported as errors.

| Exit code bit | Meaning shown |
|---|---|
| 0 (no bits) | Already up to date |
| 1 | Files copied |
| 2 | Destination has extra items (not removed); with Mirror: *Extra items at the destination were removed* |
| 4 | Some items mismatched; check the log |
| 8 | Some items failed after retries |
| 16 | Robocopy could not run; check the log |

In a dry run, bits 1 and 2 read "would be copied" / "would be removed", because nothing was actually changed.

## Elevation

Copy owner, Copy auditing info, Backup mode (`/B`), and Restartable with backup fallback (`/ZB`) need administrator rights. They show a shield, as does **Start** when any of them is ticked. On Start, RoboCopyTo:

1. writes the job to `%LocalAppData%\RoboCopyTo\Temp`, with mapped drive letters converted to UNC paths (elevated processes usually cannot see your mapped drives);
2. relaunches itself with Windows' UAC prompt, passing the job file and its SHA-256 hash on the command line. The elevated process refuses a job file whose contents changed, or that is not a `job-<id>.json` file directly inside a `RoboCopyTo\Temp` folder free of junctions and links;
3. closes the original window once the elevated one starts. The elevated window shows "Running as administrator" in its title, deletes the job file after reading it, and starts the copy.

If you decline the UAC prompt, the dialog stays open and says so. If a different administrator account approves the prompt (a standard user typing an admin's password), the job is still found in your profile, but that run's log, settings, and recent destinations are written to the administrator's profile.

## Keyboard

Alt access keys: **S**tart, **D**ry run, **B**rowse, Save **a**s, Re**v**ert, Cop**y** commands, and one letter per option (underlined when you hold Alt). Esc closes the options and results screens. On the results screen: **B**ack to options, **O**pen log, **R**etry failed; while running, Alt+**C** cancels.

---

## Every switch the dialog can emit

Switches are always emitted in this order, so the preview reads consistently. The app-required switches are dimmed in the preview.

| # | Group | Switch | Comes from |
|---|---|---|---|
| 1 | Copy scope | `/E` | **Include subfolders** (default on; ticked and locked while Mirror is on) |
| | | `/MIR` | **Mirror**: makes each destination folder an exact copy, deleting extras. Disabled while loose files are selected. Replaces `/E`. |
| 2 | What to copy | `/COPY:DAT` | Always (robocopy's default, written out). |
| | | `/COPY:DATS` | **Copy permissions** adds `S` |
| | | `…O` | **Copy owner** adds `O` (needs elevation) |
| | | `…U` | **Copy auditing info** adds `U` (needs elevation) |
| | | `/DCOPY:DAT` | **Keep folder timestamps** (default on; off means robocopy's own default, `/DCOPY:DA`) |
| 3 | Copy mode | `/Z` | **Restartable**: resume interrupted large files |
| | | `/ZB` | **Restartable with backup fallback** (needs elevation; replaces `/Z`, so the two cannot both be ticked) |
| | | `/B` | **Backup mode** (needs elevation) |
| | | `/J` | **Unbuffered I/O**: faster for very large files. Verified to work together with `/Z`. |
| | | `/MT:n` | **Multithreaded** with thread count *n* (default on, 8; range 1–128) |
| 4 | Existing files | *(none)* | **Copy if different** (default): skip files with the same size and timestamp |
| | | `/XO` | **Only copy newer**: never overwrite a newer destination file with an older one |
| | | `/XC /XN /XO` | **Skip existing**: only copy files not yet at the destination |
| | | `/IS /IT /IM` | **Always overwrite**: copy everything, including identical files (see note) |
| | | `/FFT` | Added automatically when the destination is FAT32 or exFAT (2-second timestamps); the dialog shows a note |
| 5 | Filters | `/XF …` | **Exclude files**: space-separated names or wildcards (quote names with spaces; a trailing `\` is dropped so it cannot break the quoting) |
| | | `/XD …` | **Exclude folders**: space-separated names or paths |
| | | `/XJ` | **Skip junctions** (default off) |
| 6 | Retries | `/R:n /W:n` | **Retry count** and **Wait seconds** (default `/R:3 /W:5`) |
| | | `/R:10 /W:15 /TBD` | **Network-friendly**: replaces the retry values; turning it off restores yours |
| 7 | App-required | `/BYTES /FP /NP /TEE /UNILOG+:"<log>"` | Always. Exact sizes and full paths for the progress bar, console output, and the run log |
| 8 | Dry run | `/L` | **Dry run** only |
| 9 | Extra | *anything* | **Extra arguments**: appended verbatim to every call; safety checks still apply. Loose-file calls never receive `/E`, `/S`, `/MIR` or `/PURGE` (in any spelling, e.g. `-mir`), even from here, because with `/E` robocopy would also copy same-named files from subfolders. `/XF`, `/XD`, `/XJ*` and `/LEV:n` typed here are also honored by the pre-scan. |

**Path quoting** (verified against real robocopy, and locked in by tests): every path is quoted; trailing backslashes are removed (a trailing `\` before a closing quote escapes the quote); a drive root is written `"E:\\"`; a UNC share root is written `"\\server\share"`. The argument string is built by hand, so the preview is exactly what runs. Loose-file calls whose command line would exceed 30,000 characters are split across several calls.

## Notes on choices made during implementation

- **`/Z` with `/J`**: checked with `robocopy /?` and a real copy. Robocopy accepts both together, so they are not mutually exclusive.
- **Always overwrite adds `/IM`**: current Windows 11 robocopy has a "modified" file class (same size and timestamp, different NTFS change time) that `/IS /IT` does not copy. `/IM` includes it, so the mode really copies everything.
- **Output panel text comes from the log**: robocopy always writes redirected stdout in the OEM code page (437 on US systems), so names such as `日本語.txt` arrive as `???.txt`. Its `/UNICODE` stdout output is unusable. Progress is still parsed live from stdout, but the output panel and the failed-file list read the run's UTF-16 log as robocopy writes it, so every name displays correctly.
- **Log header first**: when `/UNILOG+` targets a file that does not exist yet, robocopy writes 8-bit text. RoboCopyTo always writes its UTF-16LE header (with BOM) first, so the combined file is consistent UTF-16.
