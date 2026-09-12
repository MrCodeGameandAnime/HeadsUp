[CmdletBinding()]
param(
    [string]$PublishDirectory,
    [string]$OutputDirectory,
    [string]$PackageVersion,
    [string]$CertificatePath,
    [string]$CertificatePassword
)

$ErrorActionPreference = 'Stop'

$packagingRoot = $PSScriptRoot
$repositoryRoot = Split-Path -Parent $packagingRoot

if ([string]::IsNullOrWhiteSpace($PublishDirectory)) {
    $PublishDirectory = Join-Path $repositoryRoot 'src\GitCiHud\bin\Release\net10.0\win-x64'
}
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repositoryRoot 'artifacts'
}
if ([string]::IsNullOrWhiteSpace($PackageVersion)) {
    $PackageVersion = (Get-Content -LiteralPath (Join-Path $packagingRoot 'package-version.txt') -Raw).Trim()
}

# PowerShell's provider location and .NET's process working directory can
# differ (for example, after Set-Location from a shell started in System32).
# Normalize caller-supplied relative paths against the PowerShell location so
# output never accidentally targets the Windows directory.
$shellDirectory = (Get-Location).Path
if (-not [System.IO.Path]::IsPathRooted($PublishDirectory)) {
    $PublishDirectory = Join-Path $shellDirectory $PublishDirectory
}
if (-not [System.IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory = Join-Path $shellDirectory $OutputDirectory
}

$versionParts = $PackageVersion.Split('.')
if ($versionParts.Count -ne 4 -or $PackageVersion -notmatch '^\d{1,5}\.\d{1,5}\.\d{1,5}\.0$') {
    throw "Package version '$PackageVersion' must be Major.Minor.Build.0 with numeric components."
}
$numericVersionParts = @($versionParts | ForEach-Object { [int]$_ })
if ($numericVersionParts[0] -lt 1 -or $numericVersionParts[0] -gt 65535 -or
    $numericVersionParts[1] -gt 65535 -or $numericVersionParts[2] -gt 65535 -or
    $numericVersionParts[3] -ne 0) {
    throw "Package version '$PackageVersion' must have Major 1-65535, Minor/Build 0-65535, and Revision exactly 0."
}

if (-not (Test-Path -LiteralPath $PublishDirectory -PathType Container)) {
    throw "Publish directory was not found: $PublishDirectory"
}
if (-not (Test-Path -LiteralPath (Join-Path $PublishDirectory 'HeadsUp.exe') -PathType Leaf)) {
    throw "The publish directory does not contain HeadsUp.exe: $PublishDirectory"
}
$runtimeFiles = @('coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll', 'System.Private.CoreLib.dll')
foreach ($runtimeFile in $runtimeFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $PublishDirectory $runtimeFile) -PathType Leaf)) {
        throw "The Store MSIX payload must be self-contained; '$runtimeFile' is missing from $PublishDirectory."
    }
}

$packageAssetsDirectory = Join-Path $packagingRoot 'Assets'
if (-not (Test-Path -LiteralPath $packageAssetsDirectory -PathType Container)) {
    throw "The supplied package asset directory was not found: $packageAssetsDirectory"
}

$packageAssetFiles = @(Get-ChildItem -LiteralPath $packageAssetsDirectory -File -Filter '*.png')
$packageAssetNames = @($packageAssetFiles.Name)
$requiredAssetFiles = @(
    'Square44x44Logo.png',
    'Square150x150Logo.png',
    'StoreLogo.png'
)
foreach ($requiredAssetFile in $requiredAssetFiles) {
    if ($requiredAssetFile -notin $packageAssetNames) {
        throw "Required supplied package asset is missing: $packageAssetsDirectory\$requiredAssetFile"
    }
}

