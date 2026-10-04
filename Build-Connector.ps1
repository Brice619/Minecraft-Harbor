param([Parameter(Mandatory=$true)][string]$JavaHome,[string]$OutputDirectory=(Join-Path $PSScriptRoot 'artifacts/connector'))
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$taskOutput=[IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($taskOutput)|Out-Null
function VerifiedDownload([string]$Url,[string]$Path,[string]$Hash,[string]$Algorithm="SHA256"){
    if(!(Test-Path -LiteralPath $Path)){Invoke-WebRequest $Url -OutFile $Path}
    if((Get-FileHash -LiteralPath $Path -Algorithm $Algorithm).Hash -ne $Hash){throw "Checksum mismatch: $Path"}
}
$taskBase=Join-Path $taskOutput 'automodpack-original.jar'
VerifiedDownload 'https://cdn.modrinth.com/data/k68glP2e/versions/e6HhD1Ik/automodpack-mc1.21.1-neoforge-4.0.6.jar' $taskBase 'E76570A113AC9CD7FECC85FC6D2323EF2E04318E4B7615D34519843ADFE838F5'
$taskDeps=@()
foreach($taskDependency in @('com/google/code/gson/gson/2.10.1/gson-2.10.1.jar','org/apache/logging/log4j/log4j-api/2.22.1/log4j-api-2.22.1.jar','org/apache/logging/log4j/log4j-core/2.22.1/log4j-core-2.22.1.jar')){
    $taskTarget=Join-Path $taskOutput ([IO.Path]::GetFileName($taskDependency))
    $taskUrl='https://repo.maven.apache.org/maven2/'+$taskDependency
    $taskSha=(Invoke-WebRequest ($taskUrl+'.sha1')).Content.Trim()
    VerifiedDownload $taskUrl $taskTarget $taskSha "SHA1"
    $taskDeps+=$taskTarget
}
$taskClasses=Join-Path $taskOutput ('classes-'+[Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($taskClasses)|Out-Null
$taskClasspath=(@($taskBase)+$taskDeps)-join ';'
$taskSources=@(Get-ChildItem (Join-Path $PSScriptRoot 'automodpack-source/core'),(Join-Path $PSScriptRoot 'automodpack-source/loader') -Recurse -Filter '*.java' -File | Select-Object -ExpandProperty FullName)
& (Join-Path $JavaHome 'bin/javac.exe') --release 17 -proc:none -encoding UTF-8 -cp $taskClasspath -d $taskClasses @taskSources
if($LASTEXITCODE -ne 0){throw 'Connector compilation failed'}
$taskTestCp=$taskClasses+';'+$taskClasspath
& (Join-Path $JavaHome 'bin/javac.exe') --release 17 -proc:none -encoding UTF-8 -cp $taskTestCp -d $taskClasses (Join-Path $PSScriptRoot 'automodpack-source/PackGateTests.java')
if($LASTEXITCODE -ne 0){throw 'Connector tests did not compile'}
& (Join-Path $JavaHome 'bin/java.exe') -cp $taskTestCp PackGateTests $taskOutput
if($LASTEXITCODE -ne 0){throw 'Connector tests failed'}
$taskJar=Join-Path $taskOutput 'automodpack-mc1.21.1-neoforge-4.0.6-harbor14.jar'
$taskTemp=Join-Path $taskOutput ('connector-'+[Guid]::NewGuid().ToString('N')+'.jar')
Copy-Item -LiteralPath $taskBase -Destination $taskTemp
$taskZip=[IO.Compression.ZipFile]::Open($taskTemp,[IO.Compression.ZipArchiveMode]::Update)
try{
    foreach($taskClass in Get-ChildItem (Join-Path $taskClasses 'pl') -Recurse -Filter '*.class' -File){
        $taskName=[IO.Path]::GetRelativePath($taskClasses,$taskClass.FullName).Replace('\','/')
        $taskOld=$taskZip.GetEntry($taskName);if($taskOld){$taskOld.Delete()}
        $taskAdded=[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($taskZip,$taskClass.FullName,$taskName,[IO.Compression.CompressionLevel]::Optimal)
        $taskAdded.LastWriteTime=[DateTimeOffset]::new(2026,1,1,0,0,0,[TimeSpan]::Zero)
    }
    $taskEntry=$taskZip.GetEntry('META-INF/neoforge.mods.toml')
    $taskReader=[IO.StreamReader]::new($taskEntry.Open());$taskToml=$taskReader.ReadToEnd();$taskReader.Dispose();$taskEntry.Delete()
    $taskToml=$taskToml.Replace('version = "4.0.6"','version = "4.0.6-harbor14"').Replace('displayName = "AutoModpack"','displayName = "AutoModpack (Harbor Connector 1.4)"')
    $taskUpdated=$taskZip.CreateEntry('META-INF/neoforge.mods.toml');$taskUpdated.LastWriteTime=[DateTimeOffset]::new(2026,1,1,0,0,0,[TimeSpan]::Zero)
    $taskWriter=[IO.StreamWriter]::new($taskUpdated.Open(),[Text.UTF8Encoding]::new($false));$taskWriter.Write($taskToml);$taskWriter.Dispose()
}finally{$taskZip.Dispose()}
Move-Item -LiteralPath $taskTemp -Destination $taskJar -Force
(Get-FileHash -LiteralPath $taskJar).Hash|Set-Content -LiteralPath ($taskJar+'.sha256')
Write-Output "Connector: $taskJar"
