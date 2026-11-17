# Momoko 启动器

Windows 多游戏启动器。玩家用 Momoko 通行证登录一次，在左侧列表里选择游戏服务器，然后开始游戏。当前接入的游戏是学园偶像大师（`gakumas`）。

- 宿主程序：C#（.NET 10），Win32 无边框窗口。
- 界面：WebView2 渲染的 HTML/CSS（源码在 `ui/`），编译进程序，有浅色、深色和跟随系统三种主题。
- 发布物只有一个 exe：界面、`version.dll`、许可说明和 WebView2 加载器都在程序里。游戏图标和背景由 Momoko 下发（见「游戏图标与背景」）。
- 登录协议：`docs/login-protocol.md`。
- 仓库：<https://github.com/BanterSR/launcher>，用户版通过 GitHub Releases 发布。

## 两个版本

| 版本 | 产物 | 说明 |
| --- | --- | --- |
| 用户版 | `dist\user\MomokoLauncher.exe` | 单个 exe：NativeAOT、按体积优化，静态链接 WebView2 加载器，不依赖 .NET 运行时。不显示服务器地址，也不含任何开发功能。 |
| 开发版 | `dist\dev\MomokoLauncher.Dev.exe` | 单个 exe：依赖 .NET 10 运行时，WebView2 加载器打包在 exe 里。开发功能只在 `DEV` 编译符号下存在。 |

开发版额外提供：

- 运行日志面板。
- 每台服务器的 SDK、游戏服务端、资源服务器地址覆盖。
- Momoko 地址覆盖。
- 游戏账号密码登录：直接调用游戏服务器 SDK 的 `POST /account/login`，不经过 Momoko。只有以 `-tags dev` 编译的游戏服务器提供这个入口。
- 客户端控制台开关、汉化字体、额外启动参数。
- “仅登录”和“取消操作”。

开发版界面在 `ui/dev/`，只编译进开发版。两个版本使用不同的窗口类和单实例互斥量，可以同时运行。

## 构建

需要：

- .NET SDK 10；
- MSVC x64 工具链，用于 NativeAOT 链接和编译 `games/gakumas/native` 的 `version.dll`。

```bat
build.cmd [版本号]
```

版本号为 `X.Y.Z`，编进两个版本（`BuildInfo.Version`）；不传时是本地构建（`0.0.0-local`），不检查更新。

`build.cmd` 依次发布用户版和开发版：先发布到 `obj\publish\<版本>`，再只把 exe 复制到 `dist\user`、`dist\dev`，`dist` 里运行时生成的 `data\`、`log\` 不受影响。它先使用当前环境里的 MSVC（`VCToolsInstallDir` 已设置，例如先调用了 `vcvars64.bat` 或便携 MSVC 的 `setup_x64.bat`）；没有设置时，通过 vswhere 查找 Visual Studio。

内置的 Momoko 通行证地址由构建属性 `MomokoOrigin` 决定，默认 `https://user.alsl.xyz`，例如 `dotnet publish -p:MomokoOrigin=https://passport.example.com`。

`version.dll` 的源码变化后，构建时会自动重新编译它；传 `-p:SkipNativeBuild=true` 跳过这一步。

用户版固定使用 10.0.15 版的 NativeAOT 编译器和运行时包，因为 SDK 自带的 10.0.12 有安全公告 GHSA-p3jp-9v83-76w9。

## 发布与更新检查

- **自动发布：**`.github/workflows/release.yml`。推送标签 `vX.Y.Z`，或在 Actions 里手动运行并填写版本号（在当前提交上创建标签），工作流在 `windows-latest` 上安装 .NET 10 SDK，运行 `build.cmd X.Y.Z`，再把 `dist\user\MomokoLauncher.exe` 发布为 GitHub Release `vX.Y.Z`（说明自动生成）。开发版不发布。
- **更新检查（只在用户版）：**界面就绪后请求一次 `https://api.github.com/repos/BanterSR/launcher/releases/latest`，也可以在“设置 → 关于 → 检查更新”里手动检查。
  - 最新发布的标签必须是 `vX.Y.Z`；比当前版本新时，左侧栏和登录页出现“发现新版本”，点击在浏览器中打开该版本的发布页。
  - 请求走系统代理，超时 15 秒；失败只记日志，手动检查时提示原因。
  - 仓库由构建属性 `UpdateRepository` 决定，默认 `BanterSR/launcher`。
  - 开发版和本地构建不检查。

## 目录

发布物只有 exe。运行时写入的文件都在程序旁边，整个文件夹可以直接移动或复制。

