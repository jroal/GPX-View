param(
	[string]$Configuration = "Release",
	[string]$RuntimeIdentifier = "win-x64",
	[string]$Version = "1.0.0",
	[string]$IsccPath = ""
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDir
$projectPath = Join-Path $repoRoot "GPX View.csproj"
$publishDir = Join-Path $repoRoot "artifacts\publish\$RuntimeIdentifier"
$issPath = Join-Path $scriptDir "GPXView.iss"

Write-Host "Publishing single-file self-contained app..."
dotnet publish "$projectPath" `
	-c $Configuration `
	-r $RuntimeIdentifier `
	--self-contained true `
	/p:PublishSingleFile=true `
	/p:IncludeNativeLibrariesForSelfExtract=true `
	/p:PublishTrimmed=false `
	/p:Version=$Version `
	-o "$publishDir"

if (-not [string]::IsNullOrWhiteSpace($IsccPath)) {
	if (-not (Test-Path $IsccPath)) {
		throw "Provided -IsccPath does not exist: $IsccPath"
	}
	$isccPath = $IsccPath
}
else {
	$iscc = Get-Command ISCC.exe -ErrorAction SilentlyContinue
	if ($iscc) {
		$isccPath = $iscc.Source
	}
	else {
		$candidates = @(
			"${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
			"${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
			"${env:LOCALAPPDATA}\Programs\Inno Setup 6\ISCC.exe"
		)

		foreach ($candidate in $candidates) {
			if (Test-Path $candidate) {
				$isccPath = $candidate
				break
			}
		}

		if (-not $isccPath) {
			try {
				$appPath = (Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\ISCC.exe" -ErrorAction Stop)."(default)"
				if ($appPath -and (Test-Path $appPath)) {
					$isccPath = $appPath
				}
			}
			catch { }
		}

		if (-not $isccPath) {
			throw "ISCC.exe not found. Pass -IsccPath explicitly, e.g. -IsccPath 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'"
		}
	}
}

Write-Host "Using ISCC: $isccPath"

Write-Host "Building installer..."
& "$isccPath" "/DMyAppVersion=$Version" "/DMyPublishDir=$publishDir" "$issPath"

Write-Host "Done. Installer output: artifacts\\installer"
