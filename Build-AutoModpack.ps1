param([ValidateSet('4.0.6','5.0.0-rc.2')][string]$Version='4.0.6',[string]$OutputDirectory=(Join-Path $PSScriptRoot 'artifacts/automodpack'))
$ErrorActionPreference='Stop'
$taskOutput=[IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($taskOutput)|Out-Null
if($Version -eq '4.0.6') {
    $taskName='automodpack-mc1.21.1-neoforge-4.0.6.jar'
    $taskUrl='https://cdn.modrinth.com/data/k68glP2e/versions/e6HhD1Ik/'+$taskName
    $taskExpected='E76570A113AC9CD7FECC85FC6D2323EF2E04318E4B7615D34519843ADFE838F5'
    $taskAlgorithm='SHA256'
} else {
    $taskName='automodpack-5.0.0-rc.2.jar'
    $taskUrl='https://cdn.modrinth.com/data/k68glP2e/versions/bBBmWoC3/'+$taskName
    $taskExpected='1ECAF268B62F82C7B40774482CAF2721C029B0F5D355303C71E5223585E6E888BF6990C2D50FCB3237104E6BC3D2353D8111E2521AA3DF5F83E1C98C085F0380'
    $taskAlgorithm='SHA512'
}
$taskJar=Join-Path $taskOutput $taskName
if(!(Test-Path -LiteralPath $taskJar)){Invoke-WebRequest $taskUrl -OutFile $taskJar}
if((Get-FileHash -LiteralPath $taskJar -Algorithm $taskAlgorithm).Hash -ne $taskExpected){throw 'Official AutoModpack checksum mismatch'}
(Get-FileHash -LiteralPath $taskJar).Hash|Set-Content -LiteralPath ($taskJar+'.sha256')
Write-Output $taskJar
