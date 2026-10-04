# Minecraft Harbor

Create and manage vanilla and modded Minecraft servers from one Windows app.

[Download Minecraft Harbor](https://github.com/Brice619/Minecraft-Harbor/releases)

## Features

- Saved servers and worlds, with per-server settings.
- Live console, performance history, and player management.
- World backups and hourly backup controls.
- LAN client setup for an existing CurseForge modpack.
- AutoModpack connector checks the exact CurseForge release before syncing mods, configs and scripts.
- A mismatched or unreadable release pauses syncing and shows the required pack version.

## Install

Download **Minecraft-Harbor-Setup-1.4.exe** and run the installer. Windows 10/11, 64-bit. The app runtime is included. Minecraft, Java server runtimes, and modpacks are prepared separately as needed.

The release contains the application only. Private saves, accounts, modpacks, and user settings are not included.

## 1.4 connector

On the host, Harbor pins the client release ID associated with the server pack. Updating the host's CurseForge profile alone does not change the server's required release. Select the matching client folder through the LAN setup tools if an imported server has no client release ID.

On each joining PC, use that release in your existing CurseForge profile. Close Minecraft and replace the old AutoModpack JAR in its `mods` folder with **automodpack-mc1.21.1-neoforge-4.0.6-harbor14.jar**. Launch and join normally. The host installs this connector when starting the server through Harbor 1.4. Both sides need the connector. This release targets Minecraft 1.21.1 / NeoForge.

The connector reads local `minecraftinstance.json` and compares CurseForge project and release IDs before downloads or cached-pack activation. It does not create another CurseForge profile or update the base modpack/loader itself. Use CurseForge to change the base release when prompted. AutoModpack retains its normal staging behavior inside the current instance.

The previous standalone Windows client installer remains bundled unchanged. Version checks now belong to the Minecraft connector. The LAN setup page offers the connector and the server certificate fingerprint.

Isolated updater, cached-pack, server configuration, clean-install and in-place installer tests pass. A live join from another PC remains unverified.

## Build

Use .NET 10 and JDK 21. Run `Build-Release.ps1 -JavaHome <JDK folder> -LegacyClientInstaller <existing Agent setup EXE>`. Supply its matching `.sha256` file beside it. The script builds/tests the connector, pins its checksum, and packages Harbor with the unchanged legacy client installer. See [connector source and build details](automodpack-source/README.md).

Minecraft is a trademark of Mojang Studios. Minecraft Harbor is an independent project.
