# Quick Continue / 快速继续游戏

《隔离区》（ZED ZONE）纯客户端主菜单单人“继续游戏”快捷入口模组（BepInEx 6 IL2CPP，MIT 开源）。

- **前置依赖**：ZED ZONE Code Mod Loader（Steam Workshop ID: `3834614258`）
- **开源仓库（MIT）**：https://github.com/HelloWorldGitHub114/ZedZone-QuickContinue

## 核心功能

1. **主菜单一键继续单人游戏**
   - 在主菜单**联机游戏（Multiplayer）**按钮正上方新增视觉风格与原版完全一致的**继续游戏 / Continue**按钮。
   - 点击即可直接载入最近游玩的单人地图、对应单人角色与最新存档槽，跳过“单人游戏 -> 选择模式 -> 选择地图 -> 选择人物 -> 选择存档”多层菜单。
   - 若本地尚无任何单人存档，自动隐藏按钮不打扰主菜单布局。

2. **多模式与首装开箱即用**
   - 支持**常规生存**、**灾变**、**尸潮（手动存档）**以及**剧情模式**。
   - 仅记录单人模式进入与存盘操作，主动跳过联机房主/客户端会话，不会误把联机进度记成单人继续目标。
   - 首次安装启用时即使还没有新开记录，也会自动扫描本地磁盘中按时间排序的最新有效单人存档与对应角色；遇到角色跨存档回档冲突等极端情况时，会安全退回游戏原生确认/选人弹窗。

3. **多语言自适应与干净卸载**
   - 自动跟随游戏界面语言切换按钮文本（支持简体中文、繁体中文、English、日本語、한국어、Русский）。
   - 不修改任何 `.game` 或 `.mpchar` 存档数据结构；删除模组文件夹即可彻底还原，无任何存档残留。

## 配置文件说明

首次启动后生成配置文件：`BepInEx/config/zedzone.quickcontinue.cfg`

- `[General]`：
  - `Enabled`：启用/禁用本模组（默认 `true`）。
  - `VerticalSpacing`：主菜单按钮位于“联机游戏”上方的垂直间距（像素，默认 `26`）。
  - `VerboseLog`：是否在 `BepInEx\LogOutput.log` 输出调试日志（默认 `false`）。
- `[LastSinglePlayer]`：自动记录最近游玩的单人存档路径、存档 ID、槽位索引、角色 ID 与写入时间戳，一般无需手动修改。

## 构建与安装

```powershell
# 自动探测 Steam 中的 ZED ZONE 目录并构建打包到 content/QuickContinue
pwsh -NoProfile -File .\build.ps1

# 构建并安装到游戏 Mods 目录（需先关闭游戏）
pwsh -NoProfile -File .\build.ps1 -Install

# 手动指定游戏安装路径
pwsh -NoProfile -File .\build.ps1 -GameDir "<你的 ZED ZONE 安装路径>"
```

## 开源协议

本项目基于 [MIT License](LICENSE) 开源。

---

# Quick Continue (English)

A client-side QoL mod for **ZED ZONE** (BepInEx 6 IL2CPP, MIT licensed) that adds a singleplayer **Continue** button directly above the **Multiplayer** button on the main menu.

- Click **Continue** to immediately load your most recent singleplayer world, character, and latest save slot (including autosaves), bypassing the 4-screen menu flow.
- Supports Survival, Cataclysm, Horde (saved runs), and Story mode while strictly ignoring multiplayer sessions.
- Works out of the box on first launch by scanning the newest local singleplayer save, automatically adapts to the active game UI language, and leaves zero save-file modifications behind on removal.
- Source code (MIT): https://github.com/HelloWorldGitHub114/ZedZone-QuickContinue
