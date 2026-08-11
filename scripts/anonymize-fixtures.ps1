<#
.SYNOPSIS
    Anonymise des réponses capturées, sans altérer leur structure.

.DESCRIPTION
    Les fixtures n'ont d'intérêt que si elles sont fidèles à ce que les services renvoient
    réellement. Ce script opère donc exclusivement par substitution de texte : il ne parse
    jamais le JSON et ne le resérialise jamais.

    C'est une leçon payée : une première version passait par ConvertFrom-Json / ConvertTo-Json
    de PowerShell, qui déballe silencieusement les tableaux à un seul élément. Un
    « "records": [ { … } ] » devenait « "records": { … } », et les fixtures ne représentaient
    plus rien — panne d'autant plus vicieuse qu'elles restaient du JSON parfaitement valide.

    Ce qui est retiré : titres de médias, chemins, URL d'indexeurs et de trackers, adresses IP
    privées, noms d'instances, slugs. Ce qui est conservé : tmdbId et tvdbId, références
    publiques qui ne révèlent rien de l'installation.

    Les hashs de torrents sont réécrits, pas supprimés — un infohash identifie une release
    précise. La réécriture est stable : un même hash réel donne toujours le même hash
    synthétique, dans tous les fichiers, ce qui préserve la jointure que les tests exercent.

.EXAMPLE
    ./anonymize-fixtures.ps1 -SourceDirectory ./capture -DestinationDirectory ./Fixtures
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$SourceDirectory,
    [Parameter(Mandatory)][string]$DestinationDirectory,
    [hashtable]$Rename = @{}
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $DestinationDirectory | Out-Null

# Champs dont la valeur révèle la bibliothèque, l'arborescence du NAS ou l'infrastructure.
$sensitiveFields = @(
    'name', 'sourceTitle', 'title', 'originalTitle', 'cleanTitle', 'sortTitle', 'releaseGroup',
    'indexer', 'folderName', 'path', 'relativePath', 'fileName', 'overview', 'comment',
    'droppedPath', 'importedPath', 'outputPath', 'content_path', 'save_path', 'download_path',
    'root_path', 'tracker', 'magnet_uri', 'nzbInfoUrl', 'downloadClientName', 'instanceName',
    'titleSlug', 'externalServiceSlug', 'externalServiceSlug4k', 'website', 'youTubeTrailerId',
    'network', 'imageLink', 'remoteUrl', 'url', 'mediaUrl', 'serviceUrl', 'label'
)

$hashes = @{}
$texts = @{}
$script:hashSeed = 0
$script:textSeed = 0

function Get-FakeHash {
    param([string]$Real)
    $key = $Real.ToLowerInvariant()
    if (-not $hashes.ContainsKey($key)) {
        $script:hashSeed++
        $hashes[$key] = ('{0:x2}' -f $script:hashSeed) * 20
    }
    if ($Real -cmatch '^[0-9A-F]+$') { return $hashes[$key].ToUpperInvariant() }
    return $hashes[$key]
}

function Get-FakeText {
    param([string]$Real)
    if ([string]::IsNullOrWhiteSpace($Real)) { return $Real }
    if (-not $texts.ContainsKey($Real)) {
        $script:textSeed++
        $texts[$Real] = "Exemple.Media.$($script:textSeed).2024.1080p.WEB-DL-GROUPE"
    }
    return $texts[$Real]
}

foreach ($file in Get-ChildItem $SourceDirectory -Filter *.json) {
    if ($file.Length -le 3) { continue }

    $json = [IO.File]::ReadAllText($file.FullName)

    # 1. Hashs de torrents, en préservant la casse d'origine (majuscules côté *arr).
    $json = [regex]::Replace($json, '(?<![0-9a-zA-Z])[0-9a-fA-F]{40}(?![0-9a-zA-Z])', {
        param($m) Get-FakeHash $m.Value
    })

    # 2. Valeurs des champs sensibles. Le motif gère les guillemets échappés.
    foreach ($field in $sensitiveFields) {
        $pattern = '("' + [regex]::Escape($field) + '"\s*:\s*)"((?:[^"\\]|\\.)*)"'
        $json = [regex]::Replace($json, $pattern, {
            param($m) "$($m.Groups[1].Value)""$(Get-FakeText $m.Groups[2].Value)"""
        })
    }

    # 3. URL, adresses privées et chemins absolus, où qu'ils se trouvent.
    $json = [regex]::Replace($json, 'https?://(?:[^"\\\s]|\\.)*', 'http://service.exemple.invalid/chemin')
    $json = [regex]::Replace($json, '\b(?:192\.168|10\.\d{1,3})\.\d{1,3}\.\d{1,3}\b', '203.0.113.10')
    $json = [regex]::Replace($json, '"(/(?:[^"\\]|\\.)*)"', '"/data/media/exemple"')
    $json = [regex]::Replace($json, '"([A-Za-z]:\\\\(?:[^"\\]|\\.)*)"', '"/data/media/exemple"')

    $target = if ($Rename.ContainsKey($file.Name)) { $Rename[$file.Name] } else { $file.Name }
    [IO.File]::WriteAllText((Join-Path $DestinationDirectory $target), $json, [Text.UTF8Encoding]::new($false))
}

Write-Output "$($texts.Count) textes et $($hashes.Count) hashs remplacés."
Write-Output "Structure préservée : aucun parse, aucune resérialisation."
