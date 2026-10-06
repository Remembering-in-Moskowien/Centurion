# One-time migration: rename existing downloaded models to content-addressed (SHA-256) storage.
#  - single-file models  -> models/<category>/<sha256>.<ext>
#  - directory models    -> models/<category>/<aggregate-sha256>/  (member files keep names)
# A per-category manifest (models/<category>/.manifest.json) maps logical names to hashes,
# matching the layout produced by ModelManager/RapidOcrModelManager.
#
# The aggregate hash must match Centurion.Core.Infrastructure.ContentHasher.ComputeAggregateSha256:
# each member = relative path (forward slashes) + ":" + file sha256, lines sorted ordinal, joined
# with a trailing newline per line, whole payload UTF-8 (no BOM), SHA-256 lowercase hex.
#
# Usage: powershell -ExecutionPolicy Bypass -File scripts\migrate-models-content-hash.ps1 [-ModelsRoot <path>]

param(
    [string]$ModelsRoot = "E:\Centurion\src\Centurion.Cli\bin\Debug\net10.0\models"
)

$ErrorActionPreference = 'Stop'

function Get-Sha256([string]$path) {
    (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash.ToLowerInvariant()
}

function Get-AggregateSha256([string]$dir) {
    $files = Get-ChildItem -LiteralPath $dir -Recurse -File
    if ($files.Count -eq 0) { throw "No files under $dir" }
    $pairs = @()
    foreach ($f in $files) {
        $rel = $f.FullName.Substring($dir.Length).TrimStart('\', '/').Replace('\', '/')
        $pairs += "$rel`:" + (Get-Sha256 $f.FullName)
    }
    $sorted = [string[]]$pairs
    [Array]::Sort($sorted, [StringComparer]::Ordinal)
    $canonical = ($sorted | ForEach-Object { "$_`n" }) -join ''
    $bytes = [Text.Encoding]::UTF8.GetBytes($canonical)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $hashBytes = $sha.ComputeHash($bytes) } finally { $sha.Dispose() }
    ($hashBytes | ForEach-Object { $_.ToString('x2') }) -join ''
}

function ConvertTo-Hashtable($obj) {
    if ($obj -is [System.Management.Automation.PSCustomObject]) {
        $h = @{}
        foreach ($p in $obj.PSObject.Properties) { $h[$p.Name] = ConvertTo-Hashtable $p.Value }
        return $h
    }
    if ($obj -is [System.Array]) {
        return @($obj | ForEach-Object { ConvertTo-Hashtable $_ })
    }
    return $obj
}

function Read-Manifest([string]$catDir) {
    $path = Join-Path $catDir '.manifest.json'
    if (-not (Test-Path $path)) { return @{} }
    return ConvertTo-Hashtable (Get-Content $path -Raw | ConvertFrom-Json)
}

function Write-Manifest([string]$catDir, $manifest) {
    if ($manifest.Count -eq 0) { return }
    $json = $manifest | ConvertTo-Json -Depth 6
    $path = Join-Path $catDir '.manifest.json'
    [IO.File]::WriteAllText($path, $json, (New-Object Text.UTF8Encoding($false)))
    Write-Host "  manifest: $path"
}

function Add-SingleFileEntry([string]$category, [string]$oldName, [string]$key) {
    $catDir = Join-Path $ModelsRoot $category
    $old = Join-Path $catDir $oldName
    if (-not (Test-Path -LiteralPath $old)) { return }
    $hash = Get-Sha256 $old
    $ext = [IO.Path]::GetExtension($oldName).TrimStart('.')
    $new = Join-Path $catDir "$hash.$ext"
    if (Test-Path -LiteralPath $new) { Remove-Item -LiteralPath $old -Force }
    else { Move-Item -LiteralPath $old $new }
    $manifest = Read-Manifest $catDir
    $manifest[$key] = @{ Kind = 'file'; Hash = $hash; Ext = $ext; FileName = $oldName }
    Write-Manifest $catDir $manifest
    Write-Host "  [$category] $oldName -> $hash.$ext"
}

function Add-DirectoryEntry([string]$category, [string]$oldDirName, [string]$key) {
    $catDir = Join-Path $ModelsRoot $category
    $oldDir = Join-Path $catDir $oldDirName
    if (-not (Test-Path -LiteralPath $oldDir)) { return }
    $hash = Get-AggregateSha256 $oldDir
    $finalDir = Join-Path $catDir $hash
    if (Test-Path -LiteralPath $finalDir) { Remove-Item -LiteralPath $oldDir -Recurse -Force }
    else { Move-Item -LiteralPath $oldDir $finalDir }
    $relFiles = @(Get-ChildItem -LiteralPath $finalDir -Recurse -File |
        ForEach-Object { $_.FullName.Substring($finalDir.Length).TrimStart('\', '/').Replace('\', '/') })
    $manifest = Read-Manifest $catDir
    $manifest[$key] = @{ Kind = 'dir'; Hash = $hash; Files = @($relFiles) }
    Write-Manifest $catDir $manifest
    Write-Host "  [$category] $oldDirName -> $hash\ ($($relFiles.Count) files)"
}

if (-not (Test-Path -LiteralPath $ModelsRoot)) {
    Write-Error "Models root not found: $ModelsRoot"
    exit 1
}

Write-Host "Migrating models under $ModelsRoot"

# ---- Single-file categories ----
Add-SingleFileEntry 'whispercpp'  'ggml-tiny.bin'                            'tiny'
Add-SingleFileEntry 'whispercpp'  'ggml-base.bin'                            'base'
Add-SingleFileEntry 'whispercpp'  'ggml-small.bin'                           'small'
Add-SingleFileEntry 'whispercpp'  'ggml-medium.bin'                          'medium'
Add-SingleFileEntry 'whispercpp'  'ggml-large-v3.bin'                        'large'
Add-SingleFileEntry 'qwen3asr'    'qwen3-asr-0.6b-q4_k.gguf'                'qwen3-asr-0.6b'
Add-SingleFileEntry 'qwen3asr'    'qwen3-asr-1.7b-q4_k.gguf'                'qwen3-asr-1.7b'
Add-SingleFileEntry 'qwen3aligner' 'qwen3-forced-aligner-0.6b-q4_k.gguf'    'qwen3-forced-aligner-0.6b'
Add-SingleFileEntry 'qwen3aligner' 'qwen3-forced-aligner-0.6b-q8_0.gguf'    'qwen3-forced-aligner-0.6b-q8_0'
Add-SingleFileEntry 'qwen3aligner' 'qwen3-forced-aligner-0.6b-f16.gguf'     'qwen3-forced-aligner-0.6b-f16'
Add-SingleFileEntry 'htdemucs'    'htdemucs_fp16weights.onnx'               'htdemucs'
Add-SingleFileEntry 'sherpa-diarization' 'wespeaker_zh_cnceleb_resnet34_LM.onnx' 'wespeaker-embedder'

# ---- OCR v5 (bundled latin set, fixed role names) ----
Add-SingleFileEntry 'v5' 'ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx'   'cls'
Add-SingleFileEntry 'v5' 'ch_PP-OCRv5_mobile_det.onnx'                      'det'
Add-SingleFileEntry 'v5' 'latin_PP-OCRv5_rec_mobile_infer.onnx'             'rec'
Add-SingleFileEntry 'v5' 'ppocrv5_latin_dict.txt'                           'dict'

function Add-RootFilesDirectoryEntry([string]$category, [string[]]$fileNames, [string]$key) {
    # Directory models whose member files currently live directly in the category root:
    # pack them into a temp dir, aggregate-hash, then store under <category>/<hash>/.
    $catDir = Join-Path $ModelsRoot $category
    $present = @($fileNames | Where-Object { Test-Path -LiteralPath (Join-Path $catDir $_) })
    if ($present.Count -eq 0) { return }
    if ($present.Count -ne $fileNames.Count) { throw "Partial model set in $category; expected: $($fileNames -join ', ')" }
    $tmp = Join-Path $catDir ".migrate-$([guid]::NewGuid().ToString('N'))"
    New-Item -ItemType Directory $tmp | Out-Null
    foreach ($f in $fileNames) { Move-Item -LiteralPath (Join-Path $catDir $f) $tmp }
    $hash = Get-AggregateSha256 $tmp
    $finalDir = Join-Path $catDir $hash
    if (Test-Path -LiteralPath $finalDir) { Remove-Item -LiteralPath $tmp -Recurse -Force }
    else { Move-Item -LiteralPath $tmp $finalDir }
    $relFiles = @(Get-ChildItem -LiteralPath $finalDir -Recurse -File |
        ForEach-Object { $_.FullName.Substring($finalDir.Length).TrimStart('\', '/').Replace('\', '/') })
    $manifest = Read-Manifest $catDir
    $manifest[$key] = @{ Kind = 'dir'; Hash = $hash; Files = @($relFiles) }
    Write-Manifest $catDir $manifest
    Write-Host "  [$category] root files -> $hash\ ($($relFiles.Count) files)"
}

# ---- Directory categories (member files keep their original names) ----
Add-DirectoryEntry 'sherpa-diarization' 'pyannote-segmentation-3-0' 'pyannote-segmentation-3-0'
Add-DirectoryEntry 'opusmt'        'zh-en'          'zh-en'
Add-DirectoryEntry 'sat'           'sat-3l-sm'      'sat-3l-sm'

# ---- Directory models whose files live in the category root ----
Add-RootFilesDirectoryEntry 'polyvoice' @('powerset_int8.onnx', 'resnet34_int8.onnx') 'models'
Add-RootFilesDirectoryEntry 'qwen3tts'  @('Qwen3-TTS-12Hz-1.7B-Base-Q4_K_M.gguf', 'mmproj-Qwen3-TTS-12Hz-1.7B-Base-Q8_0.gguf') '1.7b-base-q4'
Add-RootFilesDirectoryEntry 'indextts'  @('bigvgan.onnx', 'bigvgan.onnx.data', 'speaker_encoder.onnx', 'speaker_encoder.onnx.data') 'indextts2'

# ---- Remove stale nested models/models residual (if any) ----
$nested = Join-Path $ModelsRoot 'models'
if (Test-Path -LiteralPath $nested) {
    Remove-Item -LiteralPath $nested -Recurse -Force
    Write-Host "  removed stale nested 'models/models' residual"
}

Write-Host "Migration complete."
