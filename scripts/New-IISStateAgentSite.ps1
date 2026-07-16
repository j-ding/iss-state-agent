<#
.SYNOPSIS
    Provisions an IISStateAgent IIS application on a Windows Server.

.DESCRIPTION
    Creates the app pool, physical directory, IIS application, authentication settings,
    and file system ACLs needed to run IISStateAgent under ApplicationPoolIdentity.

    Idempotent - safe to re-run. Each step checks whether the resource already exists
    before creating it.

    MUST be run as a local administrator (required for IIS config and ACL changes).

.PARAMETER Environment
    Slot name: dev | qa | stage | prod

.PARAMETER SiteName
    IIS site that will host the application.
    Example: "prd-alp-iws-01.fifsg.com"

.PARAMETER PhysicalPath
    Full path to the deployment directory on disk.
    Example: "C:\inetpub\wwwroot\s13\Internal\IISStateAgent\prod"

.PARAMETER AppPoolName
    Defaults to "$Environment-IISStateAgent".

.PARAMETER VirtualPath
    IIS virtual path under the site.
    Defaults to "/IISStateAgent/$Environment".

.EXAMPLE
    .\New-IISStateAgentSite.ps1 `
        -Environment prod `
        -SiteName "prd-alp-iws-01.fifsg.com" `
        -PhysicalPath "C:\inetpub\wwwroot\s13\Internal\IISStateAgent\prod"
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [ValidateSet('dev','qa','stage','prod')]
    [string] $Environment,

    [Parameter(Mandatory)]
    [string] $SiteName,

    [Parameter(Mandatory)]
    [string] $PhysicalPath,

    [string] $AppPoolName,
    [string] $VirtualPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $AppPoolName) { $AppPoolName = "$Environment-IISStateAgent" }
if (-not $VirtualPath) { $VirtualPath = "/IISStateAgent/$Environment" }

$AppPoolIdentity = "IIS APPPOOL\$AppPoolName"
$LogsPath        = Join-Path $PhysicalPath 'logs'
$IISAppPath      = "$SiteName$VirtualPath"

# --- Guard ---

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "This script must be run as Administrator."
}

Import-Module WebAdministration -ErrorAction Stop

Write-Host ""
Write-Host "=== IISStateAgent setup ===" -ForegroundColor Cyan
Write-Host "  Environment  : $Environment"
Write-Host "  Site         : $SiteName"
Write-Host "  Virtual path : $VirtualPath"
Write-Host "  Physical path: $PhysicalPath"
Write-Host "  App pool     : $AppPoolName"
Write-Host ""

# --- 1. App pool ---

if (Test-Path "IIS:\AppPools\$AppPoolName") {
    Write-Host "[SKIP] App pool '$AppPoolName' already exists." -ForegroundColor Yellow
} else {
    Write-Host "[CREATE] App pool '$AppPoolName'..."
    New-WebAppPool -Name $AppPoolName | Out-Null
    Set-ItemProperty "IIS:\AppPools\$AppPoolName" managedRuntimeVersion ""
    Set-ItemProperty "IIS:\AppPools\$AppPoolName" managedPipelineMode Integrated
    Set-ItemProperty "IIS:\AppPools\$AppPoolName" processModel.identityType ApplicationPoolIdentity
    Write-Host "  Done." -ForegroundColor Green
}

# --- 2. Directories ---

foreach ($dir in @($PhysicalPath, $LogsPath)) {
    if (Test-Path $dir) {
        Write-Host "[SKIP] Directory already exists: $dir" -ForegroundColor Yellow
    } else {
        Write-Host "[CREATE] Directory: $dir"
        New-Item -ItemType Directory -Path $dir | Out-Null
        Write-Host "  Done." -ForegroundColor Green
    }
}

# --- 3. IIS application ---

$appCheck = Get-WebApplication -Site $SiteName -Name ($VirtualPath.TrimStart('/')) -ErrorAction SilentlyContinue
if ($appCheck) {
    Write-Host "[SKIP] IIS application '$IISAppPath' already exists." -ForegroundColor Yellow
} else {
    Write-Host "[CREATE] IIS application '$IISAppPath'..."
    New-WebApplication -Site $SiteName `
                       -Name ($VirtualPath.TrimStart('/')) `
                       -PhysicalPath $PhysicalPath `
                       -ApplicationPool $AppPoolName | Out-Null
    Write-Host "  Done." -ForegroundColor Green
}

# --- 4. Authentication ---
# Both anonymous and Windows auth must be enabled at the IIS level.
# Without anonymousAuthentication=true, IIS rejects unauthenticated requests
# before ANCM runs, so ASP.NET Core's .AllowAnonymous() on /health never fires.

Write-Host "[SET] Authentication on '$IISAppPath'..."
& "$env:SystemRoot\system32\inetsrv\appcmd.exe" set config $IISAppPath `
    /section:system.webServer/security/authentication/windowsAuthentication `
    /enabled:true /commit:apphost
& "$env:SystemRoot\system32\inetsrv\appcmd.exe" set config $IISAppPath `
    /section:system.webServer/security/authentication/anonymousAuthentication `
    /enabled:true /commit:apphost
Write-Host "  Done." -ForegroundColor Green

# --- 5. File system ACLs ---
# App root  - Read & Execute
# logs dir  - Modify (Serilog rolling file sink)

function Grant-Access {
    param([string]$Path, [string]$Account, [string]$Rights)
    $acl  = Get-Acl $Path
    $rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
                $Account, $Rights, 'ContainerInherit,ObjectInherit', 'None', 'Allow')
    $acl.AddAccessRule($rule)
    Set-Acl -Path $Path -AclObject $acl
}

Write-Host "[ACL] Granting ReadAndExecute on app root to '$AppPoolIdentity'..."
Grant-Access -Path $PhysicalPath -Account $AppPoolIdentity -Rights 'ReadAndExecute'
Write-Host "  Done." -ForegroundColor Green

Write-Host "[ACL] Granting Modify on logs dir to '$AppPoolIdentity'..."
Grant-Access -Path $LogsPath -Account $AppPoolIdentity -Rights 'Modify'
Write-Host "  Done." -ForegroundColor Green

# --- 6. Start app pool if stopped ---

$poolState = (Get-WebAppPoolState -Name $AppPoolName).Value
if ($poolState -ne 'Started') {
    Write-Host "[START] App pool is '$poolState' - starting..."
    Start-WebAppPool -Name $AppPoolName
    Write-Host "  Done." -ForegroundColor Green
} else {
    Write-Host "[OK] App pool is already running." -ForegroundColor Green
}

# --- Summary ---

Write-Host ""
Write-Host "=== Setup complete ===" -ForegroundColor Cyan
Write-Host "  Next: copy published output to $PhysicalPath"
Write-Host "  Then: curl http://$($SiteName)$VirtualPath/health"
Write-Host ""
