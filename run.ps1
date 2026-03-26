param (
    [Parameter(Mandatory = $false)]
    [ValidateSet("ingest", "crawl", "api", "ui", "dev", "all", "migrate", "reddit", "x", "build", "analyze", "help")]
    [string]$Command = "help"
)

# --- Ensure Node/npm are available even if PATH is missing ---
$nodeDir = "C:\Program Files\nodejs"
$npmDir = Join-Path $env:APPDATA "npm"

if (Test-Path (Join-Path $nodeDir "node.exe")) {
    if ($env:Path -notlike "*$nodeDir*") { $env:Path = "$nodeDir;$env:Path" }
}

if (Test-Path $npmDir) {
    if ($env:Path -notlike "*$npmDir*") { $env:Path = "$npmDir;$env:Path" }
}

# Bypass execution policy if needed:
# powershell -ExecutionPolicy Bypass -File .\run.ps1 ingest

$ErrorActionPreference = "Stop"
$ScriptRoot = $PSScriptRoot

function Write-Status {
    param([string]$Message)
    Write-Host "[CivicLens] $Message" -ForegroundColor Cyan
}

function Load-Env {
    $EnvPath = "$ScriptRoot\.env"
    if (Test-Path $EnvPath) {
        Write-Status "Loading .env file..."
        Get-Content $EnvPath | ForEach-Object {
            $line = $_.Trim()
            if ($line -and -not $line.StartsWith("#")) {
                $parts = $line.Split("=", 2)
                if ($parts.Length -eq 2) {
                    $name = $parts[0].Trim()
                    $value = $parts[1].Trim()
                    [System.Environment]::SetEnvironmentVariable($name, $value, [System.EnvironmentVariableTarget]::Process)
                }
            }
        }
    }
}

# Load env vars at start
Load-Env

function Ensure-Go {
    if (Get-Command "go" -ErrorAction SilentlyContinue) {
        return "go"
    }
    
    $GoPath = "C:\Program Files\Go\bin\go.exe"
    if (Test-Path $GoPath) {
        return $GoPath
    }
    
    throw "Go is not installed. Please install Go 1.22+ from https://go.dev/dl/"
}

function Build-Ingest {
    Write-Status "Building Go Ingestion..."
    $GoExe = Ensure-Go
    
    Push-Location "$ScriptRoot\ingest"
    try {
        & $GoExe build -o "$ScriptRoot\civic-ingest.exe" ./cmd/civic-ingest
        if ($LASTEXITCODE -ne 0) {
            throw "Go build failed"
        }
        Write-Status "Built civic-ingest.exe"
    }
    finally {
        Pop-Location
    }
}

function Run-Migrate {
    $ExePath = "$ScriptRoot\civic-ingest.exe"
    if (-not (Test-Path $ExePath)) {
        Build-Ingest
    }
    
    Write-Status "Running database migrations..."
    & $ExePath migrate --db "$ScriptRoot\data\civic_lens.db"
}

function Run-Ingest {
    $ExePath = "$ScriptRoot\civic-ingest.exe"
    if (-not (Test-Path $ExePath)) {
        Build-Ingest
    }
    
    Write-Status "Running seed ingestion..."
    & $ExePath ingest --config "$ScriptRoot\data\seeds.yaml" --db "$ScriptRoot\data\civic_lens.db"
}

function Run-Crawl {
    param([string]$Duration = "10m")
    
    $ExePath = "$ScriptRoot\civic-ingest.exe"
    if (-not (Test-Path $ExePath)) {
        Build-Ingest
    }
    
    Write-Status "Running crawl for $Duration..."
    & $ExePath crawl --config "$ScriptRoot\data\seeds.yaml" --db "$ScriptRoot\data\civic_lens.db" --duration $Duration
}

function Run-Reddit {
    $ExePath = "$ScriptRoot\civic-ingest.exe"
    if (-not (Test-Path $ExePath)) {
        Build-Ingest
    }
    
    Write-Status "Fetching Reddit data..."
    & $ExePath reddit --config "$ScriptRoot\data\seeds.yaml" --db "$ScriptRoot\data\civic_lens.db"
}

function Run-X {
    $ExePath = "$ScriptRoot\civic-ingest.exe"
    if (-not (Test-Path $ExePath)) {
        Build-Ingest
    }
    
    Write-Status "Fetching X (Twitter) data..."
    & $ExePath x --config "$ScriptRoot\data\seeds.yaml" --db "$ScriptRoot\data\civic_lens.db"
}