| 路径 | 内容 |
| --- | --- |
| `data/settings.json` | 主题、设备 ID、选中的服务器、账号资料缓存、各游戏的程序路径与汉化开关；开发版还有开发设置 |
| `data/webview2/` | WebView2 用户数据 |
| `data/media/` | 游戏图标和背景的缓存 |
| `data/games/<游戏>/` | 游戏运行时数据：待清理标记、导入的汉化包 |
| `log/` | `launcher.log`、客户端日志 `client-*.log`、Hook 日志 |

运行需要 Microsoft Edge WebView2 运行时；缺少时启动器会提示安装。

## 通行证登录

启动器不接触通行证的账号密码，登录在系统浏览器里完成（协议见 `docs/login-protocol.md` 第 1 节）。通行证网页支持什么登录方式，玩家就用什么方式登录，启动器不受影响。

1. **启动时：**从 Windows 凭据管理器读取通行证刷新令牌（凭据名 `Momoko.Launcher/Passport/<Momoko 地址>`），再调用 `POST /api/v1/auth/refresh` 换取访问令牌。
   - 没有保存的登录，或刷新被拒：显示登录页。
   - 连不上 Momoko：显示离线页，可以重试或退出登录。
2. **登录：**点击“登录通行证”。
   - 启动器调用 `POST /api/v1/auth/client`（`client=launcher`，设备名 `Momoko 启动器 (Windows)`，开发版为 `Momoko 启动器 Dev (Windows)`；`deviceId` 在首次运行时生成），在系统浏览器中打开返回的授权页，并在窗口里显示验证码。
   - 玩家在浏览器里登录通行证，核对验证码后点击“授权”。
   - 启动器按返回的间隔轮询 `POST /api/v1/auth/client/token`，拿到会话后保存刷新令牌、读取账号资料，并把窗口切回前台。
   - 拒绝或过期都会回到登录页并显示原因；可以随时取消，也可以重新打开浏览器。
   - 每次刷新都会轮换令牌，新的刷新令牌立即写回凭据管理器。访问令牌只在内存中。
3. **注册、找回密码、账号管理：**在系统浏览器中打开通行证网页 `/register`、`/forgot`、`/account`。
4. **退出登录：**调用 `POST /api/v1/auth/logout`，删除保存的刷新令牌，回到登录页。

## 游戏图标与背景

- `GET /api/v1/launch/apps` 的每个启动目标带 `icon`（游戏图标）和 `background`（游戏页背景，图片或 mp4 / webm 视频）两个链接。管理员在 Momoko 服务器的「接入设置 → 客户端」里上传，文件放在 Momoko 配置的对象存储里。
- 启动器把链接对应的文件下载到 `data\media\`（文件名是链接的 SHA-256 加原扩展名），页面通过 WebView2 虚拟主机 `https://media.momoko.example/` 显示本地文件。
  - 链接没变：直接用缓存，不再下载。
  - 链接变了：下载新文件；每次同步后，不再被任何启动目标使用的旧文件会被删除（正在播放的视频暂时删不掉时，下次刷新再删）。
  - 每次读取服务器列表（启动、登录、点击刷新）后同步，下载在后台进行，每下完一个文件界面就更新。单个文件最大 200 MB，下载走系统代理。
- 标题是 Momoko 报告的游戏名称。没有图标的服务器在左侧栏显示游戏名称的首字，没有背景时游戏页只有底色。

## 服务器列表与游戏账号

- 每台可启动的游戏服务器在 Momoko 中有一个客户端应用 ID（`appId`）。启动器用它指明目标服务器，所有启动相关请求都以它为路径参数。
- 登录后调用 `GET /api/v1/launch/apps`。
  - 列表只保留启动器有对应模块的游戏（当前只有 `gakumas`），按 Momoko 返回的顺序排列。
  - 玩家在左侧选择服务器，选择按应用 ID 保存。
  - 点击刷新按钮会重新获取。
- 在这台服务器上还没有游戏账号时，界面提供两种方式：
  - **绑定已有账号：**输入游戏账号和密码，调用 `POST /api/v1/launch/apps/{appId}/bind`。
  - **新建账号：**调用 `POST /api/v1/launch/apps/{appId}/account`。
- Momoko 返回 `bindable=false` 时显示原因：
  - `unsupported`：服务器不支持；
  - `not_visible`：服务器未开放；
  - `cooldown`：解绑冷却中，只能重新绑定原账号。

## 开始游戏（学园偶像大师）

