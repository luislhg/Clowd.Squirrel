# Stop the script if an error occurs
$ProgressPreference = "SilentlyContinue" # progress bar in powershell is slow af
$ErrorActionPreference = "Stop"

# This variable is null in github actions
if ($PSScriptRoot -eq $null) {
    $PSScriptRoot = "."
} else {
    Set-Location $PSScriptRoot
}

# search for msbuild, the loaction of vswhere is guarenteed to be consistent
$MSBuildPath = (&"${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -prerelease -products * -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe) | Out-String
Set-Alias msbuild $MSBuildPath.Trim()
Set-Alias seven "$PSScriptRoot\vendor\7za.exe"

# Ensure a clean state by removing build/package folders
Write-Host "Cleaning previous build outputs (if any)" -ForegroundColor Magenta
$Folders = @("build", "packages", "test\bin", "test\obj")
foreach ($Folder in $Folders) {
    if (Test-Path $Folder) {
        Remove-Item -path "$Folder" -Recurse -Force
    }
}

Write-Host "Retrieving current version from nbgv" -ForegroundColor Magenta
$gitVerJson = (&nbgv get-version -f json) | ConvertFrom-Json
$version = $gitVerJson.NuGetPackageVersion
$version

# Build solution with msbuild as dotnet can't build C++
Write-Host "Building Solution" -ForegroundColor Magenta
msbuild /verbosity:minimal /restore /p:Configuration=Release

# Build single-exe packaged projects and drop into nupkg
Write-Host "Extracting Generated Packages" -ForegroundColor Magenta
Set-Location "$PSScriptRoot\build\Release"
seven x csq*.nupkg -ocsq
seven x Clowd.Squirrel*.nupkg -osquirrel
Remove-Item *.nupkg

Write-Host "Publishing SingleFile Projects" -ForegroundColor Magenta
$ToolsDir = "csq\tools\net6.0\any"
dotnet publish -v minimal --no-build -c Release --self-contained "$PSScriptRoot\src\Update.Windows\Update.Windows.csproj" -o "$ToolsDir"
dotnet publish -v minimal --no-build -c Release --self-contained "$PSScriptRoot\src\Update.OSX\Update.OSX.csproj" -o "$ToolsDir"

Write-Host "Copying Tools" -ForegroundColor Magenta
Copy-Item -Path "$PSScriptRoot\Squirrel.entitlements" -Destination "$ToolsDir"
New-Item -Path "squirrel" -Name "tools" -ItemType "directory"
Copy-Item -Path "$ToolsDir\*" -Destination "squirrel\tools" -Recurse
Remove-Item "squirrel\tools\*.xml"

Write-Host "Re-assembling Packages" -ForegroundColor Magenta
seven a "csq.$version.nupkg" -tzip -mx9 "$PSScriptRoot\build\Release\csq\*"
seven a "Clowd.Squirrel.$version.nupkg" -tzip -mx9 "$PSScriptRoot\build\Release\squirrel\*"

Write-Host "Done." -ForegroundColor Magenta