function Initialize-Venv {
    $VenvPath = "$ScriptRoot\analysis\.venv"
    if (-not (Test-Path $VenvPath)) {
        Write-Status "Creating Python virtual environment..."
        python -m venv $VenvPath
    }
    
    $PythonExe = "$VenvPath\Scripts\python.exe"
    if (-not (Test-Path $PythonExe)) {
        throw "Virtual environment not found or invalid."
    }
    return $PythonExe
}

function Run-Api {
    Write-Status "Starting Python API..."
    $PythonExe = Initialize-Venv
    
    $ReqFile = "$ScriptRoot\analysis\requirements.txt"
    if (Test-Path $ReqFile) {
        & $PythonExe -m pip install -r $ReqFile --quiet
    }
    
    $MainScript = "$ScriptRoot\analysis\src\main.py"
    $Env:PYTHONPATH = $ScriptRoot
    & $PythonExe $MainScript
}

function Run-Ui {
    Write-Status "Starting React Frontend..."
    if (-not (Get-Command "npm" -ErrorAction SilentlyContinue)) {
        throw "npm is not installed. Please install Node.js 18+."
    }
    
    Push-Location "$ScriptRoot\ui"
    try {
        if (-not (Test-Path "node_modules")) {
            Write-Status "Installing UI dependencies..."
            npm install
        }
        npm run dev
    }
    finally {
        Pop-Location
    }
}

function Run-Analyze {
    Write-Status "Running analysis pipeline..."
    $PythonExe = Initialize-Venv
    
    $ReqFile = "$ScriptRoot\analysis\requirements.txt"
    if (Test-Path $ReqFile) {
        & $PythonExe -m pip install -r $ReqFile --quiet
    }
    
    $Env:PYTHONPATH = $ScriptRoot
    & $PythonExe -m analysis.src.scheduler.job_runner
    
    if ($LASTEXITCODE -eq 0) {
        Write-Status "Analysis pipeline complete. Cached snapshots saved to data/cache/"
    }
    else {
        throw "Analysis pipeline failed"
    }
}

function Run-Dev {
    Write-Status "Launching Dev Environment (API + UI)..."
    # Launch in separate processes
    Start-Process powershell -ArgumentList "-NoExit", "-File", "`"$ScriptRoot\run.ps1`"", "api"
    Start-Process powershell -ArgumentList "-NoExit", "-File", "`"$ScriptRoot\run.ps1`"", "ui"
}

# Main Dispatch
switch ($Command) {
    "build" { 
        Build-Ingest 
    }
    "migrate" { 
        Run-Migrate 
    }
    "ingest" { 
        Run-Migrate
        Run-Ingest 
    }
    "crawl" { 
        Run-Migrate
        Run-Crawl 
    }
    "reddit" { 
        Run-Migrate
        Run-Reddit 
    }
    "x" {
        Run-Migrate
        Run-X
    }
    "api" { 
        Run-Api 
    }
    "analyze" {
        Run-Analyze
    }
    "ui" { 
        Run-Ui 
    }
    "dev" { 
        Run-Dev 
    }
    "all" {
        Run-Migrate
        Run-Ingest
        #Run-X
        Run-Crawl -Duration "5m"
        Run-Analyze
        Run-Dev
    }
    "help" {
        Write-Host "Usage: .\run.ps1 [command]"
        Write-Host ""
        Write-Host "Commands:"
        Write-Host "  build    - Build Go ingestion binary"
        Write-Host "  migrate  - Apply database migrations"
        Write-Host "  ingest   - Discover URLs from seed feeds"
        Write-Host "  crawl    - Run the web crawler"
        Write-Host "  analyze  - Run analysis pipeline (ETL + AI + caching)"
        Write-Host "  api      - Start Python FastAPI server (serves cached data)"
        Write-Host "  ui       - Start React Frontend"
        Write-Host "  dev      - Start both API and UI"
        Write-Host "  all      - Run ingest, crawl, analyze, then dev"
        Write-Host ""
        Write-Host "Typical workflow:"
        Write-Host "  1. .\run.ps1 crawl     # Fetch news articles"
        Write-Host "  2. .\run.ps1 analyze   # Run AI analysis pipeline"
        Write-Host "  3. .\run.ps1 dev       # Start API + UI"
        Write-Host ""
        Write-Host "To bypass execution policy:"
        Write-Host "  powershell -ExecutionPolicy Bypass -File .\run.ps1 ingest"
    }
}
