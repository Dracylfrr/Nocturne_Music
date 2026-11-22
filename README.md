# Nocturne Local

A Windows desktop music player built to explore system design, user needs, and the scope of a focused personal library application. Nocturne Local plays audio files you already have, with a minimal record-player icon and a dark interface.

## Purpose and intended users

The project serves listeners who organize music in folders and want quick access to favorites, album artwork, and playback without managing an online account. It is a practical learning project for translating user feedback into interaction design, choosing a manageable scope, and understanding how filesystem operations affect application state.

The public edition contains local playback and library management. It has no YouTube downloader, download screen, yt-dlp, FFmpeg, or QuickJS dependency.

## Features

- Play MP3, M4A, AAC, WAV, and WMA through Windows media support. Codec availability depends on Windows.
- Use a flat Music folder or nested album and section folders. The sidebar reflects the folder hierarchy on disk.
- Import files or complete folder trees, keeping source files in place.
- Refresh the library manually or let filesystem changes trigger a background scan.
- Create and rename sections, drag sections into other sections, reorder siblings, move nested sections up a level, and send deleted sections to the Recycle Bin after confirmation.
- Drag a song into a section or use its Move to section menu. Associated metadata and image sidecars move with it.
- Star tracks and use a dedicated Favorites view with counts, alphabetical sorting, recent ordering, section-name sorting, and a saved custom order.
- Search titles, artists, and section paths.
- Play a section and its descendants, shuffle, repeat, seek, and adjust volume.
- Switch to a compact player, optionally keep it above other windows, or minimize to the system tray.
- Show artwork beside section names or use compact cover-only cards.
- Look up album artwork through a browser image search, choose a local image, or use a direct HTTPS image URL.
- Display embedded MP3 artwork and metadata where supported.
- Apply artwork to one track or all tracks in its section.
- Change the application's displayed name in Settings.
- Install per user, choose an application location and Music folder, and create Start menu and desktop shortcuts.

## Install and data

Build the project and run dist/Nocturne.exe. The local edition defaults to %USERPROFILE%\Music\NocturneLocal, and its music library defaults to %USERPROFILE%\Music. Sections are ordinary folders within that library.

Preferences, artwork overrides, and the selected library path are stored in %LOCALAPPDATA%\NocturneLocal\Data. Local and personal editions have separate preferences. Uninstall preserves music and preferences. Section deletion and song moves operate on actual files, so their effects are also visible in Explorer.

## Design and architecture

The application uses C# and .NET Framework 4.8 with WPF for the interface, Windows MediaPlayer for playback, and Windows Forms shell dialogs for file selection. It builds with the Windows framework compiler and has no NuGet dependency.

Main.xaml defines the window, track rows, section cards, and playback controls. Nocturne.cs holds library scanning, metadata parsing, playback state, preferences, and filesystem operations. ExplorerFolderPicker.cs wraps the native Windows folder picker. Installer.cs packages a per-user installation and uninstaller.

Library scans read metadata on a background task. FileSystemWatcher events are debounced before refreshing the UI. Track favorites and section order persist to JSON. Paths are updated when sections or tracks move, so favorites continue referring to the same music. Artwork overrides remain separate from audio files.

These choices support a small, inspectable application. The current source keeps several responsibilities in one player class, which is a maintainability tradeoff and a potential next refactoring exercise.

## Learning and interview discussion

The project demonstrates requirements discovery through iterative feedback. Examples include replacing implicit folder nesting with explicit creation and drag operations, separating app and library locations, exposing refresh feedback, and making Favorites ordering user-controlled.

Useful discussion topics include identifying an intended user, defining a local-first scope, handling partial file moves, keeping UI and filesystem state consistent, preserving settings across upgrades, and explaining validation limits honestly. The repository describes implemented behavior and design reasoning. It does not claim measured usability improvements or formal user research.

## Build and validation

On Windows with .NET Framework 4.8, run:

    powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1

The script compiles the player, runs its self-test, and creates the installer. It requires no download executables or tools folder contents.

The self-test checks library boundaries, rejects path traversal, loads the XAML, and checks required controls. During development, isolated libraries have also exercised favorites, section moves, artwork cards, and filesystem refresh. A clean-account installer run and a broad accessibility review remain outstanding.

## Current scope and limitations

This is a Windows application with an unsigned installer. Metadata support is strongest for MP3 ID3v2.3/v2.4. Other formats can show Unknown artist. Artwork search opens the default browser and requires the user to choose an image. Large-library performance and all Windows codec combinations have not been comprehensively tested.

Version 1.1 checks Dracylfrr/Nocturne_Music for the latest stable GitHub Release at startup. Settings also has Check for updates. It compares numeric versions and offers to download and install newer releases. The installer download must match the SHA-256 digest from GitHub and the expected assembly version. No GitHub sign-in is needed for the public repository.
 
After approval, the app saves preferences and a settings.before-update.json backup, closes, and starts the installer in update mode. The installer waits for the app to exit, updates its existing location, and restarts it. Updates replace only the five packaged application files. Preferences, artwork and music remain in their existing locations. Replacement failures roll back files already changed. A running Nocturne Local process blocks concurrent replacement.
 
Version 1.0 requires a manual upgrade to 1.1 to receive the updater. The personal Bhumika edition has a separate release lifecycle and is not updated from this public repository. Installers are currently unsigned. SHA-256 verifies consistency with the release metadata, and does not replace publisher code signing.
 
Run tests/run.ps1 after building for updater version, integrity, preservation and rollback regression checks. Future release tags must be numeric versions such as v1.2 and include an uploaded Nocturne.exe asset with GitHub digest metadata. Stage new releases as drafts, upload the installer, then publish.

## Source layout

- src/ contains C#, XAML, the icon, and application configuration.
- build.ps1 produces the player and installer.
- dist/ is generated and excluded from source control.

No sample music, personal library, credentials, or personal preferences are included.
