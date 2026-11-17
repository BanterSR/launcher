# Momoko 通行证登录与启动票据

所有客户端（PC 启动器、Android APK）用同一套流程进入游戏：

1. 在系统浏览器里授权登录 Momoko 通行证，客户端不接触通行证的账号、密码或任何登录方式。
2. 用**客户端应用 ID** 指明目标服务器，向 Momoko 申请一次性启动票据。
3. 用票据向这台游戏服务器的 SDK 换取游戏会话。

游戏服务器不是 OIDC 客户端，Momoko 不提供面向玩家的 OIDC 授权页。游戏内的账号引继由通行证绑定决定（第 7 节）。游戏账号密码直接登录游戏服务器只存在于开发构建（第 8 节）。

## 1. 通行证登录（浏览器授权）

通行证服务地址记作 `{momoko}`（Momoko `passport.public_url`，例如 `https://user.alsl.xyz`）。接口前缀 `/api/v1`，JSON 字段为 camelCase。

- 成功：`{"code":200,"message":"success","data":{...}}`
- 失败：HTTP 状态码同 `code`，`{"code":<status>,"message":"PASSPORT_*"}`

客户端只通过浏览器授权取得通行证会话。通行证网页支持的任何登录方式（密码、邮箱验证码、两步验证等）都在浏览器里完成，客户端不受登录方式变化影响。

### 1.1 授权流程

1. **发起：**`POST /api/v1/auth/client`，无需登录。
   - 请求：`{client, deviceId, device}`。
     - `client`：`launcher` 或 `android`，Momoko 内置这两种客户端的显示名（“Momoko 启动器”“学马仕 Android”），其他值返回 400 `PASSPORT_INVALID_ARGUMENT`。
     - `deviceId`：客户端首次运行生成并持久保存的随机 ID，同一设备只保留一个会话。
     - `device`：设备名，启动器为 `Momoko 启动器 (Windows)`，APK 为 `学马仕 Android`。
   - 返回：`{deviceCode, userCode, verificationUrl, interval, expiresIn}`。
     - `deviceCode`：32 字节随机数的无填充 base64url，只给客户端，用于领取会话。
     - `userCode`：8 个字符，字母表 `BCDFGHJKLMNPQRSTVWXZ`，显示为 `XXXX-XXXX`。
     - `verificationUrl`：`{momoko}/authorize?code=XXXX-XXXX`。
     - `interval` 为 2（秒），`expiresIn` 为 600（秒）。
   - 同一 IP 发起过于频繁时返回 429 `PASSPORT_TOO_MANY_REQUESTS`。
2. **打开浏览器：**客户端用系统浏览器打开 `verificationUrl`，并在自己的界面上显示 `userCode`，提示玩家在浏览器里核对并授权。
3. **网页授权（`{momoko}/authorize`）：**
   - 没有登录时先走通行证网页的正常登录，登录后回到授权页。
   - 授权页显示客户端名称、设备名、`userCode` 和发起时间，提供“授权”和“拒绝”。
   - 授权：`POST /api/v1/auth/client/approve {userCode}`；拒绝：`POST /api/v1/auth/client/deny {userCode}`。两个接口都需要网页会话的 Bearer。
   - `userCode` 不存在、已过期或已处理：404 `PASSPORT_AUTH_NOT_FOUND`。
4. **领取：**客户端每隔 `interval` 秒调用 `POST /api/v1/auth/client/token {deviceCode}`，无需登录。

   | 状态 | message | 客户端处理 |
   | --- | --- | --- |
   | 200 | — | 返回 `{accessToken, refreshToken, expiresIn}`，为授权的通行证用户建立这台设备的会话（`deviceId`、`device`）；`deviceCode` 作废 |
   | 400 | `PASSPORT_AUTH_PENDING` | 还没有授权，继续轮询 |
   | 400 | `PASSPORT_AUTH_SLOW_DOWN` | 轮询过快，间隔加 2 秒后继续 |
   | 400 | `PASSPORT_AUTH_DENIED` | 玩家拒绝，结束 |
   | 400 | `PASSPORT_AUTH_EXPIRED` | 已过期、已领取或不存在，结束 |

   客户端取消时停止轮询即可，授权请求到期后自然失效。

### 1.2 会话

