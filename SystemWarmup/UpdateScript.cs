namespace SystemWarmup;

/// <summary>
/// PowerShell helper is stored OUTSIDE the installation folder so the original executable can exit.
/// It attempts an explicit rollback if file replacement or launch fails.
/// The old installation remains in a dated sibling backup until the user removes it manually.
/// </summary>
public static class UpdateScript
{
    public const string Content = """
param(
    [Parameter(Mandatory=$true)][string]$ApplicationDirectory,
    [Parameter(Mandatory=$true)][string]$StagedDirectory,
    [Parameter(Mandatory=$true)][int]$OldPid,
    [Parameter(Mandatory=$true)][string]$LogFile
)
$ErrorActionPreference = 'Stop'
function Write-Log([string]$message) {
    Add-Content -LiteralPath $LogFile -Value ("{0} {1}" -f (Get-Date -Format o), $message)
}
$parent = Split-Path -Parent $ApplicationDirectory
$name = Split-Path -Leaf $ApplicationDirectory
$backup = Join-Path $parent ($name + ".previous-" + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$backupCreated = $false
$newInstalled = $false
try {
    Write-Log "Waiting for PID $OldPid to close."
    $deadline = (Get-Date).AddSeconds(120)
    while ((Get-Process -Id $OldPid -ErrorAction SilentlyContinue) -and (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 500
    }
    if (Get-Process -Id $OldPid -ErrorAction SilentlyContinue) {
        throw "Previous process is still running after 120 seconds; application left unchanged."
    }
    if (-not (Test-Path -LiteralPath (Join-Path $StagedDirectory 'SystemWarmup.exe'))) {
        throw 'Staged executable missing.'
    }
    if (-not (Test-Path -LiteralPath (Join-Path $ApplicationDirectory 'SystemWarmup.exe'))) {
        throw 'Installed executable missing. Refusing replacement.'
    }
    if (Test-Path -LiteralPath $backup) {
        throw 'Backup destination already exists.'
    }
    # Rename is on the same destination volume. Never delete the original before the replacement is ready.
    Move-Item -LiteralPath $ApplicationDirectory -Destination $backup
    $backupCreated = $true
    Write-Log "Old installation saved: $backup"
    # Directory copy may cross drives. Preserve backup until new installation is verified.
    Copy-Item -LiteralPath $StagedDirectory -Destination $ApplicationDirectory -Recurse
    $newInstalled = $true
    $exe = Join-Path $ApplicationDirectory 'SystemWarmup.exe'
    if (-not (Test-Path -LiteralPath $exe)) { throw 'Copied executable is missing.' }
    $process = Start-Process -FilePath $exe -WorkingDirectory $ApplicationDirectory -PassThru
    Start-Sleep -Seconds 3
    $process.Refresh()
    if ($process.HasExited) {
        throw 'The updated application exited immediately after launch.'
    }
    Write-Log "Installed new release. Previous installation retained at: $backup"
}
catch {
    Write-Log ("Update failed: " + $_.Exception.Message)
    if ($backupCreated) {
        try {
            if (Test-Path -LiteralPath $ApplicationDirectory) {
                Remove-Item -LiteralPath $ApplicationDirectory -Recurse -Force
            }
            Move-Item -LiteralPath $backup -Destination $ApplicationDirectory
            Write-Log 'Rollback succeeded.'
            Start-Process -FilePath (Join-Path $ApplicationDirectory 'SystemWarmup.exe') -WorkingDirectory $ApplicationDirectory
        }
        catch { Write-Log ("Rollback failed, backup retained at $backup : " + $_.Exception.Message) }
    }
    exit 1
}
""";
}