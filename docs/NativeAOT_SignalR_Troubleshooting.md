# ServerLatency 踩坑与经验总结

这份文档记录了在将 ASP.NET Core (.NET 10) 与 SignalR 结合，并通过 Native AOT (交叉编译) 部署至 Linux Docker / K3S 容器环境时遇到的典型坑点及解决方案。

## 1. 静态文件内嵌路径问题 (跨平台)

### 现象
在 Windows 本地使用 `dotnet run` 一切正常，但打包为 Docker 镜像并在 Linux 容器中运行时，服务端启动瞬间直接崩溃，日志报错：
```text
Unhandled exception. System.InvalidOperationException: Invalid path: 'Server\wwwroot'
```

### 原因
在 `ServerLatency.csproj` 和 `ServerApp.cs` 中，错误地使用了 Windows 专用的反斜杠 `\` 作为路径分隔符来配置 `ManifestEmbeddedFileProvider`。在 Linux 严格区分大小写且仅识别正斜杠 `/` 的环境下，读取内嵌资源会导致路径无效异常。

### 解决方案
统一修改所有相对路径中的分隔符为跨平台的正斜杠 `/`。
**Csproj:**
```xml
<EmbeddedResource Include="Server/wwwroot/**/*" />
```
**C#:**
```csharp
var embeddedProvider = new ManifestEmbeddedFileProvider(assembly, "Server/wwwroot");
```

---

## 2. SignalR 在 Native AOT 下的序列化幽灵 BUG (连接秒断)

### 现象
服务端没有抛出任何明显的异常，客户端连接正常打印 `Connected to SignalR Hub.`，但在发送握手指令并期待服务器返回时，连接立刻被单方面掐断，客户端报错：
```text
[Connection Closed] 
[Connection Error] A task was canceled.
```
服务端的 HTTP 请求日志中，只留下了一条持续时间仅约 `400ms` 的 101 WebSocket 协议升级记录，没有任何业务报错（`fail: ...`）。

### 原因
这是一个极度隐蔽的 Native AOT JSON 序列化限制导致的问题。
1. 在 `LatencyHub.JoinPingNode` 内部，服务端试图通过 SignalR 给客户端发送一个基础的字符串欢迎消息：
   `await Clients.Caller.SendAsync("Welcome", $"Connected as {name} ({ip})");`
2. 在 JIT 模式下，发送基础类型 `string` 畅通无阻。但在启用 Native AOT 并使用特定的 `JsonSerializerContext`（即 `AppJsonContext`）时，**任何未经静态扫描与注册的数据类型都无法被反序列化/序列化**。
3. 我们的 `AppJsonContext.cs` 中注册了自定义的响应对象（如 `MatrixResponse`、`List<string>` 等），但**漏掉了最原始的 `string` 类型**。
4. 导致底层 `System.Text.Json` 在尝试处理字符串序列化时抛出了 `NotSupportedException`。由于该异常发生在 SignalR 内部异步数据流通过程中，它直接导致了 Transport 层的 WebSocket 物理连接硬阻断，并没有将异常暴露给应用程序层的主日志捕获管道，造成了“无报错、秒断开”的假象。

### 解决方案
在专门处理 AOT 的序列化上下文中，显式地将所涉及的原生基础类型一并注册进去：
```csharp
[JsonSerializable(typeof(string))] // <- 必须显式注册基础类型
public partial class AppJsonContext : JsonSerializerContext
{
}
```

---

## 3. GitHub Actions 缓存与标签发布

### 现象
更新代码后，重新部署容器（`docker pull ghcr.io/csvkse/serverlatency:latest`），但容器仍抛出旧版本的 BUG。

### 原因
Native AOT 交叉编译（同时涵盖 AMD64 和 ARM64）相比普通 JIT 镜像构建需要长得多的耗时（通常 4~5 分钟）。如果流水线尚未合并并推送最新的多架构 Manifest，客户端提取到的 `latest` 标签仍将指向缓存或被上一次历史覆写提交覆盖的旧镜像。

### 解决方案
在生产和调试部署时：
1. **优先使用带确切版本号的 Tag**（例如 `v1.0.3`），而不要过度依赖 `latest`。
2. 了解 CI/CD 耗时，确保容器镜像完全合并推送完毕后再进行 `docker restart` 或 `docker pull` 操作。
3. 如果容器陷入 Crash Loop 重启死循环，必须先执行 `docker rm -f` 强制销毁旧容器实例，才能基于纯净状态拉取并启动新版本。
