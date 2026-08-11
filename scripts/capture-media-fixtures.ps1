<#
.SYNOPSIS
    Capture les réponses des services média pendant qu'un téléchargement progresse.

.DESCRIPTION
    Un instantané unique ne sert à rien : les champs qui portent la corrélation
    (trackedDownloadState, trackedDownloadStatus, statusMessages, timeleft) ne prennent leurs
    valeurs intéressantes qu'en transition. Un enregistrement figé en « downloading » ne dit
    rien de ce qui se passe quand un import bloque — et c'est précisément ce que le module doit
    détecter.

    Ce script échantillonne en continu et n'écrit un fichier que lorsqu'une signature d'état
    change. On obtient la séquence des transitions, pas des centaines de doublons.

    Aucune clé n'est stockée ici : elles sont passées en paramètres.

.EXAMPLE
    ./capture-media-fixtures.ps1 -OutputDirectory ./capture `
        -RadarrUrl http://192.168.1.233:7878 -RadarrApiKey xxx `
        -SonarrUrl http://192.168.1.232:8989 -SonarrApiKey yyy `
        -SeerrUrl  http://192.168.1.231:5055 -SeerrApiKey  zzz `
        -QBittorrentUrl http://192.168.1.240:8090 -DurationMinutes 45
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$RadarrUrl,
    [string]$RadarrApiKey,
    [string]$SonarrUrl,
    [string]$SonarrApiKey,
    [string]$SeerrUrl,
    [string]$SeerrApiKey,
    [string]$QBittorrentUrl,
    [int]$IntervalSeconds = 5,
    [int]$DurationMinutes = 30
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null

$deadline = (Get-Date).AddMinutes($DurationMinutes)
$sequence = 0
$lastSignature = @{}

function Read-Endpoint {
    param([string]$Url, [hashtable]$Headers)
    try {
        return (Invoke-WebRequest $Url -Headers $Headers -UseBasicParsing -TimeoutSec 20).Content
    }
    catch {
        # Un service qui hoquette ne doit pas interrompre la capture : on saute ce cycle.
        return $null
    }
}

<#
    Signature d'un état. Volontairement construite à partir des seuls champs dont la variation
    nous intéresse : si seul « timeleft » bouge, on n'écrit pas un fichier toutes les cinq
    secondes, mais on capte bien le passage de downloading à importPending.
#>
function Get-Signature {
    param([string]$Source, [string]$Json)

    if (-not $Json) { return $null }
    try { $parsed = $Json | ConvertFrom-Json } catch { return $null }

    switch ($Source) {
        'qbittorrent' {
            return (($parsed | ForEach-Object { "$($_.hash):$($_.state):$([math]::Round($_.progress, 2))" }) -join '|')
        }
        'seerr' {
            return (($parsed.results | ForEach-Object {
                "$($_.id):$($_.status):$($_.media.status):$($_.media.downloadStatus.Count)"
            }) -join '|')
        }
        default {
            # Radarr et Sonarr : enveloppe paginée.
            return (($parsed.records | ForEach-Object {
                "$($_.downloadId):$($_.status):$($_.trackedDownloadState):$($_.trackedDownloadStatus):$($_.statusMessages.Count):$($_.errorMessage)"
            }) -join '|')
        }
    }
}

function Save-IfChanged {
    param([string]$Source, [string]$Json)

    $signature = Get-Signature -Source $Source -Json $Json
    if ($null -eq $signature) { return }
    if ($lastSignature[$Source] -eq $signature) { return }

    $lastSignature[$Source] = $signature
    $script:sequence++

    $stamp = (Get-Date).ToString('HHmmss')
    $name = '{0:d3}-{1}-{2}.json' -f $script:sequence, $Source, $stamp
    [IO.File]::WriteAllText((Join-Path $OutputDirectory $name), $Json, [Text.UTF8Encoding]::new($false))

    Write-Output "[$((Get-Date).ToString('HH:mm:ss'))] $name"
    if ($signature) { Write-Output "    $($signature.Substring(0, [Math]::Min(160, $signature.Length)))" }
}

Write-Output "Capture démarrée, fin prévue à $($deadline.ToString('HH:mm:ss')), échantillonnage toutes les $IntervalSeconds s."
Write-Output "Un fichier n'est écrit que lorsqu'un état change."

while ((Get-Date) -lt $deadline) {
    if ($RadarrUrl) {
        Save-IfChanged 'radarr-queue' (Read-Endpoint `
            "$RadarrUrl/api/v3/queue?pageSize=100&includeUnknownMovieItems=true&includeMovie=true" `
            @{ 'X-Api-Key' = $RadarrApiKey })
    }
    if ($SonarrUrl) {
        Save-IfChanged 'sonarr-queue' (Read-Endpoint `
            "$SonarrUrl/api/v3/queue?pageSize=100&includeUnknownSeriesItems=true&includeSeries=true&includeEpisode=true" `
            @{ 'X-Api-Key' = $SonarrApiKey })
    }
    if ($QBittorrentUrl) {
        Save-IfChanged 'qbittorrent' (Read-Endpoint "$QBittorrentUrl/api/v2/torrents/info" $null)
    }
    if ($SeerrUrl) {
        # Seerr expose un downloadStatus corrélé de son côté : intéressant à confronter au nôtre.
        Save-IfChanged 'seerr' (Read-Endpoint `
            "$SeerrUrl/api/v1/request?take=20&skip=0&sort=modified" `
            @{ 'X-Api-Key' = $SeerrApiKey })
    }

    Start-Sleep -Seconds $IntervalSeconds
}

Write-Output "Capture terminée : $sequence instantané(s) dans $OutputDirectory."
