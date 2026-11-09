<#
.SYNOPSIS
    Prepares and verifies a Ledgerline working copy with one command.

.DESCRIPTION
    Checks the tools Ledgerline needs, creates the Python environment in
    .venv, builds everything, creates or updates the BankCore and Warehouse
    databases, then runs the tests. Safe to run again at any time.

.PARAMETER SqlServer
    The SQL Server instance the migrations and tests connect to. Defaults to
    the local default instance.

.PARAMETER Python
    The Python 3.12 interpreter used to create .venv the first time.
    Defaults to "python" on PATH.

.EXAMPLE
    ./setup.ps1

.EXAMPLE
    ./setup.ps1 -Python C:\Tools\Python312\python.exe
#>
[CmdletBinding()]
param(
    [string]$SqlServer = 'localhost',
    [string]$Python = 'python'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

function Write-Step([string]$Message) {
    Write-Host "`n== $Message" -ForegroundColor Cyan
}

# Runs a command-line tool and stops the script if the tool reports failure.
function Invoke-Checked([string]$Description, [scriptblock]$Command) {
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed (exit code $LASTEXITCODE)."
    }
}

# Returns a version such as "3.12", or nothing if the interpreter cannot run.
function Get-PythonVersion([string]$Interpreter) {
    try {
        & $Interpreter -c "import sys; print('%d.%d' % sys.version_info[:2])"
    }
    catch {
    }
}

# --- 1. Prerequisites -------------------------------------------------------

Write-Step '.NET SDK (version 10, pinned by global.json)'
$sdkVersion = & dotnet --version
if ($LASTEXITCODE -ne 0 -or -not $sdkVersion.StartsWith('10.')) {
    throw 'The .NET 10 SDK is required. Install it from https://dotnet.microsoft.com/download.'
}
Write-Host "Found .NET SDK $sdkVersion"

Write-Step 'ODBC Driver 18 for SQL Server'
if (-not (Get-OdbcDriver -Name 'ODBC Driver 18 for SQL Server' -Platform '64-bit' -ErrorAction SilentlyContinue)) {
    throw 'ODBC Driver 18 for SQL Server (64-bit) is required.'
}
Write-Host 'Found ODBC Driver 18'

# --- 2. Python environment --------------------------------------------------

Write-Step 'Python environment (.venv, Python 3.12)'
$venvPython = Join-Path $PSScriptRoot '.venv\Scripts\python.exe'

if (-not (Test-Path $venvPython)) {
    $version = Get-PythonVersion $Python
    if ($version -ne '3.12') {
        throw "Python 3.12 is required to create .venv, but '$Python' is version '$version' or was not found. Run: ./setup.ps1 -Python <path to python.exe>"
    }
    Invoke-Checked 'Creating .venv' { & $Python -m venv .venv }
}

$venvVersion = Get-PythonVersion $venvPython
if ($venvVersion -ne '3.12') {
    throw ".venv uses Python '$venvVersion', not 3.12. Delete the .venv folder and run setup again."
}

Invoke-Checked 'Installing Python packages' {
    & $venvPython -m pip install --quiet --disable-pip-version-check -r requirements.txt
}
Write-Host 'Python packages match requirements.txt'

# --- 3. Build, migrate and test ---------------------------------------------

# The .NET and Python tests both read the server name from this variable.
$env:LEDGERLINE_SQL_SERVER = $SqlServer

Write-Step 'dotnet build'
Invoke-Checked 'dotnet build' { dotnet build --nologo --verbosity quiet }

# Creates BankCore and Warehouse if they are missing and applies any pending
# scripts from db\*\migrations. Applied scripts are skipped, so this is safe
# to run every time.
Write-Step "database migrations (SQL Server: $SqlServer)"
Invoke-Checked 'database migrations' {
    dotnet run --project src/Ledgerline.DbMigrator --no-build -- --server $SqlServer
}

Write-Step "dotnet test (SQL Server: $SqlServer)"
Invoke-Checked 'dotnet test' { dotnet test --nologo --no-build }

Write-Step "pytest (SQL Server: $SqlServer)"
Invoke-Checked 'pytest' { & $venvPython -m pytest generator pipeline --quiet }

Write-Host "`nLedgerline is ready." -ForegroundColor Green
