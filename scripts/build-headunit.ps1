param(
    [string]$AndroidSdk = $(if ($env:ANDROID_HOME) { $env:ANDROID_HOME } elseif ($env:ANDROID_SDK_ROOT) { $env:ANDROID_SDK_ROOT } elseif ($env:LOCALAPPDATA) { Join-Path $env:LOCALAPPDATA 'Android/Sdk' } else { '' }),
    [string]$JavaSdk = $env:JAVA_HOME,
    [string]$SigningKey = ''
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
if (-not $AndroidSdk -or -not (Test-Path -LiteralPath $AndroidSdk)) { throw 'Pass -AndroidSdk or set ANDROID_HOME to Android SDK 35.' }
if (-not $JavaSdk -or -not (Test-Path -LiteralPath $JavaSdk)) { throw 'Pass -JavaSdk or set JAVA_HOME to JDK 21.' }
if ($SigningKey -and -not (Test-Path -LiteralPath $SigningKey)) { throw 'The specified existing Android debug keystore was not found.' }
Push-Location $repoRoot
try {
    & dotnet run --project tests/WiiYiiHudNavigator.Yandex.Tests -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Navigation tests failed.' }
    & dotnet run --project tests/YandexHudHeadUnit.Tests -c Release
    if ($LASTEXITCODE -ne 0) { throw 'HUD protocol tests failed.' }

    $buildArgs = @('build', 'src/YandexHudHeadUnit/YandexHudHeadUnit.csproj', '-c', 'Release', '-t:SignAndroidPackage',
        "-p:AndroidSdkDirectory=$AndroidSdk", "-p:JavaSdkDirectory=$JavaSdk")
    # With no key argument, .NET Android uses this developer's local debug key.
    # -SigningKey supports the original Android debug key for compatible updates.
    if ($SigningKey) {
        $buildArgs += @('-p:AndroidKeyStore=true', "-p:AndroidSigningKeyStore=$SigningKey",
            '-p:AndroidSigningKeyAlias=androiddebugkey', '-p:AndroidSigningStorePass=android', '-p:AndroidSigningKeyPass=android')
    }
    & dotnet @buildArgs
    if ($LASTEXITCODE -ne 0) { throw 'APK build failed.' }

    $outputDirectory = Join-Path $repoRoot 'artifacts/release'
    New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
    $apkName = 'YandexHUD-TEYES-Navigator-1.4.apk'
    Copy-Item -LiteralPath 'src/YandexHudHeadUnit/bin/Release/net9.0-android/com.avashield.yandexhud.headunit-Signed.apk' -Destination (Join-Path $outputDirectory $apkName)
    $hash = (Get-FileHash -LiteralPath (Join-Path $outputDirectory $apkName) -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText((Join-Path $outputDirectory 'SHA256SUMS.txt'), "$hash  $apkName`n", [Text.UTF8Encoding]::new($false))
    Write-Output "Built: $outputDirectory"
}
finally { Pop-Location }
