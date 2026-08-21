param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [string]$SigningPrivateKeyPath
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$releaseRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot "artifacts/release/$Version"))
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts/release'))
if (-not $releaseRoot.StartsWith($artifactRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Unsafe release output path.'
}
if (Test-Path -LiteralPath $releaseRoot) {
    throw "Release output already exists: $releaseRoot"
}

$packageRoot = Join-Path $releaseRoot 'InputCue'
$updaterPublishRoot = Join-Path $releaseRoot '.updater-publish'
New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
New-Item -ItemType Directory -Path $updaterPublishRoot -Force | Out-Null

$publishArguments = @(
    '--configuration', 'Release',
    '--runtime', 'win-x64',
    '--self-contained', 'true',
    '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:PublishTrimmed=false',
    '-p:DebugType=None',
    '-p:DebugSymbols=false',
    "-p:Version=$Version"
)

dotnet publish (Join-Path $repoRoot 'src/InputCue.App/InputCue.App.csproj') @publishArguments --output $packageRoot
if ($LASTEXITCODE -ne 0) { throw 'InputCue publish failed.' }
dotnet publish (Join-Path $repoRoot 'src/InputCue.Updater/InputCue.Updater.csproj') @publishArguments --output $updaterPublishRoot
if ($LASTEXITCODE -ne 0) { throw 'InputCue.Updater publish failed.' }

Copy-Item -LiteralPath (Join-Path $updaterPublishRoot 'InputCue.Updater.exe') -Destination $packageRoot
[IO.File]::WriteAllText(
    (Join-Path $packageRoot 'portable.flag'),
    "InputCue portable $Version`n",
    [Text.UTF8Encoding]::new($false))

$licenseRoot = Join-Path $packageRoot 'licenses'
New-Item -ItemType Directory -Path $licenseRoot -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE') -Destination (Join-Path $packageRoot 'LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $repoRoot 'THIRD-PARTY-NOTICES.md') -Destination $packageRoot

$dotnetRoot = Split-Path -Parent (Get-Command dotnet -ErrorAction Stop).Source
$dotnetLicensePath = Join-Path $dotnetRoot 'LICENSE.txt'
$dotnetNoticesPath = Join-Path $dotnetRoot 'ThirdPartyNotices.txt'
if (-not (Test-Path -LiteralPath $dotnetLicensePath) -or
    -not (Test-Path -LiteralPath $dotnetNoticesPath)) {
    throw "The .NET license files were not found under $dotnetRoot."
}

Copy-Item -LiteralPath $dotnetLicensePath -Destination (Join-Path $licenseRoot 'DOTNET-LICENSE.txt')
Copy-Item -LiteralPath $dotnetNoticesPath -Destination (Join-Path $licenseRoot 'DOTNET-THIRD-PARTY-NOTICES.txt')

$assetName = "InputCue-$Version-win-x64-portable.zip"
$assetPath = Join-Path $releaseRoot $assetName
Compress-Archive -Path (Join-Path $packageRoot '*') -DestinationPath $assetPath -CompressionLevel Optimal

$assetInfo = Get-Item -LiteralPath $assetPath
$assetHash = (Get-FileHash -LiteralPath $assetPath -Algorithm SHA256).Hash
$manifest = [ordered]@{
    schemaVersion = 1
    version = $Version
    publishedAt = [DateTimeOffset]::UtcNow.ToString('O')
    asset = [ordered]@{
        url = "https://github.com/huangko555/InputCue/releases/download/v$Version/$assetName"
        sha256 = $assetHash
        size = $assetInfo.Length
    }
}
$manifestPath = Join-Path $releaseRoot 'portable-releases.json'
$manifestJson = $manifest | ConvertTo-Json -Depth 4
[IO.File]::WriteAllText($manifestPath, $manifestJson, [Text.UTF8Encoding]::new($false))

if ([string]::IsNullOrWhiteSpace($SigningPrivateKeyPath)) {
    $SigningPrivateKeyPath = Join-Path $repoRoot '.local/update-signing-private.pem'
}

$privateKeyPem = if (-not [string]::IsNullOrWhiteSpace($env:UPDATE_SIGNING_PRIVATE_KEY_PEM)) {
    $env:UPDATE_SIGNING_PRIVATE_KEY_PEM
} elseif (Test-Path -LiteralPath $SigningPrivateKeyPath) {
    [IO.File]::ReadAllText([IO.Path]::GetFullPath($SigningPrivateKeyPath))
} else {
    throw 'No update signing private key was provided.'
}

$signerSource = @'
using System.Security.Cryptography;
public static class InputCueManifestSigner
{
    public static byte[] Sign(byte[] data, string privateKeyPem)
    {
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(privateKeyPem);
        return ecdsa.SignData(data, HashAlgorithmName.SHA256);
    }
}
'@
Add-Type -TypeDefinition $signerSource -Language CSharp
$signature = [InputCueManifestSigner]::Sign([IO.File]::ReadAllBytes($manifestPath), $privateKeyPem)
[IO.File]::WriteAllText(
    (Join-Path $releaseRoot 'portable-releases.json.sig'),
    [Convert]::ToBase64String($signature),
    [Text.UTF8Encoding]::new($false))

$manifestHash = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash
$checksums = @(
    "$assetHash  $assetName",
    "$manifestHash  portable-releases.json"
) -join "`n"
[IO.File]::WriteAllText(
    (Join-Path $releaseRoot 'SHA256SUMS.txt'),
    $checksums + "`n",
    [Text.UTF8Encoding]::new($false))

Remove-Item -LiteralPath $packageRoot -Recurse -Force
Remove-Item -LiteralPath $updaterPublishRoot -Recurse -Force
Write-Output $releaseRoot
