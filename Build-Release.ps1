param([Parameter(Mandatory=$true)][string]$JavaHome,[Parameter(Mandatory=$true)][string]$LegacyClientInstaller,[string]$OutputDirectory=(Join-Path $PSScriptRoot 'artifacts'))
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$releaseRoot=[IO.Path]::GetFullPath($OutputDirectory)
$appOutput=Join-Path $releaseRoot 'app'
$installerOutput=Join-Path $releaseRoot 'installer'
$connectorOutput=Join-Path $releaseRoot 'connector'
& (Join-Path $PSScriptRoot 'Build-Connector.ps1') -JavaHome $JavaHome -OutputDirectory $connectorOutput
$helper=Join-Path $connectorOutput 'automodpack-mc1.21.1-neoforge-4.0.6-harbor14.jar'
$helperHash=(Get-FileHash -LiteralPath $helper).Hash
$clientSource=Join-Path $PSScriptRoot 'source/ClientSetup.cs'
$source=[IO.File]::ReadAllText($clientSource)
if([regex]::Matches($source,'internal const string HelperSha256 = "[A-F0-9]{64}";').Count -ne 1){throw 'Helper checksum declaration not found'}
$source=[regex]::Replace($source,'internal const string HelperSha256 = "[A-F0-9]{64}";','internal const string HelperSha256 = "'+$helperHash+'";')
[IO.File]::WriteAllText($clientSource,$source)
function Publish-Project([string]$Project,[string]$Destination){
    & dotnet publish $Project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=false -o $Destination
    if($LASTEXITCODE -ne 0){throw "Publish failed: $Project"}
}
Publish-Project (Join-Path $PSScriptRoot 'source/MinecraftHarbor.csproj') $appOutput
$clientDirectory=Join-Path $appOutput 'client-setup'
[IO.Directory]::CreateDirectory($clientDirectory)|Out-Null
# Keep the existing standalone client installer unchanged in this release.
$legacyHash=(Get-FileHash -LiteralPath $LegacyClientInstaller).Hash
if($legacyHash -ne ([IO.File]::ReadAllText($LegacyClientInstaller+'.sha256').Trim())){throw 'Legacy client checksum mismatch'}
Copy-Item -LiteralPath $LegacyClientInstaller -Destination (Join-Path $clientDirectory 'Minecraft-Harbor-Agent-Setup.exe') -Force
$legacyHash|Set-Content -LiteralPath (Join-Path $clientDirectory 'Minecraft-Harbor-Agent-Setup.exe.sha256')
Copy-Item -LiteralPath $helper -Destination $clientDirectory -Force
Copy-Item -LiteralPath ($helper+'.sha256') -Destination $clientDirectory -Force
$licenseDirectory=Join-Path $appOutput 'licenses'
[IO.Directory]::CreateDirectory($licenseDirectory)|Out-Null
Copy-Item -Path (Join-Path $PSScriptRoot 'licenses/*.txt') -Destination $licenseDirectory -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'automodpack-source/LICENSE') -Destination (Join-Path $licenseDirectory 'AutoModpack-LGPL-3.0.txt') -Force
$payloadPath=Join-Path $PSScriptRoot 'app-installer-source/payload.zip'
$temporaryPayload=Join-Path $releaseRoot ('payload-'+[Guid]::NewGuid().ToString('N')+'.zip')
$archive=[IO.Compression.ZipFile]::Open($temporaryPayload,[IO.Compression.ZipArchiveMode]::Create)
try{
    Get-ChildItem -LiteralPath $appOutput -Recurse -File | Where-Object {$_.Extension -ne '.pdb'} | ForEach-Object {
        $relative=[IO.Path]::GetRelativePath($appOutput,$_.FullName).Replace('\','/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,$_.FullName,$relative,[IO.Compression.CompressionLevel]::Optimal)|Out-Null
    }
}finally{$archive.Dispose()}
Move-Item -LiteralPath $temporaryPayload -Destination $payloadPath -Force
(Get-FileHash -LiteralPath $payloadPath).Hash|Set-Content -LiteralPath (Join-Path $PSScriptRoot 'app-installer-source/payload.sha256')
Publish-Project (Join-Path $PSScriptRoot 'app-installer-source/HarborSetup.csproj') $installerOutput
$installer=Join-Path $installerOutput 'Minecraft-Harbor-Setup-1.4.exe'
(Get-FileHash -LiteralPath $installer).Hash|Set-Content -LiteralPath ($installer+'.sha256')
Write-Output "Installer: $installer"