| 接口 | 请求 | 返回 data |
| --- | --- | --- |
| `POST /api/v1/auth/refresh` | `{refreshToken}` | `{accessToken, refreshToken, expiresIn}`，访问令牌与刷新令牌一起轮换，旧令牌立即失效 |
| `POST /api/v1/auth/logout` | 需要 Bearer | 删除本设备会话 |
| `GET /api/v1/me` | 需要 Bearer | `PassportMe`（`id, username, name, avatar, ...`） |

- 客户端长期保存刷新令牌；访问令牌只放在内存里，有效期 `expiresIn` 秒，过期后调用刷新接口。
- 注册、找回密码、账号管理都在通行证网页完成：`{momoko}/register`、`{momoko}/forgot`、`{momoko}/account`。
- 通行证网页自身的登录接口（`/api/v1/auth/login` 等）只供网页使用，客户端不调用。

## 2. 客户端应用凭据

每台支持客户端启动的游戏服务器在 Momoko 中有一组客户端应用凭据：应用 ID 和应用密钥。

- **应用 ID（`appId`）：**公开标识，客户端靠它指明要登录哪台服务器。
  - 格式为 `app_` 加 16 个小写 base32 字符（10 字节随机数，RFC 4648 字母表转小写，无填充），全局唯一。
- **应用密钥（`appSecret`）：**Momoko 用它签发启动票据，游戏服务器用它验证票据确实来自 Momoko。
  - 格式为 32 字节随机数的无填充 base64url（43 个字符）。
  - Momoko 加密保存。
  - 只配置在游戏服务器上，不进入任何客户端。

**生成与重置**（管理员在服务器“连接设置 → 客户端”中操作）：

- **生成：**第一次生成时，同时得到应用 ID 和应用密钥。
- **重置密钥：**只换应用密钥。旧密钥立即失效，需要更新游戏服务器配置；已发布的 APK 不受影响。
- **重新生成应用 ID：**同时换应用 ID 和应用密钥。旧凭据立即失效，需要更新游戏服务器配置并重新打包 APK。
- 两种重置都要先确认。

**配置位置：**

| 位置 | 应用 ID | 应用密钥 |
| --- | --- | --- |
| Momoko | `game_servers.client_app_id` | 加密保存 |
| 游戏服务器配置（gakumas） | `momoko.app_id` | `momoko.app_secret` |
| Android APK | `Bootstrap.APP_ID`，打包时写入 | 无 |
| PC 启动器 | 不内置，从第 3 节的列表里取得，由玩家选择服务器 | 无 |

**可启动的条件：**服务器的游戏适配器支持客户端启动、已接入 MGP、已生成应用凭据，并且填写了 SDK 与游戏服务端地址。不满足的服务器对客户端来说不存在。

客户端地址指 `client_sdk_url`、`client_game_url`、`client_resource_url`：不带路径的 HTTP(S) origin，由管理员在“连接设置 → 客户端”中填写。资源服务器地址可以不填，客户端这时使用官方资源。

## 3. 查询启动目标（Momoko）

两个接口都需要 Bearer，返回同一种结构 `PassportLaunchApp`：

```json
{
  "appId": "app_…",
  "server": GameServerBrief,
  "endpoints": {"sdk": "https://...", "game": "https://...", "resource": "https://..."},
  "link": PassportLink | null,
  "bindable": true,
  "blockReason": "",
  "icon": "https://...",
  "background": "https://..."
}
```

- **endpoints：**服务器的客户端地址。`resource` 为空时客户端使用官方资源。
- **icon、background：**启动器里的游戏图标和游戏页背景（图片，背景也可以是 mp4 / webm 视频）。管理员在 Momoko 服务器的「接入设置 → 客户端」里上传，文件放在 Momoko 配置的对象存储（系统设置 · 储存）里，这里是公开链接；没有上传时为空。换文件后链接随之改变。
- **link：**当前通行证在这台服务器上生效的绑定；`null` 表示还没有游戏账号。
- **bindable / blockReason：**没有绑定时，能否绑定或新建。`blockReason` 取值：
  - `unsupported`：服务器不支持；
  - `not_visible`：服务器未向该玩家开放；
  - `cooldown`：解绑冷却中，只能重新绑定原账号。

  可以绑定时 `blockReason` 为空。