1. **会话：**申请启动票据 `POST /api/v1/launch/apps/{appId}/ticket`，再用票据调用这台服务器 SDK 的 `POST /passport/ticket-login`，得到游戏会话。票据里带有应用 ID，并由这台服务器的应用密钥签名；游戏服务器只接受发给自己应用 ID、并能用自己的应用密钥验证的票据。启动器不接触应用密钥。
2. **检查游戏程序：**
   - 文件名必须是 `gakumas.exe`，同目录要有 `gakumas_Data\app.info`；
   - 默认路径为 `D:\game\gakumas\gakumas.exe`，可在设置中更改。
3. **检查运行环境：**
   - 启动器以管理员身份运行；
   - 游戏目录里没有程序在运行：游戏目录（不含子目录）中任一 exe 的进程都算，启动器自己的进程除外（启动器可以放在游戏目录里）；有的话报出进程路径和 PID；
   - 上一次会话的残留已清理完。
4. **安装 Hook：**把程序内置的 `version.dll` 写到游戏程序旁，并写入安装记录 `version.dll.gakumas.json`（SHA-256 + 产品名）。属于其他加载器的 `version.dll` 不会被覆盖。
5. **启动游戏：**以管理员身份启动 `gakumas.exe`。
   - 参数为 `/viewer_id`、`/onetime_token`、`/open_id`、`/pf_access_token`，以及 `-logFile log\client-*.log`。
   - 启动上下文通过环境变量 `GAKUMAS_LAUNCH_CONTEXT`（版本 `9`）和 `GAKUMAS_LAUNCH_CONTEXT_*` 字段传给 Hook，包括：
     - SDK、游戏服务端地址，以及配置了的资源服务器地址；
     - SDK 的刷新令牌与平台令牌、用户 ID、平台 ID；
     - 日志路径、控制台开关；
     - 汉化目录与字体。
6. **运行中：**界面显示运行状态。关闭启动器只会把窗口最小化，等游戏退出、清理完成后再自动关闭。

同一游戏一次只运行一份；在另一台服务器上运行时，开始按钮不可用。

## 退出清理

游戏目录中所有程序的进程（启动器自己除外）都退出后，启动器做两件事：

1. 删除游戏目录里属于本启动器的 `version.dll` 和它的安装记录。判断依据是 SHA-256 与安装记录一致，或与程序内置的 `version.dll` 一致。
2. 删除客户端存档 `RefreshTokenLocalSaveData`，避免之后用 DMM GAMES Player 启动时，客户端拿本地签发的令牌去官方刷新而失败。

其他存档、资源缓存和 `LocalLow` 目录都不动。启动时写入待清理标记 `data\games\gakumas\session.json`，启动器异常退出后，下次启动会先完成清理。

## 汉化

- 在“设置 → 学园偶像大师 → 导入新包”中选择汉化 zip。
- 包内文件校验通过后，解压到 `data\games\gakumas\localization`，并在 `.import.json` 中记录来源、版本、导入时间、文件数和跳过的文件。
- 只有已导入汉化包时，“启用汉化”开关才可用。
- 游戏运行时不能导入或切换。
- 字体默认 `C:\Windows\Fonts\msyh.ttc`；开发版可以修改。

## 原生 Hook

`games/gakumas/native` 是 C++ 写的 `version.dll` 代理（MinHook 1.3.4），由游戏的 UnityPlayer 加载。它负责：

- 读取启动上下文；
- 把 SDK、游戏 API 请求重定向到所选服务器；服务器配置了资源服务器地址时，资源请求也重定向过去，否则使用官方资源；
- 加载汉化。

`build.cmd` 用 MSVC 编译它（`/W4 /WX`），编进启动器。第三方许可见 `THIRD_PARTY.md`，也可以在“设置 → 帮助 → 开源许可”里查看。

## 界面与宿主

界面由宿主推送的一条 `state` 消息整体渲染，用户操作以 `{type, ...}` 消息发回宿主。窗口的标题栏区域、游戏页背景和登录背景都可以拖动窗口。

界面是程序的嵌入资源，宿主通过 WebView2 的 `WebResourceRequested` 从内存提供：`https://ui.momoko.example/<路径>` 对应资源 `ui/<路径>`，找不到的返回 404。游戏图标和背景从 `https://media.momoko.example/`（映射到 `data\media\` 的虚拟主机）加载；页面的 CSP 只允许内置文件、https 和 data 图片，以及这个主机上的视频。

窗口最小化时隐藏 WebView，还原后再显示；只有窗口处于激活且没有最小化时才把键盘焦点交给 WebView（WebView2 在隐藏时拒绝 `MoveFocus`）。

WebView2 的设置如下：

- 状态栏、缩放、宿主对象、默认对话框、密码自动保存都已关闭；
- 开发者工具、右键菜单和浏览器快捷键只在开发版开启；
- 新窗口和外部链接交给系统浏览器打开，只允许 http(s)。
