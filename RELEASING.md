# 发布流程

当前仓库使用项目 Release 版本 `v0.10.0`，与 Bridge Mod 和 MCP Server 版本对齐。Agent 在自己的 `pyproject.toml` 中保留独立组件版本。

## 发布前检查

```powershell
cd mcp\server
uv sync
uv run pytest

cd ..\..\agent
uv sync
uv run pytest

cd ..
dotnet build .\mcp\mod\Sts2StateBridge\Sts2StateBridge.csproj `
  -c Release `
  -p:Sts2ManagedDir="E:\SteamLibrary\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64"
```

确认 `git status` 干净，并检查 Mod 清单、Bridge `/health` 和项目 Release 的版本号。

## 生成 MCP Release ZIP

```powershell
.\scripts\build-release.ps1 `
  -Version 0.10.0 `
  -Sts2ManagedDir "E:\SteamLibrary\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64"
```

输出：

```text
release/Sts2StateBridge-MCP-v0.10.0.zip
```

压缩包只包含可分发的 Mod 产物、独立 MCP Server、安装说明和 MIT License，不包含游戏 DLL、虚拟环境、缓存、测试或 API Key。

## 创建 GitHub Release

先提交并推送全部变更，再创建标签和 Release：

```powershell
git tag -a v0.10.0 -m "Sts2StateBridge v0.10.0"
git push origin main
git push origin v0.10.0
gh release create v0.10.0 `
  ".\release\Sts2StateBridge-MCP-v0.10.0.zip" `
  --title "Sts2StateBridge v0.10.0" `
  --notes-file RELEASE_NOTES.md
```

创建 Release 属于公开或私有远程仓库写操作。执行前应确认仓库可见性、提交内容和 Release Notes。