### `GET /api/v1/launch/apps`

返回 `{"apps": [PassportLaunchApp]}`，供 PC 启动器使用。

- **列出：**可启动、并且满足以下任一条件的服务器：
  - 玩家在这台服务器上有生效的绑定；
  - 服务器对玩家可见：公开，或是玩家注册邀请码所属的服务器。
- **排序：**有绑定的在前，按最近使用倒序；其余按服务器名称排序。

### `GET /api/v1/launch/apps/{appId}`

返回这一个 `PassportLaunchApp`，供单服务器客户端（APK）使用。

- 服务器对玩家不可见、也没有绑定时，照常返回，`bindable=false`，`blockReason=not_visible`。
- 应用 ID 不存在，或对应服务器不可启动：404 `PASSPORT_APP_NOT_FOUND`。

## 4. 游戏账号（Momoko）

两个接口都需要 Bearer，都返回 `PassportLink`：

| 接口 | 请求体 | 作用 |
| --- | --- | --- |
| `POST /api/v1/launch/apps/{appId}/bind` | `{account, password}` | 把这台服务器上已有的游戏账号绑定到当前通行证 |
| `POST /api/v1/launch/apps/{appId}/account` | `{}` | 经 MGP `account.create` 新建游戏账号并绑定，绑定方式为 `create` |

- **绑定规则：**与通行证网页的 `POST /api/v1/bind` 相同，账号密码经 MGP 向游戏服务器核对。
- **前提：**服务器可见、适配器支持、不在解绑冷却期、尚无绑定。
- **串行：**同一用户与同一服务器的操作串行处理。
- **错误码：**
  - `PASSPORT_APP_NOT_FOUND`
  - `PASSPORT_ALREADY_LINKED`
  - `PASSPORT_SERVER_NOT_BINDABLE`
  - `PASSPORT_BIND_UNSUPPORTED`
  - `PASSPORT_BIND_COOLDOWN`
  - `PASSPORT_GAME_UNAVAILABLE`
  - 绑定另有：`PASSPORT_GAME_CREDENTIALS_INVALID`、`PASSPORT_GAME_PLAYER_NOT_FOUND`、`PASSPORT_GAME_ACCOUNT_TAKEN`、`PASSPORT_BIND_TOO_MANY_ATTEMPTS`

## 5. 启动票据（Momoko 签发）

`POST /api/v1/launch/apps/{appId}/ticket`，请求体为 `{}`，需要 Bearer。

- **返回：**`{ticket, expiresIn, playerId, displayId}`，`expiresIn` 为 120。签发成功后更新绑定的最近使用时间。
- **错误：**

  | 状态 | 错误码 | 原因 |
  | --- | --- | --- |
  | 404 | `PASSPORT_APP_NOT_FOUND` | 应用 ID 不存在或服务器不可启动 |
  | 409 | `PASSPORT_GAME_ACCOUNT_REQUIRED` | 没有生效的绑定 |
  | 502 | `PASSPORT_GAME_UNAVAILABLE` | MGP 不可用 |

票据格式：

```
ticket  = "lt1." + b64url(payload) + "." + b64url(HMAC-SHA256(key, "momoko-launch-ticket.v1." + b64url(payload)))
payload = {"aid":<appId>,"sid":<服务器 id>,"pid":"<十进制游戏账号 id>","uid":<通行证用户 id>,"jti":<16 字节随机数的 b64url>,"iat":<Unix 秒>,"exp":<iat+120>}
```

- `b64url` 为无填充的 base64url。
- payload 为紧凑 JSON，字段按上面的顺序排列。
- `aid`、`sid`、`pid`、`uid`、`jti` 都是字符串，`iat`、`exp` 是整数。
- `key` 为该服务器应用密钥字符串的 UTF-8 字节。MGP 密钥只用于 MGP 管理通道和客户端日志凭证，不参与启动票据。

## 6. 票据登录（游戏服务器 SDK）

`POST /passport/ticket-login`，请求体为 `{"ticket":"lt1...."}`。

游戏服务器必须同时配置应用 ID（`momoko.app_id`）和应用密钥（`momoko.app_secret`），否则返回 503 `TICKET_LOGIN_DISABLED`。

**校验顺序：**