$requiredAssetFamilies = @(
    @{ Label = 'Square44x44Logo scale assets'; Pattern = '^Square44x44Logo(?:\.scale-\d+)?\.png$' },
    @{ Label = 'Square150x150Logo scale assets'; Pattern = '^Square150x150Logo(?:\.scale-\d+)?\.png$' },
    @{ Label = 'StoreLogo scale assets'; Pattern = '^StoreLogo(?:\.scale-\d+)?\.png$' },
    @{ Label = 'Square44x44Logo target-size default assets'; Pattern = '^Square44x44Logo\.targetsize-\d+\.png$' },
    @{ Label = 'Square44x44Logo target-size unplated assets'; Pattern = '^Square44x44Logo\.targetsize-\d+_altform-unplated\.png$' },
    @{ Label = 'Square44x44Logo target-size light-unplated assets'; Pattern = '^Square44x44Logo\.targetsize-\d+_altform-lightunplated\.png$' },
    @{ Label = 'Medium tile assets'; Pattern = '^MedTile(?:\.scale-\d+)?\.png$' }
)
foreach ($requiredAssetFamily in $requiredAssetFamilies) {
    $matchingAssets = @($packageAssetNames | Where-Object { $_ -match $requiredAssetFamily.Pattern })
    if ($matchingAssets.Count -eq 0) {
        throw "Required supplied package asset family is missing ($($requiredAssetFamily.Label)): $packageAssetsDirectory\$($requiredAssetFamily.Pattern)"
    }
}

$manifestTemplatePath = Join-Path $packagingRoot 'AppxManifest.xml'
$manifestTemplate = Get-Content -LiteralPath $manifestTemplatePath -Raw
[xml]$manifestXml = $manifestTemplate
$identity = $manifestXml.Package.Identity
if ($identity.Name -ne '404Builds.HeadsUpCI') {
    throw "Unexpected package identity name '$($identity.Name)'."
}
if ($identity.Publisher -ne 'CN=2BB531F8-77DD-4D82-AA5F-230F26EB09EE') {
    throw "Unexpected package publisher '$($identity.Publisher)'."
}
if ($manifestXml.Package.Properties.PublisherDisplayName -ne '404 Builds') {
    throw "Unexpected publisher display name '$($manifestXml.Package.Properties.PublisherDisplayName)'."
}
$identity.Version = $PackageVersion

$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$stagingDirectory = Join-Path $OutputDirectory '.HeadsUp-msix-staging'
$packagePath = Join-Path $OutputDirectory "HeadsUp-$PackageVersion.msix"
if (Test-Path -LiteralPath $stagingDirectory) {
    Remove-Item -LiteralPath $stagingDirectory -Recurse -Force
}
if (Test-Path -LiteralPath $packagePath) {
    Remove-Item -LiteralPath $packagePath -Force
}
New-Item -ItemType Directory -Force -Path $stagingDirectory, (Join-Path $stagingDirectory 'Assets') | Out-Null

Get-ChildItem -LiteralPath $PublishDirectory -Force | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $stagingDirectory $_.Name) -Recurse -Force
}

