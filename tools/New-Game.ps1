<#
.SYNOPSIS
Turns a fresh copy of renegade-game-template into a named game.

.EXAMPLE
.\tools\New-Game.ps1 -Title "Mancala" -CodeName Mancala -PackageId mancala

.DESCRIPTION
Replaces the template's names everywhere, in file contents and file names:
  "Game Template"   -> Title      (shown to players: window, launcher, store)
  GameTemplate      -> CodeName   (C# namespace, assembly, project and solution files)
  gametemplate      -> PackageId  (Android package: com.renegadegames.<PackageId>, permanent once on Play)
and sets the developer log tag. Run it once, from the new game's folder, before opening Godot.
#>
param(
	[Parameter(Mandatory)] [string] $Title,
	[Parameter(Mandatory)] [ValidatePattern('^[A-Z][A-Za-z0-9]*$')] [string] $CodeName,
	[Parameter(Mandatory)] [ValidatePattern('^[a-z][a-z0-9]*$')] [string] $PackageId,
	[ValidatePattern('^[a-z]{2,4}$')] [string] $LogTag = $PackageId.Substring(0, [Math]::Min(3, $PackageId.Length))
)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
if (-not (Test-Path (Join-Path $root 'GameTemplate.csproj'))) { throw 'Already renamed: GameTemplate.csproj not found.' }

$skip = '\\(\.git|\.godot|addons|build|android|bin|obj)\\'
$textTypes = '\.(cs|csproj|sln|godot|cfg|tscn|json|yml|md|ps1|txt)$'
$utf8 = New-Object System.Text.UTF8Encoding $false

Get-ChildItem $root -Recurse -File | Where-Object { $_.FullName -notmatch $skip -and $_.Name -match $textTypes } | ForEach-Object {
	if ($_.FullName -eq $PSCommandPath) { return }
	$text = [IO.File]::ReadAllText($_.FullName)
	$new = $text.Replace('Game Template', $Title).Replace('GameTemplate', $CodeName).Replace('gametemplate', $PackageId).Replace('DevLog.Tag = "gt"', "DevLog.Tag = `"$LogTag`"")
	if ($new -ne $text) { [IO.File]::WriteAllText($_.FullName, $new, $utf8); Write-Host "Updated $($_.FullName.Substring($root.Path.Length + 1))" }
}

# Deepest first, so renaming a folder never invalidates a path still to be renamed.
Get-ChildItem $root -Recurse | Where-Object { $_.FullName -notmatch $skip -and $_.Name -like '*GameTemplate*' } |
	Sort-Object { $_.FullName.Length } -Descending | ForEach-Object {
		Rename-Item $_.FullName ($_.Name.Replace('GameTemplate', $CodeName))
		Write-Host "Renamed $($_.Name)"
	}

Write-Host ""
Write-Host "Now: $Title (namespace $CodeName, package com.renegadegames.$PackageId)."
Write-Host "Next: edit assets/icon/*.svg and run tools/Render-Icons.ps1, then write docs/DESIGN.md."