1. 格式与前缀。
2. `aid` 等于本服 `momoko.app_id`。
3. HMAC 用本服 `momoko.app_secret` 计算，常量时间比较。
4. 时间：`iat ≤ now+60`，`exp > now`，`exp − iat ≤ 300`。
5. `jti` 只能用一次，内存记录保留到 `exp`。
6. `pid` 为大于 0 的十进制账号 ID。

通过后与其他登录方式走同一个开会话流程，返回：

```json
{"idToken","platformToken","refreshToken","userId","expiresIn","playerId","publicUserId"}
```

| 状态 | message | 含义 |
| --- | --- | --- |
| 400 | `invalid request` | 请求体无效 |
| 401 | `INVALID_TICKET` | 格式、签名、时间或 `aid` 不符，或票据已用过 |
| 404 | `PLAYER_NOT_FOUND` | 账号不存在 |
| 403 | `account banned` | 账号封禁中 |
| 503 | `TICKET_LOGIN_DISABLED` | 本服未配置应用 ID 或应用密钥 |

后续刷新照旧使用 `/securetoken/v1/token`。

## 7. 游戏内账号引继

游戏内原有的“数据引继”（Bandai Namco ID 连携）在本系统里表示“游戏账号已绑定 Momoko 通行证”。绑定关系只以 Momoko 为准，游戏内不输入任何账号密码，也不在游戏内切换账号。

- **连携状态：**票据登录成功时，游戏服务器把这个游戏账号的 Bandai Namco ID 连携记录写为票据中的通行证用户 `uid`。如果有别的游戏账号持有同一个 `uid`，它的连携记录同时移除。游戏内因此显示为“已连携”。
- **连携页（`/bnid/login.html`，正式构建）：**说明游戏账号的绑定由 Momoko 通行证管理，提供打开通行证账号页 `{momoko}/account` 的按钮和返回按钮。返回按钮回到客户端的 `backto`。这个页面不签发引继码。
- **游戏内解除连携、引继码迁移：**正式构建一律拒绝。解除或更换绑定在通行证账号页操作，更换账号用启动器或 APK 的退出登录。

## 8. 开发构建：游戏账号密码直接登录

游戏服务器 SDK 中直接用游戏账号密码注册或登录的入口只在开发构建里编译（gakumas：`go build -tags dev`），用于调试时跳过 Momoko。正式构建没有这些入口。

gakumas 开发构建的入口：

- `POST /account/register`
- `POST /account/login`
- `/bnid/login.html`：开发构建中是用 SDK 账号密码签发引继码的表单，替换第 7 节的正式页面

Momoko 绑定已有账号时经 MGP 核对游戏账号密码，这条路径不属于直接登录，正式构建保留。

PC 启动器开发版的“账号密码（测试）”登录方式调用 `POST /account/login`，只能用于开发构建的游戏服务器。用户版启动器和 APK 没有这个入口。

## 9. 客户端流程

### 共同部分

1. **读取保存的登录：**没有保存的通行证刷新令牌时，显示通行证登录。
2. **登录：**按第 1.1 节在浏览器里授权，领取会话后保存刷新令牌：启动器存 Windows 凭据管理器，APK 用 AndroidKeyStore 加密保存。
3. **退出登录：**调用 `POST /auth/logout`，并删除保存的刷新令牌。

### PC 启动器

1. 调用 `GET /launch/apps`，左侧列出所有启动目标，玩家选择一个。
2. 选中目标没有游戏账号时，玩家选择绑定已有账号（`/launch/apps/{appId}/bind`）或新建（`/launch/apps/{appId}/account`）。
3. 开始游戏：调用 `/launch/apps/{appId}/ticket`，再调用 `POST {endpoints.sdk}/passport/ticket-login`。

### Android APK

APK 内置 `APP_ID`、`SDK`、`MOMOKO` 三个值，SDK 地址还写在原生 Hook 里。

1. 调用 `GET /launch/apps/{APP_ID}`。返回的 `endpoints.sdk` 必须等于内置的 SDK 地址；不一致说明应用 ID 与客户端打包配置不符，直接报错，不进入游戏。
2. 没有游戏账号时，在客户端界面里绑定或新建，接口同 PC 启动器。
3. 开始游戏：调用 `/launch/apps/{APP_ID}/ticket`，再调用 `POST {SDK}/passport/ticket-login`。
