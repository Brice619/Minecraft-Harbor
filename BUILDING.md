# Building Minecraft Harbor

Use Windows, the .NET 10 SDK and JDK 21. From the repository root, run:

```powershell
./Build-Release.ps1 -JavaHome <JDK21-folder> -LegacyClientInstaller <existing-agent-installer>
```

The script verifies the unmodified official AutoModpack 5.0.0-rc.2 download, builds the host-only configuration utility, then publishes Harbor and the 1.4 app installer. It retains the supplied LAN client installer unchanged; its matching .sha256 file must be beside it. It includes native dependencies, OCR data, artwork, and dependency notices. The final installer is in `artifacts/installer/`, with its SHA-256 checksum.

The installer payload is generated from these builds. Do not commit `payload.zip`, build output, server data, saved worlds, local settings, or credentials.

Projects:

- `server-tools-source/`: host-only configuration utility using the official AutoModpack parser. Never installed in a client or a mods folder.
- `source/`: current Harbor desktop application, artwork, and fixture checks.
- `agent-source/`: current LAN mod update client and its installer.
- `app-installer-source/`: current desktop application installer.
- `installer-source/`: earlier client/helper installer, kept for project history. It is not used by the current release script; its original build requires the AutoModpack helper JAR described in its `BUILD.txt`.
- `licenses/`: dependency and artwork notices included in distributions.

The checked-in defaults use generic world and owner values. Existing local server settings are separate from the source and stay on the owner's PC.

For a local framework-dependent app build:

```powershell
dotnet publish source/MinecraftHarbor.csproj -p:PublishProfile=Local -o artifacts/local-app
```

Fixture checks and their arguments are listed in `source/BUILD.txt`. Real-server verification starts and stops a world; do not run it while an existing Harbor server is running.

Building an unsigned installer does not guarantee Windows signing/reputation approval. Release checks run against the built executables in isolated test folders; they do not use personal server worlds.
