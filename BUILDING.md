# Building Minecraft Harbor

Use Windows and the .NET 10 SDK. From the repository root, run:

```powershell
./Build-Release.ps1
```

The script publishes Harbor, the LAN client installer, and the 1.3 app installer. It includes native dependencies, OCR data, artwork, and dependency notices. The final installer is in `artifacts/installer/`, with its SHA-256 checksum.

The installer payload is generated from these builds. Do not commit `payload.zip`, build output, server data, saved worlds, local settings, or credentials.

Projects:

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
