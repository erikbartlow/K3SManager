function Assert-LinuxArm64DockerBuilder {
    param(
        [string]$ImageDescription = 'ARM64 images'
    )

    $dockerPlatform = (& docker info --format '{{.OSType}}/{{.Architecture}}' 2>$null)
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($dockerPlatform)) {
        throw 'Unable to read Docker engine information.'
    }

    $dockerPlatform = $dockerPlatform.Trim()
    $builderInfo = (& docker buildx inspect --bootstrap 2>$null)
    if ($LASTEXITCODE -eq 0 -and ($builderInfo -match 'Platforms:\s*(.*linux/arm64.*)')) {
        return
    }

    $builders = (& docker buildx ls 2>$null)
    if ($LASTEXITCODE -eq 0) {
        foreach ($builderLine in $builders) {
            if ($builderLine -match '^(?<name>[^\s*]+)\*?\s+' -and $builderLine -notmatch '^\s') {
                $builderName = $Matches.name.TrimEnd('*')
                if (-not [string]::IsNullOrWhiteSpace($builderName)) {
                    $candidateInfo = (& docker buildx inspect $builderName --bootstrap 2>$null)
                    if ($LASTEXITCODE -eq 0 -and ($candidateInfo -match 'Platforms:\s*(.*linux/arm64.*)')) {
                        Write-Host "Selecting Docker buildx builder '$builderName' for linux/arm64..." -ForegroundColor Cyan
                        & docker buildx use $builderName
                        if ($LASTEXITCODE -ne 0) {
                            throw "Unable to select Docker buildx builder '$builderName'."
                        }
                        return
                    }
                }
            }
        }
    }

    throw "No active Docker buildx builder supports linux/arm64. Docker engine is '$dockerPlatform'; run 'docker buildx ls' and select or create a builder with linux/arm64 support before building $ImageDescription."
}
