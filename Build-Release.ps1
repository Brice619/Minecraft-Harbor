param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'artifacts'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
$releaseRoot = [IO.Path]::GetFullPath($OutputDirectory)
$appOutput = Join-Path $releaseRoot 'app'
$clientOutput = Join-Path $releaseRoot 'client'
$installerOutput = Join-Path $releaseRoot 'installer'
function Publish-Project([string]$Project, [string]$Destination) {
    & dotnet publish $Project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=false -o $Destination
    if ($LASTEXITCODE -ne 0) { throw "Publish failed: $Project" }
}
Publish-Project (Join-Path $PSScriptRoot 'source/MinecraftHarbor.csproj') $appOutput
Publish-Project (Join-Path $PSScriptRoot 'agent-source/HarborAgent.csproj') $clientOutput
$clientDirectory = Join-Path $appOutput 'client-setup'
[IO.Directory]::CreateDirectory($clientDirectory) | Out-Null
$clientInstaller = Join-Path $clientDirectory 'Minecraft-Harbor-Agent-Setup.exe'
Copy-Item -LiteralPath (Join-Path $clientOutput 'Minecraft-Harbor-Agent-Setup.exe') -Destination $clientInstaller -Force
(Get-FileHash -LiteralPath $clientInstaller).Hash | Set-Content -LiteralPath ($clientInstaller + '.sha256')
$licenseDirectory = Join-Path $appOutput 'licenses'
[IO.Directory]::CreateDirectory($licenseDirectory) | Out-Null
Copy-Item -Path (Join-Path $PSScriptRoot 'licenses/*.txt') -Destination $licenseDirectory -Force
$payloadPath = Join-Path $PSScriptRoot 'app-installer-source/payload.zip'
# Recreate this generated file through a temporary ZIP, preserving old output on failure.
$temporaryPayload = Join-Path $releaseRoot ('payload-' + [Guid]::NewGuid().ToString('N') + '.zip')
$archive = [IO.Compression.ZipFile]::Open($temporaryPayload, [IO.Compression.ZipArchiveMode]::Create)
try {
    Get-ChildItem -LiteralPath $appOutput -Recurse -File | Where-Object { $_.Extension -ne '.pdb' } | ForEach-Object {
        $relative = [IO.Path]::GetRelativePath($appOutput, $_.FullName).Replace('\', '/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $_.FullName, $relative, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $archive.Dispose() }
Move-Item -LiteralPath $temporaryPayload -Destination $payloadPath -Force
(Get-FileHash -LiteralPath $payloadPath).Hash | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'app-installer-source/payload.sha256')
Publish-Project (Join-Path $PSScriptRoot 'app-installer-source/HarborSetup.csproj') $installerOutput
$installer = Join-Path $installerOutput 'Minecraft-Harbor-Setup-1.3.exe'
(Get-FileHash -LiteralPath $installer).Hash | Set-Content -LiteralPath ($installer + '.sha256')
Write-Output "Installer: $installer"
