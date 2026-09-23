# k3smanager-build:name=Web
# Follows the chorli / GoogleCalendarPi Deployment build/tag/push workflow.
[CmdletBinding(SupportsShouldProcess)]
param(
    [switch]$Login
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$sourceRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

$images = @(
    @{
        Name       = 'K3S Manager Web'
        Project    = 'src/K3SManager.Web/K3SManager.Web.csproj'
        Dockerfile = 'Deployment/Dockerfile_Web_Arm64'
        Repository = 'codedthought/k3smanager-web'
    }
)

function Invoke-CheckedCommand {
    param(
        [Parameter(Mandatory)]
        [string]$Command,

        [Parameter(Mandatory)]
        [string[]]$Arguments
    )

    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $Command $($Arguments -join ' ')"
    }
}

<#
    This solution keeps <Version> in Directory.Build.props so every assembly is stamped from one
    place, so the project file is checked first and the props file is the fallback. Everything else
    about the tag is the usual convention: localTag <prefix>/<repo-leaf>:<version>-arm64 and
    remoteTag <repo>:v<version>-arm64.
#>
function Get-ProjectVersion {
    param(
        [Parameter(Mandatory)]
        [string]$ProjectPath,

        [Parameter(Mandatory)]
        [string]$SourceRoot
    )

    $candidates = @($ProjectPath, (Join-Path $SourceRoot 'Directory.Build.props'))

    foreach ($candidate in $candidates) {
        if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            continue
        }

        [xml]$xml = Get-Content -LiteralPath $candidate -Raw

        # SelectSingleNode returns $null when the element is absent. Dotted property access
        # ($xml.Project.PropertyGroup.Version) throws PropertyNotFoundException instead under
        # Set-StrictMode -Version Latest, which is exactly the case this fallback exists to
        # handle: K3SManager.Web.csproj carries no <Version> of its own.
        $node = $xml.SelectSingleNode('/Project/PropertyGroup/Version')

        if ($null -ne $node -and -not [string]::IsNullOrWhiteSpace($node.InnerText)) {
            return $node.InnerText.Trim()
        }
    }

    throw "No <Version> value was found in $ProjectPath or in Directory.Build.props."
}

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw 'Docker was not found on PATH.'
}

. (Join-Path $PSScriptRoot 'docker-buildx-arm64.ps1')

Invoke-CheckedCommand -Command 'docker' -Arguments @('version')
Assert-LinuxArm64DockerBuilder -ImageDescription 'the ARM64 K3S Manager image'

if ($Login) {
    Invoke-CheckedCommand -Command 'docker' -Arguments @('login', 'docker.io')
}

Push-Location $sourceRoot
try {
    foreach ($image in $images) {
        $projectPath = Join-Path $sourceRoot $image.Project
        $dockerfilePath = Join-Path $sourceRoot $image.Dockerfile

        if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
            throw "Project file was not found: $projectPath"
        }
        if (-not (Test-Path -LiteralPath $dockerfilePath -PathType Leaf)) {
            throw "Dockerfile was not found: $dockerfilePath"
        }

        $version = Get-ProjectVersion -ProjectPath $projectPath -SourceRoot $sourceRoot
        if ($version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$' -or $version.Length -gt 121) {
            throw "Version '$version' must be a Docker-tag-compatible version such as 1.0.0 or 1.0.0-rc.1 (without a v prefix)."
        }

        $localTag = "k3smanager-build/$($image.Repository.Split('/')[-1]):$version-arm64"
        $remoteTag = "$($image.Repository):v$version-arm64"

        if (-not $PSCmdlet.ShouldProcess($remoteTag, "Build, tag, and push the ARM64 $($image.Name) image")) {
            continue
        }

        Write-Host "`nBuilding $($image.Name) version $version..." -ForegroundColor Cyan
        Invoke-CheckedCommand -Command 'docker' -Arguments @(
            'buildx', 'build',
            '--platform', 'linux/arm64',
            '--file', $dockerfilePath,
            '--build-arg', "PROJECT_VERSION=$version",
            '--tag', $localTag,
            '--load',
            $sourceRoot
        )

        Write-Host "Tagging $remoteTag..." -ForegroundColor Cyan
        Invoke-CheckedCommand -Command 'docker' -Arguments @(
            'tag', $localTag, $remoteTag
        )

        Write-Host "Pushing $remoteTag..." -ForegroundColor Cyan
        Invoke-CheckedCommand -Command 'docker' -Arguments @(
            'push', $remoteTag
        )

        Write-Host "Published $remoteTag" -ForegroundColor Green
    }
}
finally {
    Pop-Location
}

Write-Host "`nAll K3S Manager ARM64 images were built and pushed successfully." -ForegroundColor Green
