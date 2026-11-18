# Publie une nouvelle version de Stretch Switch sur GitHub.
# Usage :  .\publier.ps1 1.3.1 "Ce qui change dans cette version"
# Les potes qui ont l'app reçoivent la mise à jour au prochain démarrage.
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$Notes = ""
)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$gh = 'C:\Program Files\GitHub CLI\gh.exe'
if (-not (Test-Path $gh)) { $gh = 'gh' }

if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version invalide : $Version (ex : 1.3.1)" }
$repo = (Get-Content github-repo.txt -Raw).Trim()

# 1. numéro de version dans le code
$theme = 'src\Theme.cs'
$code = [IO.File]::ReadAllText((Resolve-Path $theme))
$code = [regex]::Replace($code, 'public const string Version = "[^"]+";', "public const string Version = `"$Version`";")
$code = [regex]::Replace($code, 'public const string GitHubRepo = "[^"]*";', "public const string GitHubRepo = `"$repo`";")
[IO.File]::WriteAllText((Resolve-Path $theme), $code, (New-Object Text.UTF8Encoding $true))

# 2. compilation + zip
& cmd /c build.bat
if ($LASTEXITCODE -ne 0) { throw "La compilation a échoué." }
Compress-Archive -Force -Path StretchSwitch.exe, LISEZ-MOI.txt -DestinationPath StretchSwitch.zip

# 3. code source sur GitHub
git add -A
git commit -m "Version $Version" | Out-Null
git push origin HEAD

# 4. release (l'app télécharge StretchSwitch.exe, les potes le zip)
if ($Notes -eq "") { $Notes = "Version $Version" }
& $gh release create "v$Version" StretchSwitch.exe StretchSwitch.zip --repo $repo --title "Stretch Switch $Version" --notes $Notes
if ($LASTEXITCODE -ne 0) { throw "La publication a échoué." }

Write-Host ""
Write-Host "Version $Version publiée."
Write-Host "Lien à envoyer sur Discord : https://github.com/$repo/releases/latest/download/StretchSwitch.zip"
