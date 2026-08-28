[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Sts2ManagedDir,

    [string]$Version = "0.9.0"
)

$ErrorActionPreference = "Stop"

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$managedDirectory = [System.IO.Path]::GetFullPath($Sts2ManagedDir)
$releaseRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot "release"))
$packageName = "Sts2StateBridge-MCP-v$Version"
$packageRoot = [System.IO.Path]::GetFullPath((Join-Path $releaseRoot $packageName))
$archivePath = [System.IO.Path]::GetFullPath((Join-Path $releaseRoot "$packageName.zip"))

if (-not (Test-Path -LiteralPath $managedDirectory -PathType Container)) {
    throw "找不到游戏程序集目录：$managedDirectory"
}

if (-not $packageRoot.StartsWith($releaseRoot + [System.IO.Path]::DirectorySeparatorChar)) {
    throw "发布目录解析到仓库 release/ 之外，已停止。"
}

if ((Test-Path -LiteralPath $packageRoot) -or (Test-Path -LiteralPath $archivePath)) {
    throw "目标发布包已存在：$packageName。请先手工移走旧包，或使用新的版本号。"
}

$modProject = Join-Path $repositoryRoot "mcp\mod\Sts2StateBridge\Sts2StateBridge.csproj"
dotnet build $modProject -c Release -p:Sts2ManagedDir="$managedDirectory"
if ($LASTEXITCODE -ne 0) {
    throw "Mod 构建失败。"
}

$modOutput = Join-Path $repositoryRoot "mcp\mod\Sts2StateBridge\bin\Release\net9.0"
$modSource = Join-Path $repositoryRoot "mcp\mod\Sts2StateBridge"
$serverSource = Join-Path $repositoryRoot "mcp\server"
$modTarget = Join-Path $packageRoot "mod"
$serverTarget = Join-Path $packageRoot "server"

New-Item -ItemType Directory -Path $modTarget, $serverTarget | Out-Null

Copy-Item -LiteralPath (Join-Path $modOutput "Sts2StateBridge.dll") -Destination $modTarget
Copy-Item -LiteralPath (Join-Path $modSource "Sts2StateBridge.json") -Destination $modTarget
Copy-Item -LiteralPath (Join-Path $modSource "Sts2StateBridge.config.example.json") -Destination $modTarget

Copy-Item -LiteralPath (Join-Path $serverSource "src") -Destination $serverTarget -Recurse
Get-ChildItem -LiteralPath $serverTarget -Directory -Filter "__pycache__" -Recurse |
    Remove-Item -Recurse -Force
Get-ChildItem -LiteralPath $serverTarget -File -Filter "*.pyc" -Recurse |
    Remove-Item -Force
Copy-Item -LiteralPath (Join-Path $serverSource "pyproject.toml") -Destination $serverTarget
Copy-Item -LiteralPath (Join-Path $serverSource "uv.lock") -Destination $serverTarget
Copy-Item -LiteralPath (Join-Path $serverSource "requirements.txt") -Destination $serverTarget
Copy-Item -LiteralPath (Join-Path $serverSource "README.md") -Destination $serverTarget
Copy-Item -LiteralPath (Join-Path $repositoryRoot "mcp\RELEASE_README.md") -Destination (Join-Path $packageRoot "README.md")
Copy-Item -LiteralPath (Join-Path $repositoryRoot "LICENSE") -Destination $packageRoot

Compress-Archive -LiteralPath $packageRoot -DestinationPath $archivePath -CompressionLevel Optimal

Write-Host "发布包已生成：$archivePath"
Write-Host "下一步可运行：gh release create v$Version '$archivePath' --title 'Sts2StateBridge v$Version' --notes-file RELEASE_NOTES.md"
