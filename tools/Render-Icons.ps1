<#
.SYNOPSIS
Renders the app icon PNGs from the SVG sources in assets/icon, using Chrome in headless mode.

.DESCRIPTION
Android adaptive icons are two 432 px layers (background, and a foreground whose art stays inside
a 132 px radius of the centre so no launcher mask clips it) plus a monochrome layer for themed
icons. The 192 px icon is the legacy launcher icon; the 512 px one is for the Play Store listing.
Edit the SVGs, run this, then rebuild.
#>
$ErrorActionPreference = 'Stop'
$icons = Join-Path $PSScriptRoot '..\assets\icon' | Resolve-Path
$chrome = @(
	"$env:ProgramFiles\Google\Chrome\Application\chrome.exe",
	"${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $chrome) { throw 'Needs Chrome or Edge to render the SVGs.' }

$jobs = @(
	@{ Name = 'background_432'; Size = 432; Layers = @('background') },
	@{ Name = 'foreground_432'; Size = 432; Layers = @('foreground') },
	@{ Name = 'monochrome_432'; Size = 432; Layers = @('monochrome') },
	@{ Name = 'icon_192'; Size = 192; Layers = @('background', 'foreground') },
	@{ Name = 'icon_512'; Size = 512; Layers = @('background', 'foreground') }
)
foreach ($job in $jobs) {
	$size = $job.Size
	$images = $job.Layers | ForEach-Object {
		$url = ([Uri](Join-Path $icons "$_.svg")).AbsoluteUri
		"<img src='$url' style='position:absolute;left:0;top:0;width:${size}px;height:${size}px'>"
	}
	$html = Join-Path $env:TEMP "render_$($job.Name).html"
	"<html><body style='margin:0;background:transparent'>$($images -join '')</body></html>" | Set-Content -Encoding utf8 $html
	$out = Join-Path $icons "$($job.Name).png"
	# Start-Process, not &: Chrome logs to stderr, which Windows PowerShell 5.1 treats as an error.
	$arguments = @('--headless=new', '--disable-gpu', '--hide-scrollbars', '--default-background-color=00000000',
		"--window-size=$size,$size", "`"--screenshot=$out`"", ([Uri]$html).AbsoluteUri)
	Start-Process -FilePath $chrome -ArgumentList $arguments -NoNewWindow -Wait -RedirectStandardError (Join-Path $env:TEMP 'render_icons.log')
	Write-Host "Rendered $out"
}
