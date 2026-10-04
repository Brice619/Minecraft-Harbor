# Minecraft Harbor

Create and manage vanilla and modded Minecraft servers from one Windows app.

[Download Minecraft Harbor](https://github.com/Brice619/Minecraft-Harbor/releases)

## Features

- Saved servers and worlds, with per-server settings.
- Live console, performance history, and player management.
- World backups and hourly backup controls.
- LAN setup using your existing CurseForge modpack.
- Official AutoModpack synchronizes mods, configs and client scripts.
- The server's pack version appears as a reminder before download confirmation and in update review notes.

## Install

Download **Minecraft-Harbor-Setup-1.4.exe** and run the installer. Windows 10/11, 64-bit. The app runtime is included. Minecraft, Java server runtimes, and modpacks are prepared separately as needed.

Private saves, accounts, modpacks, and user settings are not included in the application download.

## Minecraft client setup

This build uses the unmodified official **AutoModpack 5.0.0-rc.2 beta** for Minecraft 1.21.1 / NeoForge. On each joining PC, close Minecraft and replace the old AutoModpack JAR in the existing profile's `mods` folder with **automodpack-5.0.0-rc.2.jar**, available from Harbor's LAN setup page or [the official release](https://github.com/Skidamek/AutoModpack/releases/tag/v5.0.0-rc.2). Launch and join normally. No Harbor helper mod or custom AutoModpack build is required.

Harbor installs the same official JAR on the host when starting a supported server. The native first-download confirmation lists the required CurseForge pack version in the main group's display name. In-game update review includes the reminder in the server's patch notes. This is a reminder only: it does not inspect the joining PC's CurseForge metadata or block mismatched versions. Automatic startup updates may run without an interactive review screen.

Use CurseForge to select the server's base pack version. AutoModpack synchronizes server-provided mods, configs and client scripts; server scripts and FancyMenu user variables are excluded. Configs are managed so later server changes can update them. Your worlds and personal options are not included.

The previous standalone Windows client installer remains bundled unchanged and optional. The LAN setup page provides the official AutoModpack download and certificate fingerprint. Upgrading a V4 host preserves its network configuration and certificate; configuration migration uses AutoModpack's own parser. The host-only configuration utility under `server-tools` is never placed in a client's mods folder.

## Build

Use .NET 10 and JDK 21. Run `Build-Release.ps1 -JavaHome <JDK folder> -LegacyClientInstaller <existing Agent setup EXE>`. Supply its matching `.sha256` file beside it. The script verifies the official AutoModpack download, builds Harbor and the host configuration utility, and packages the unchanged legacy installer. `Build-Connector.ps1` and `automodpack-source` are historical source for the earlier custom connector and are no longer used by the release build.

Minecraft is a trademark of Mojang Studios. Minecraft Harbor is an independent project.
