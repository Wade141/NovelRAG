param([int]$Port = 11434)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$runtime = Join-Path $root '.tools\ollama\ollama.exe'
if (-not (Test-Path -LiteralPath $runtime)) { throw 'Portable runtime missing. Install Ollama from https://ollama.com/download/windows, then pull embeddinggemma and qwen3:4b.' }
if ($Port -lt 1024 -or $Port -gt 65535) { throw 'Port must be between 1024 and 65535.' }
$endpoint = "http://127.0.0.1:$Port"
try { $null = Invoke-RestMethod -Uri "$endpoint/api/version" -TimeoutSec 2; Write-Output "An Ollama service is already listening at $endpoint. Use that service and verify its local models."; exit 0 } catch { }
$env:OLLAMA_HOST = "127.0.0.1:$Port"
$env:OLLAMA_MODELS = Join-Path $root '.tools\models'
$env:OLLAMA_NO_CLOUD = '1'
$env:OLLAMA_NUM_PARALLEL = '1'
$process = Start-Process -FilePath $runtime -ArgumentList 'serve' -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $root '.tools\local-ai-out.log') -RedirectStandardError (Join-Path $root '.tools\local-ai-error.log')
Write-Output "Local-only Ollama started (PID $($process.Id)) at $endpoint. Models: $env:OLLAMA_MODELS"
Write-Output 'In NovelRAG Settings, enable local AI, save settings, and rebuild the semantic index.'
