# Kestrel 在 Docker / K3S 反向代理环境下的真实 IP 获取与 WebSocket 断线问题总结

## 📝 现象与症状

在使用 `v1.0.3` 之前的版本时，将服务端 (Server) 部署在 K3S 或通过 Docker 端口映射启动，并在外网通过 Nginx/Traefik 等反向代理访问时，客户端 (Client) 会出现以下现象：

1. 客户端输出：
   ```
   Connecting to http://hs.h1.pw:15002/latencyHub via SignalR...
   Connected to SignalR Hub.
   [Connection Closed] 
   [Connection Error] A task was canceled.
   ```
2. 服务端输出：仅能看到一次成功的 `101 Switching Protocols` 握手，但紧接着 WebSocket 就在 500ms 内被关闭。
3. 没有任何报错：由于连接是静默切断的，`OnDisconnectedAsync` 或者 `Closed` 回调中不会带有 Exception 的详细信息。

---

## 🔍 根本原因分析

该问题由两个独立的底层逻辑相互作用产生：

### 1. ASP.NET Core 的反向代理安全限制
为了支持反向代理透传真实 IP，我们在 `ServerApp.cs` 中开启了 `UseForwardedHeaders` 中间件，试图读取代理服务器传来的 `X-Forwarded-For`。

然而，ASP.NET Core 对这个中间件的安全控制极其严格：
- 默认情况下，**`ForwardedHeadersOptions` 仅信任来自 `127.0.0.1` 和 `[::1]` 的代理源**。
- 在 Docker 或 K3S 网络下，经过内部网关转发的请求来源 IP（比如 `10.42.x.x` 或者 `172.17.x.x`）并不在 Kestrel 的信任白名单中。
- 因此，Kestrel 认为这个 `X-Forwarded-For` 头不可信并将其丢弃，导致某些极端边缘网络情况下（结合 IPv6 映射或无法还原底层 Socket IP），从 `Context.GetHttpContext()?.Connection.RemoteIpAddress` 拿到的是一个空值 (`null`)。

### 2. SignalR Hub 鉴权校验拦截
在原本的 `LatencyHub.cs` 业务逻辑中，为保证监控矩阵的严谨性，我们做了拦截：
```csharp
if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(ip))
{
    Context.Abort(); // 因为拿不到 IP，瞬间踢下线！
    return;
}
```
因为第一步的原因导致 `ip` 为空，服务端主动调用了 `Context.Abort()` 强制释放了连接，这才造成了客户端提示 `A task was canceled` 且毫无错误日志的"灵异"现象。

---

## 🛠️ 解决方案与代码实现

针对这个经典的反代 IP 问题，我们从两个层面进行了修复（已在 v1.0.4 和 v1.0.5 落地）：

### 修复 1：在业务层容错空 IP (v1.0.4)
不因获取不到底层物理 IP 而终止整个探测网络，赋予默认值 `"Unknown"`，保证长连接不被主动切断：
```csharp
var ip = string.IsNullOrWhiteSpace(clientIp) ? Context.GetHttpContext()?.Connection.RemoteIpAddress?.ToString() : clientIp;
ip = NormalizeIp(ip);

if (string.IsNullOrWhiteSpace(ip))
{
    ip = "Unknown";
}
```

### 修复 2：放开 Kestrel 的内网代理信任限制 (v1.0.5)
如果只是用 "Unknown" 替代，监控大屏上无法展示各个节点的真实地理分布。
通过强制清空白名单，让 Kestrel 无条件相信 Kubernetes / Docker 内部路由网关传进来的真实头部：
```csharp
var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
};
// 清空默认的白名单限制，信任所有来源代理
forwardedHeadersOptions.KnownNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeadersOptions);
```

### 备选修复 3：客户端绕过反代直接上报（客户端方案）
对于实在无法穿透反代的网络环境，还可以利用外网查询接口获取自身的公网 IP，作为配置通过 SignalR 参数传给服务端（`v1.0.6` 中已整合在命令行帮助中）：
```bash
./ServerLatency -m Client --Ip "$(curl -s https://api.ip.sb/ip)"
```

---

## 💡 经验教训

1. **SignalR 断线排查**：当出现没有任何 `Exception` 报错信息的瞬间断连（且客户端抛出 `TaskCanceledException`）时，首先应该排查**服务端是否调用了 `Context.Abort()`**，而非客户端序列化或通信逻辑的问题。
2. **Kestrel 代理穿透**：只要部署在 K8S/Docker 中，**必须注意 `ForwardedHeadersOptions` 的信任源限制**，`KnownNetworks` 和 `KnownProxies` 的默认策略经常是导致取不到客户端真实 IP 的隐形杀手。
