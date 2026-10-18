# 2ctrl

This project was developed with the assistance of LLMs, including ChatGPT and Codex.

Windows tray prototype: a solo left Ctrl selects English, a solo right Ctrl selects Russian. Each key selects a specific installed layout, rather than cycling through languages. Inspired by the behavior of 2cmd; this is an independent Windows implementation.

## Download

Download `2ctrl-windows.zip` from [GitHub Releases](https://github.com/tinted-knight/2ctrl/releases), extract it, and run `2ctrl.exe`. You can also download `2ctrl.exe` and `2ctrl.exe.config` separately into the same folder. Requires Windows 10 or later with .NET Framework 4.8.

## Build and run

Windows 10 with .NET Framework 4.8 or later. No NuGet packages or .NET SDK required; the build script uses the .NET Framework compiler shipped with Windows.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Test
.\bin\2ctrl.exe
```

Right-click the notification-area icon to choose installed layouts for either Ctrl, pause, toggle **Run on startup**, or exit. Expand the hidden tray icons if necessary. Defaults choose the first installed English and Russian layouts; a missing language remains unconfigured. Install missing layouts through Windows settings, then choose them in the menu. Settings are saved to `%LOCALAPPDATA%\2ctrl\settings.xml`.

**Run on startup** is off by default. Enabling it registers the current executable for the current user's Windows sign-in, without administrator rights; disabling it removes that registration. Keep the application in a permanent folder. If you move it, enable the option again from the new location. The checkbox reflects this copy's registration in `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` (`2ctrl`); Windows Task Manager can separately disable startup execution.

## Publish a release

The version tag is the release version's source of truth: `v1.2.3` embeds file/assembly version `1.2.3.0` and product version `1.2.3` in the EXE. Prerelease tags such as `v1.2.3-beta.1` retain the suffix in the product version. Each numeric component must be between 0 and 65534. Unsupported tags fail the build before publication. Versions are selected when creating tags, not automatically incremented by CI.

Local builds default to product version `0.0.0-dev`; use `./build.ps1 -Test -Version 1.2.3` to specify a version. Build-generated assembly metadata stays in `bin/` and is not committed. The build verifies that the resulting EXE contains the requested version.

After the release workflow and desired changes are pushed to GitHub, push a version tag:

```powershell
git tag v1.0.0
git push origin v1.0.0
```

The `Release` GitHub Actions workflow builds the tagged source on Windows, runs the tests and a startup check, then publishes a GitHub release with `2ctrl.exe`, `2ctrl.exe.config`, and `2ctrl-windows.zip`. Tags containing a hyphen (for example `v1.0.0-beta.1`) create prereleases. Use a new version tag for each release. Publication uses the built-in `GITHUB_TOKEN`; no additional secret is needed.

If a release already exists for the tag, the workflow uploads the built files to it, replacing assets with the same names while preserving the release title and notes. To publish an existing tag using the current workflow, open **Actions → Release → Run workflow** on the default branch and enter the tag (for example `v0.4.0`). This uses the current workflow to build the source at that tag; rerunning an old failed job still uses its original workflow.

## Behavior

- Switch on release, only when Ctrl was pressed alone.
- Other keys, modifier combinations, mouse buttons and wheel activity cancel the pending tap. Pointer movement does not.
- Ctrl+C/V/Z, Ctrl+click and other shortcuts pass through unchanged; no keyboard events are suppressed or synthesized.
- Both Ctrl keys together do not switch. A long solo hold still switches on release.
- Injected keyboard events cancel recognition; this also avoids treating AltGr's synthetic Ctrl as a tap.
- A switch request is discarded if the foreground window changed between the Ctrl press and dispatch.
- A second instance exits without installing duplicate hooks.

## Architecture and limits

`TapDetector.cs` contains the independently tested state machine. `Program.cs` installs low-level keyboard/mouse hooks, provides the tray UI and requests a concrete HKL via `WM_INPUTLANGCHANGEREQUEST` on the foreground application's focused window. Work is queued outside the hook callback. A delayed check reports whether the target thread's layout matches the request; posting a message alone is not considered proof of success.

This is a prototype for ordinary desktop applications and EN/RU keyboard layouts. An application may ignore the request. Elevated applications, UWP/IME input, remote desktops, games and secure desktops are not guaranteed. It does not change layouts on the lock/login screen. No administrator rights are requested. Hooks run on the UI thread; a stalled message loop can cause Windows to remove a low-level hook. Recovery from desktop/session transitions and hook timeouts needs hardening before calling this production-ready.

The tests cover recognition, cancellation, shortcut ordering, repeats, both Ctrl keys, startup state and AltGr/injected input. Real input and layout switching still require interactive Windows checks.

## Manual acceptance checks

1. In Notepad, choose EN with left Ctrl and RU with right Ctrl; type after each tap and inspect the tray status.
2. Repeat in the browser and the editor used daily, including more than two installed layouts.
3. Check Ctrl+C/V/Z, Ctrl+Shift combinations and Ctrl+click without accidental layout switches.
4. Check both Ctrl keys, wheel activity, long holds, rapid taps and AltGr if available.
5. Check pause/resume, settings persistence after restart and exit (Ctrl should remain normal).
6. Check focus changes, sleep/resume and elevated apps separately; record any incompatibility.

## Icon

The icon artwork was generated with ChatGPT and is in src/app-icon.png. src/app.ico contains 16–256 px variants and is embedded in both the executable and the tray resource. To regenerate it after changing the PNG, run node tools/make-icon.cjs with sharp installed (or pass the path to the sharp package as the first argument). Ordinary builds use the existing ICO and need no Node.js.

## License

Copyright 2026 tinted-knight.

Licensed under the [Apache License, Version 2.0](LICENSE).
