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
这是一个极度隐蔽的 Native AOT JSON 序列化限制导致的问题。在 JIT 模式下，SignalR 的内部反射机制可以动态处理任意类型；但在 **Native AOT 模式**下，`System.Text.Json` 的序列化能力完全由 `JsonSerializerContext` 的静态声明决定，**任何未注册的类型都会在运行时抛出 `NotSupportedException`**，且该异常发生在 SignalR 的 Transport 内部管道，不会被应用层日志捕获，造成无报错秒断的假象。

此问题在项目演化过程中出现过两个独立的子案例：

#### 子案例 A：缺少 `string` 类型（早期版本）
服务端在 Hub 方法内调用 `Clients.Caller.SendAsync("Welcome", someString)` 时，SignalR 框架底层需要将参数 `string` 序列化后推送给客户端。由于 `AppJsonContext` 中遗漏了对 `typeof(string)` 的注册，序列化直接失败，连接被硬切断。

#### 子案例 B：缺少 `object[]` 类型（v1.0.9，2026-07-15）
客户端执行以下调用时：
```csharp
await _connection.InvokeAsync("JoinPingNode", _nodeName, _accessKey, _nodeIp, cancellationToken);
```
SignalR 客户端框架会将多个参数（`name`、`key`、`nodeIp`）打包成一个 **`object[]`** 数组发送给服务端。服务端在反序列化该数组时，发现 `object[]` 未在 `AppJsonContext` 中注册，于是在协议解析层抛出异常，导致 Hub 方法 `JoinPingNode` 的业务代码根本未能执行。

此时症状极具迷惑性：
- 若服务端日志中**完全没有**出现任何业务打印（如 `[Auth Failed]`、节点注册成功日志），则几乎可以确定是序列化层崩溃，而非业务逻辑拦截。
- 客户端抛出 `TaskCanceledException`，极易被误判为密钥错误或网络问题。

### 解决方案
在 `AppJsonContext.cs` 中，将所有 SignalR 内部可能用到的底层类型一并显式注册：
```csharp
[JsonSerializable(typeof(object))]
[JsonSerializable(typeof(object[]))]  // <- SignalR InvokeAsync 的参数包装类型，必须注册！
[JsonSerializable(typeof(string))]    // <- SendAsync 传递基础字符串时必须注册
public partial class AppJsonContext : JsonSerializerContext
{
}
```

> **经验法则**：在 Native AOT + SignalR 项目中，`AppJsonContext` 除了注册自定义业务模型外，还必须注册 `string`、`object`、`object[]` 这三个基础类型，否则极易踩坑。

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
