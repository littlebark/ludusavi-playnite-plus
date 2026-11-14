# Ludusavi Playnite Plus

A [Playnite](https://playnite.link/) plugin for [Ludusavi](https://github.com/mtkennerly/ludusavi), forked from [mtkennerly/ludusavi-playnite](https://github.com/mtkennerly/ludusavi-playnite).

## 支持 Goldberg / RUNE 模拟器游戏

原版插件只能识别从 Steam 库导入的游戏（通过 `game.Source == "Steam"` 获取 AppID）。对于手动添加到 Playnite 的游戏——尤其是打过 Goldberg、RUNE 等模拟器补丁的版本——插件无法获取 Steam AppID，导致名字匹配失败时无法兜底。

本 fork 在插件端实现了与 Ludusavi 本体一致的 AppID 检测逻辑，当游戏不是从 Steam 导入时，自动从游戏安装目录探测 Steam AppID，再传给 `ludusavi find --steam-id` 做兜底匹配。

### 检测规则

对齐 Ludusavi 本体的 `detect_steam_appid()`，4 路 fallback：

1. **`steam_appid.txt`**：先在安装目录和 `steam_settings/` 下查；找不到则按文件名全树搜索（不限深度），命中多个时优先路径最浅的那个
2. **`steam_emu.ini` / `rune.ini`**：RUNE 等模拟器不生成 `steam_appid.txt`，AppID 写在 ini 的 `AppId=` 行，且常埋在任意深度（如 UE 的 `Engine/Binaries/.../Win64/`）。全树搜索，并优先取与 `steam_api.dll` / `steam_api64.dll` 同目录的那份
3. **其他模拟器配置**：全树搜索 `SteamConfig.ini`、`SmartSteamEmu.ini`、`flt.ini`、`steam_appid.txt`，匹配 `AppID=123456`、`AppId = 123456` 或裸数字行
4. **路径末尾数字**：路径最后一段以数字结尾时（如 `.../2288630`）取该数字，会先剥掉 `\stats`、`\SteamEmu`、`\SteamEmu\UserStats` 后缀

全树搜索的约束：跳过 `node_modules`、`.git`、`.svn`、`.hg`、`__MACOSX`；单次扫描上限 20 万个目录项；不跟随符号链接目录。

### 工作流程

```
Playnite 启动游戏
  ↓
插件调用 ludusavi find "游戏名" --steam-id <探测到的AppID>
  ↓
  ├─ 名字匹配成功 → 直接用匹配结果
  └─ 名字匹配失败 → 用 --steam-id 反查 manifest 找到官方游戏名
  ↓
插件调用 ludusavi backup "官方游戏名"
```

这样即使 Playnite 里的游戏名是中文、带括号副标题、或与 manifest 官方名不一致，插件也能通过 AppID 兜底找到正确的存档路径。

### 改动文件

仅修改了 `src/Etc.cs` 中的 `SteamId()` 方法及辅助函数，不影响原版任何功能。

| 函数 | 说明 |
|---|---|
| `SteamId()` | 重写：非 Steam 来源游戏增加安装目录 AppID 探测 |
| `DetectSteamAppId()` | 4 路 fallback 主入口 |
| `FindFiles()` / `WalkFiles()` | 按文件名全树搜索（不限深度，跳过无关目录，上限 20 万条目） |
| `DetectGameAppId()` | `steam_appid.txt`，浅层优先、其次全树取最浅 |
| `ReadAppIdFromEmuIni()` | `steam_emu.ini` / `rune.ini`，优先与 `steam_api*.dll` 同目录 |
| `AppIdFromConfig()` | `SteamConfig.ini` / `SmartSteamEmu.ini` / `flt.ini` 全树搜索 |
| `AppIdFromPath()` | 路径末尾数字，剥离 SteamEmu 统计目录后缀 |

---

以下为原版 README 内容：

## Features
* Known save locations for more than 10,000 games.
* On-demand backup and restore for all saves.
* Automatically back up a game when you finish playing it
  and even periodically during play.

The [info on what to back up](https://github.com/mtkennerly/ludusavi-manifest)
is ultimately sourced from [PCGamingWiki](https://www.pcgamingwiki.com/wiki/Home),
so please contribute any new or fixed data back to the wiki itself,
and your improvements will be incorporated into Ludusavi's data as well.

If you'd like to help translate this plugin into other languages,
[check out the Crowdin project](https://crowdin.com/project/ludusavi-playnite).

## Setup
### Ludusavi
**You must download and install Ludusavi itself in addition to this plugin.**

Refer to the [Ludusavi project](https://github.com/mtkennerly/ludusavi)
for instructions on how to install Ludusavi itself.
Ludusavi 0.24.0 or newer is recommended for full functionality,
but version 0.7.0 or newer will work (albeit without some features enabled).

By default, the plugin will look for the Ludusavi executable in your `PATH` environment variable,
but you can also configure the plugin with the full path to the executable.

### Plugin
You can download the plugin from the
[releases page](https://github.com/mtkennerly/ludusavi-playnite/releases).
There are two ways to install it:

* Using the `*.pext` file:
  * Download `ludusavi-playnite-v*.pext`
  * Drag and drop the file into Playnite
* Using the `*.zip` file:
  * Download `ludusavi-playnite-v*.zip`
  * Extract it into a subfolder of the Playnite extensions directory
    (e.g., `C:/Users/<YOUR_NAME>/AppData/Local/Playnite/Extensions/Ludusavi`)

### Configuration
The plugin provides some of its own configuration (see the screenshots below),
but you'll also need to open Ludusavi to configure it as needed.
For example, at this time,
the plugin is not able to tell Ludusavi about your Playnite library sources,
so you'll need to configure Ludusavi's roots separately.

Behavior can be overridden for specific games by using tags:

| Tag                                       | Back up after play  | Restore before play | Back up non-PC platform before play | Restore non-PC platform after play |
|-------------------------------------------|---------------------|---------------------|-------------------------------------|------------------------------------|
| `[Ludusavi] Skip`                         | No                  | No                  | No                                  | No                                 |
| `[Ludusavi] Game: backup`                 | Yes, without asking |                     |                                     |                                    |
| `[Ludusavi] Game: backup and restore`     | Yes, without asking | Yes, without asking |                                     |                                    |
| `[Ludusavi] Game: no backup`              | No                  | No                  |                                     |                                    |
| `[Ludusavi] Game: no restore`             |                     | No                  |                                     |                                    |
| `[Ludusavi] Platform: backup`             |                     |                     | Yes, without asking                 |                                    |
| `[Ludusavi] Platform: backup and restore` |                     |                     | Yes, without asking                 | Yes, without asking                |
| `[Ludusavi] Platform: no backup`          |                     |                     | No                                  | No                                 |
| `[Ludusavi] Platform: no restore`         |                     |                     |                                     | No                                 |

For example, if the global backup-after-play option is enabled,
but a game is marked with `[Ludusavi] Skip`,
then it will not be backed up after play.
However, the game will still be included when doing a full backup of all games from the menu.

`[Ludusavi] Skip` takes precedence over the other tags,
and the `no` tags take precedence over their positive counterparts.

The following tags are only informational and do not have special behavior:

* `[Ludusavi] Backed up`
* `[Ludusavi] Unknown save data`

## Screenshots
### Prompt after exiting a game
> ![Screenshot of prompt after exiting a game](docs/prompt.png)

### Main menu actions
> ![Screenshot of main menu actions](docs/actions.png)

### Game menu actions
> ![Screenshot of game menu actions](docs/actions-per-game.png)

### Notifications
> ![Screenshot of notifications](docs/notifications.png)

### Settings
> ![Screenshot of settings](docs/settings.png)

## Other notes
* The backup and restore operations run in the background after you start them.
  You'll get a notification in Playnite when they finish.
  If you try to start another operation before the first one finishes,
  you'll have to wait.
* When processing specific games via the context menu,
  the plugin will try to look them up a few ways,
  in this order of precedence:

  * Steam ID (if applicable)
  * Title
  * Normalized title (if enabled in the plugin settings)
    * For example, "Some Game: Special Edition (2022)" normalizes to just "Some Game".

  Playnite and Ludusavi don't always use the same name for each game,
  so that's why these fallbacks are needed.
  If a match is found with a different name than in Playnite,
  then the notification will include `Playnite Title (↪ Ludusavi Title)`.
* After doing a full backup/restore of all games, you can click the notification to see the list.
  There, "+" denotes a newly found game, and "Δ" denotes a game whose saves have changed.
* For backups, the plugin always sets Ludusavi's `--merge` flag.
  This way, if you back up saves for one game,
  it will not interfere with any backups you may have for another game.
* Although Ludusavi only knows about PC games by default,
  you can add custom entries in Ludusavi for non-PC games if you'd like.
  The plugin provides a setting to either look up those custom entries
  by the game's name on its own or with a suffix,
  in case you need multiple custom entries for the same game name on different platforms.

  Within the suffix, you can write `<platform>`
  and it will be replaced by the actual platform name.
  For example, if you enable the default suffix
  and you'd like a custom entry for `Some Game` on Nintendo 64,
  then in Ludusavi you would create a custom entry called `Some Game (Nintendo 64)`.
* If you want a single custom entry for all games on a particular non-PC platform
  (such as when all of the saves for that platform are in a single memory card file),
  you can enable the "back up save data by platform name" setting.
  You will just need to create a custom entry in Ludusavi with the same name as the platform.

## Development
Please refer to [CONTRIBUTING.md](./CONTRIBUTING.md).

## 致谢

- [mtkennerly/ludusavi-playnite](https://github.com/mtkennerly/ludusavi-playnite) — 原版插件
- [mtkennerly/ludusavi](https://github.com/mtkennerly/ludusavi) — 存档备份工具本体
- [Goldberg Steam Emulator](https://gitlab.com/Mr_Goldberg/goldberg_emulator) — 让非 Steam 游戏也有 steam_appid.txt
