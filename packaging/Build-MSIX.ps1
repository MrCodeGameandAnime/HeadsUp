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

$versionParts = $PackageVersion.Split('.')
if ($versionParts.Count -ne 4 -or $PackageVersion -notmatch '^\d+(\.\d+){3}$' -or
    @($versionParts | ForEach-Object { [int]$_ } | Where-Object { $_ -gt 65535 }).Count -gt 0) {
    throw "Package version '$PackageVersion' must be a four-part MSIX version between 0.0.0.0 and 65535.65535.65535.65535."
}

if (-not (Test-Path -LiteralPath $PublishDirectory -PathType Container)) {
    throw "Publish directory was not found: $PublishDirectory"
}
if (-not (Test-Path -LiteralPath (Join-Path $PublishDirectory 'HeadsUp.exe') -PathType Leaf)) {
    throw "The publish directory does not contain HeadsUp.exe: $PublishDirectory"
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

$logoSource = Join-Path $repositoryRoot 'src\GitCiHud\Assets\headsup-logo.png'
if (-not (Test-Path -LiteralPath $logoSource -PathType Leaf)) {
    throw "Canonical HeadsUp logo was not found: $logoSource"
}

Add-Type -AssemblyName System.Drawing
$logoImage = [System.Drawing.Image]::FromFile($logoSource)
try {
    $logoTargets = @(
        @{ Name = 'Square44x44Logo.png'; Size = 44 },
        @{ Name = 'Square44x44Logo.scale-200.png'; Size = 88 },
        @{ Name = 'Square44x44Logo.scale-400.png'; Size = 176 },
        @{ Name = 'Square150x150Logo.png'; Size = 150 },
        @{ Name = 'Square150x150Logo.scale-200.png'; Size = 300 },
        @{ Name = 'Square150x150Logo.scale-400.png'; Size = 600 },
        @{ Name = 'StoreLogo.png'; Size = 50 }
    )
    foreach ($target in $logoTargets) {
        $bitmap = [System.Drawing.Bitmap]::new($target.Size, $target.Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
            $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
            $graphics.DrawImage($logoImage, 0, 0, $target.Size, $target.Size)
            $bitmap.Save((Join-Path $stagingDirectory 'Assets' $target.Name), [System.Drawing.Imaging.ImageFormat]::Png)
        }
        finally {
            $graphics.Dispose()
            $bitmap.Dispose()
        }
    }
}
finally {
    $logoImage.Dispose()
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
