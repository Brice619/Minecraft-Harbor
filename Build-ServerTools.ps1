param([Parameter(Mandatory=$true)][string]$JavaHome,[Parameter(Mandatory=$true)][string]$AutoModpackJar,[Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$taskOutput=[IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($taskOutput)|Out-Null
$taskDeps=@()
foreach($dependency in @('com/google/code/gson/gson/2.10.1/gson-2.10.1.jar','org/apache/logging/log4j/log4j-api/2.22.1/log4j-api-2.22.1.jar','org/apache/logging/log4j/log4j-core/2.22.1/log4j-core-2.22.1.jar')) {
    $target=Join-Path $taskOutput ([IO.Path]::GetFileName($dependency))
    $url='https://repo.maven.apache.org/maven2/'+$dependency
    $hash=(Invoke-WebRequest ($url+'.sha1')).Content.Trim()
    if(!(Test-Path -LiteralPath $target)){Invoke-WebRequest $url -OutFile $target}
    if((Get-FileHash -LiteralPath $target -Algorithm SHA1).Hash -ne $hash){throw "Dependency checksum mismatch: $target"}
    $taskDeps+=$target
}
$classes=Join-Path ([IO.Path]::GetDirectoryName($taskOutput)) ('server-tool-classes-'+[Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($classes)|Out-Null
& (Join-Path $JavaHome 'bin/javac.exe') --release 17 -proc:none -encoding UTF-8 -cp ((@($AutoModpackJar)+$taskDeps)-join ';') -d $classes (Join-Path $PSScriptRoot 'server-tools-source/ConfigureAutoModpack.java')
if($LASTEXITCODE -ne 0){throw 'Server configuration tool compilation failed'}
& (Join-Path $JavaHome 'bin/jar.exe') cf (Join-Path $taskOutput 'configure-automodpack.jar') -C $classes ConfigureAutoModpack.class
if($LASTEXITCODE -ne 0){throw 'Server configuration tool packaging failed'}
