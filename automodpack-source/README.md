# Harbor connector 1.4

These are the modified files from AutoModpack v4.0.6, upstream commit
`2ff957d03ffb4253fea0c0cbda4d0def4e9528d6`:
https://github.com/Skidamek/AutoModpack/tree/2ff957d03ffb4253fea0c0cbda4d0def4e9528d6

AutoModpack is licensed under LGPL-3.0; its license is included here. The full
unchanged source is available at the pinned upstream commit above. Overlay these
files onto that checkout to obtain the complete modified source. Harbor's changes
are also licensed under LGPL-3.0.

`Build-Connector.ps1 -JavaHome <JDK 21 folder>` compiles the changed Java classes,
tests the real updater's preflight, and overlays the classes into the verified
upstream 1.21.1 NeoForge JAR. The upstream artifact and Maven dependencies are
downloaded with checksum verification. No installed Minecraft files are used or
modified by the build.

Harbor writes `automodpack/harbor-requirements.json` before server startup. It
contains only the required pack name/version and public CurseForge project/file
IDs. AutoModpack includes it as a manifest field over its existing verified
connection. The client reads its own `minecraftinstance.json` and checks the exact
installed CurseForge project/file IDs before staging, downloading, or applying
mods/configs/scripts. A renamed profile and stale exported `manifest.json` do not
affect this check. Missing or unreadable metadata stops syncing.

The same gate applies to startup/preload and cached-pack activation. Version
mismatch displays the required and installed release and tells the player to
update the existing profile in CurseForge. Matching versions use the normal
AutoModpack updater. Configs are included and update with the server; personal
options and FancyMenu user variables are excluded. The connector disables its
upstream self-update so it cannot lose Harbor's preflight.

The connector's version is `4.0.6-harbor14`, distinguishing it from unmodified
AutoModpack. Both sides must use this connector. Minecraft 1.21.1 / NeoForge is
the supported target for this release. The modloader itself remains installed
by CurseForge, rather than downloaded as a mod JAR.
