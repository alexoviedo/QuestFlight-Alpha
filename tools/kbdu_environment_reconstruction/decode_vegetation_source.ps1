[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ArtifactDir,

    [string]$SourceImage = "QuestFlight/Assets/Production/Environment/Textures/KBDU_NAIP_2019_Mid12km_4096.jpg",

    [ValidateRange(256, 2048)]
    [int]$OutputSize = 1024
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$sourcePath = (Resolve-Path (Join-Path $repoRoot $SourceImage)).Path
$artifactPath = [System.IO.Path]::GetFullPath($ArtifactDir)
if ($artifactPath.StartsWith($repoRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase) -or
    $artifactPath.Equals($repoRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "ArtifactDir must remain external to the product repository: $artifactPath"
}
[System.IO.Directory]::CreateDirectory($artifactPath) | Out-Null

Add-Type -AssemblyName System.Drawing
$bitmap = [System.Drawing.Bitmap]::FromFile($sourcePath)
try {
    if ($bitmap.Width -ne 4096 -or $bitmap.Height -ne 4096) {
        throw "Expected the committed 4096x4096 mid-zone source; observed $($bitmap.Width)x$($bitmap.Height)"
    }
    if (($bitmap.Width % $OutputSize) -ne 0 -or ($bitmap.Height % $OutputSize) -ne 0) {
        throw "OutputSize must evenly divide the source dimensions"
    }
    $stride = [int]($bitmap.Width / $OutputSize)
    $pixels = [byte[]]::new($OutputSize * $OutputSize * 3)
    $offset = 0
    for ($y = 0; $y -lt $OutputSize; $y++) {
        $sourceY = [Math]::Min($bitmap.Height - 1, $y * $stride + [int]($stride / 2))
        for ($x = 0; $x -lt $OutputSize; $x++) {
            $sourceX = [Math]::Min($bitmap.Width - 1, $x * $stride + [int]($stride / 2))
            $color = $bitmap.GetPixel($sourceX, $sourceY)
            $pixels[$offset] = $color.R
            $pixels[$offset + 1] = $color.G
            $pixels[$offset + 2] = $color.B
            $offset += 3
        }
    }
}
finally {
    $bitmap.Dispose()
}

$ppmPath = Join-Path $artifactPath "KBDU_NAIP_2019_Mid12km_1024.ppm"
$header = [System.Text.Encoding]::ASCII.GetBytes("P6`n$OutputSize $OutputSize`n255`n")
$stream = [System.IO.File]::Open($ppmPath, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write)
try {
    $stream.Write($header, 0, $header.Length)
    $stream.Write($pixels, 0, $pixels.Length)
}
finally {
    $stream.Dispose()
}

$sourceHash = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash.ToLowerInvariant()
$ppmHash = (Get-FileHash -LiteralPath $ppmPath -Algorithm SHA256).Hash.ToLowerInvariant()
$receipt = [ordered]@{
    schema_version = 1
    source_path = $SourceImage.Replace("\", "/")
    source_sha256 = $sourceHash
    source_license = "United States government work / public domain; USGS, USDA, The National Map: Orthoimagery"
    source_raster_function = "NaturalColor; NIR unavailable in committed runtime derivative"
    decoding = "System.Drawing JPEG decode; deterministic center sample of each 4x4 source-pixel block"
    output_path = $ppmPath.Replace("\", "/")
    output_dimensions = @($OutputSize, $OutputSize)
    output_bytes = (Get-Item -LiteralPath $ppmPath).Length
    output_sha256 = $ppmHash
}
$receiptPath = Join-Path $artifactPath "vegetation_source_decode_receipt.json"
$receipt | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $receiptPath -Encoding utf8NoBOM

Write-Output $ppmPath
Write-Output $receiptPath