foreach ($packageAssetFile in $packageAssetFiles) {
    $assetDestination = Join-Path (Join-Path $stagingDirectory 'Assets') $packageAssetFile.Name
    Copy-Item -LiteralPath $packageAssetFile.FullName `
        -Destination $assetDestination -Force
}

$writerSettings = [System.Xml.XmlWriterSettings]::new()
$writerSettings.Encoding = [System.Text.UTF8Encoding]::new($false)
$writerSettings.Indent = $true
$writer = [System.Xml.XmlWriter]::Create((Join-Path $stagingDirectory 'AppxManifest.xml'), $writerSettings)
try {
    $manifestXml.Save($writer)
}
finally {
    $writer.Dispose()
}

function Find-WindowsSdkTool([string]$Name) {
    $command = Get-Command $Name -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -ne $command) {
        return $command.Source
    }

    $roots = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'),
        (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\App Certification Kit'),
        (Join-Path $env:ProgramFiles 'Windows Kits\10\bin')
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and (Test-Path -LiteralPath $_) }
    $candidates = foreach ($root in $roots) {
        Get-ChildItem -LiteralPath $root -Filter $Name -Recurse -File -ErrorAction SilentlyContinue
    }
    $candidate = $candidates | Where-Object { $_.FullName -match '\\x64\\' } | Sort-Object FullName | Select-Object -Last 1
    if ($null -eq $candidate) {
        $candidate = $candidates | Sort-Object FullName | Select-Object -Last 1
    }
    if ($null -eq $candidate) {
        throw "$Name was not found. Install the Windows 10/11 SDK to package HeadsUp."
    }
    return $candidate.FullName
}

$makePri = Find-WindowsSdkTool 'makepri.exe'
$priConfig = Join-Path $stagingDirectory 'priconfig.xml'
$resourcesPri = Join-Path $stagingDirectory 'resources.pri'

& $makePri createconfig /cf $priConfig /dq en-US /o
if ($LASTEXITCODE -ne 0) {
    throw "MakePri createconfig failed with exit code $LASTEXITCODE."
}
if (-not (Test-Path -LiteralPath $priConfig -PathType Leaf)) {
    throw "MakePri did not create the PRI configuration: $priConfig"
}

& $makePri new /pr $stagingDirectory /cf $priConfig /of $resourcesPri /o
if ($LASTEXITCODE -ne 0) {
    throw "MakePri resource indexing failed with exit code $LASTEXITCODE."
}
if (-not (Test-Path -LiteralPath $resourcesPri -PathType Leaf)) {
    throw "MakePri did not create the required resource index: $resourcesPri"
}

Remove-Item -LiteralPath $priConfig -Force

$makeAppx = Find-WindowsSdkTool 'makeappx.exe'
& $makeAppx pack /o /h SHA256 /d $stagingDirectory /p $packagePath
if ($LASTEXITCODE -ne 0) {
    throw "MakeAppx failed with exit code $LASTEXITCODE."
}

$validationDirectory = Join-Path $OutputDirectory '.HeadsUp-msix-validation'
if (Test-Path -LiteralPath $validationDirectory) {
    Remove-Item -LiteralPath $validationDirectory -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $validationDirectory | Out-Null
try {
    & $makeAppx unpack /o /p $packagePath /d $validationDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "MakeAppx package validation failed with exit code $LASTEXITCODE."
    }

    [xml]$packagedManifest = Get-Content -LiteralPath (Join-Path $validationDirectory 'AppxManifest.xml') -Raw
    $packagedIdentity = $packagedManifest.Package.Identity
    if ($packagedIdentity.Name -ne '404Builds.HeadsUpCI' -or
        $packagedIdentity.Publisher -ne 'CN=2BB531F8-77DD-4D82-AA5F-230F26EB09EE' -or
        $packagedIdentity.Version -ne $PackageVersion -or
        $packagedManifest.Package.Properties.PublisherDisplayName -ne '404 Builds') {
        throw 'The generated MSIX does not contain the expected Partner Center identity.'
    }
    if (-not (Test-Path -LiteralPath (Join-Path $validationDirectory 'resources.pri') -PathType Leaf)) {
        throw 'The generated MSIX is missing resources.pri; qualified assets cannot be resolved without the package resource index.'
    }
    if (Test-Path -LiteralPath (Join-Path $validationDirectory 'priconfig.xml')) {
        throw 'The generated MSIX must not contain the temporary priconfig.xml file.'
    }
}
finally {
    Remove-Item -LiteralPath $validationDirectory -Recurse -Force -ErrorAction SilentlyContinue
}

if (-not [string]::IsNullOrWhiteSpace($CertificatePath)) {
    $signtool = Find-WindowsSdkTool 'signtool.exe'
    $signArguments = @('sign', '/fd', 'SHA256', '/a', '/f', $CertificatePath)
    if (-not [string]::IsNullOrWhiteSpace($CertificatePassword)) {
        $signArguments += @('/p', $CertificatePassword)
    }
    $signArguments += $packagePath
    & $signtool @signArguments
    if ($LASTEXITCODE -ne 0) {
        throw "SignTool failed with exit code $LASTEXITCODE."
    }
}

Remove-Item -LiteralPath $stagingDirectory -Recurse -Force
Write-Host "MSIX package: $packagePath"
