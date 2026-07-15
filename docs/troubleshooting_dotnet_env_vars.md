# .NET Configuration 与 Docker 扁平环境变量的坑

## 现象描述

在使用 Docker 部署 .NET 应用时，我们通常在 `docker run` 时通过 `-e` 参数传递环境变量，例如 `-e SERVER_URL="http://ip:port"`。
然而，在 .NET 应用程序内部，这些环境变量似乎无法覆盖 `appsettings.json` 中的默认值。

## 原因分析

.NET 的 `ConfigurationBuilder` 依赖键的**层级结构**来合并不同来源的配置。

在 `appsettings.json` 中，配置通常是嵌套的：
```json
{
  "NodeConfig": {
    "ServerUrl": "http://localhost:15002"
  }
}
```
此时，该配置项在 .NET 内部的扁平化 Key 是 `NodeConfig:ServerUrl`。

### 误区 1：环境变量的自动映射
默认的 `AddEnvironmentVariables()` 会读取所有系统环境变量。如果在 Docker 中传入 `-e SERVER_URL="..."`，它在 Configuration 中的 Key 仅仅是 `SERVER_URL`，**而不会自动去匹配** `NodeConfig:ServerUrl`。

如果想要完全遵循 .NET 的约定，Docker 启动命令必须写成 `-e NodeConfig__ServerUrl="..."`（使用双下划线表示层级）。但这对于外部使用者（特别是熟悉常规 Docker 应用的用户）来说，非常反直觉。

### 误区 2：手动兜底逻辑的陷阱
为了兼容扁平的自定义环境变量，开发者可能会编写类似的“手动兜底”读取逻辑：

```csharp
string GetConfigValue(string configKey, string configSectionKey, string fallbackEnvVar, string defaultValue)
{
    // 1. 先尝试读取层级配置
    var val = config[$"{configSectionKey}:{configKey}"];
    if (!string.IsNullOrWhiteSpace(val)) return val;
    
    // 2. 尝试读取同名属性
    val = config[configKey];
    if (!string.IsNullOrWhiteSpace(val)) return val;

    // 3. 最后兜底读取系统环境变量
    val = Environment.GetEnvironmentVariable(fallbackEnvVar);
    if (!string.IsNullOrWhiteSpace(val)) return val;
    
    return defaultValue;
}

// 预期逻辑：如果用户没在 appsettings.json 里配，就读 SERVER_URL 环境变量
string baseUrl = GetConfigValue("ServerUrl", "ClientConfig", "SERVER_URL", "http://localhost:15002");
```

**致命 Bug**：由于发布时的 `appsettings.json` 中已经包含了 `"ServerUrl": "http://localhost:15002"` 这个默认值，所以代码在执行 `config["NodeConfig:ServerUrl"]` 时，**返回值永远不为空（至少是默认值）**。因此，代码在第一步就直接返回了默认值，**永远无法执行到第 3 步去读取 `SERVER_URL` 环境变量**。

从而导致：Docker 传入的环境变量形同虚设，应用“死锁”在了默认配置上。

## 最佳实践与解决方案

**不要使用手动的 GetConfigValue 兜底逻辑**，而是利用 .NET 原生的 `ConfigurationBuilder` 优先级机制，在构建配置前**主动将扁平环境变量映射到层级 Key 中**。

正确的优先级应当是：**命令行 > 自定义环境变量（Docker） > appsettings.json**。

### 修复代码示例

在 `ConfigurationBuilder` 构建之前，读取系统的扁平环境变量，映射到强类型的字典，并通过 `AddInMemoryCollection` 覆盖在默认的环境变量和 Json 文件之上：

```csharp
// Setup configuration
var env = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production";

// 1. 建立扁平环境变量 -> 层级配置Key 的映射表
var envMappings = new Dictionary<string, string>
{
    { "SERVER_PORT", "ServerConfig:Port" },
    { "SERVER_URL", "NodeConfig:ServerUrl" },
    { "NODE_NAME", "NodeConfig:NodeName" },
    { "NODE_IP", "NodeConfig:NodeIp" }
};

var memConfig = new Dictionary<string, string>();
foreach (var mapping in envMappings)
{
    // 2. 主动从系统中提取这些特定变量
    var envVal = Environment.GetEnvironmentVariable(mapping.Key);
    if (!string.IsNullOrWhiteSpace(envVal))
    {
        memConfig[mapping.Value] = envVal;
    }
}

// 3. 构建配置链
var builder = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile($"appsettings.{env}.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables()
    // 关键点：挂载手动映射的内存集合，它的优先级高于 appsettings.json
    .AddInMemoryCollection(memConfig) 
    .AddCommandLine(args, switchMappings);

var config = builder.Build();
```

通过这种方式，在程序的后续逻辑中，只需统一且安全地使用标准的 `.GetValue`：

```csharp
string baseUrl = config.GetValue<string>("NodeConfig:ServerUrl", "http://localhost:15002");
```
这保证了 `SERVER_URL` 能够完美覆写 `appsettings.json` 中的 `NodeConfig:ServerUrl`，同时保持了代码的极简与原生行为。
