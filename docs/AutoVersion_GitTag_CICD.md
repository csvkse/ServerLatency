# 通过 Git Tag 自动注入程序集版本号

## 📝 背景与问题

在 v1.0.9 之前，项目的版本号分散在以下两处，每次发版需要手动同步修改：

1. `ServerLatency.csproj` 中的 `<Version>` 字段
2. `Program.cs` 中的硬编码字符串：
   ```csharp
   Console.WriteLine(" ServerLatency (LatencyMatrix) - v1.0.8");
   ```

这导致极易出现版本不一致、忘记修改等低级错误。理想的发版工作流应当是：**只推送一个 `git tag`，其余全部自动完成**。

---

## 🛠️ 解决方案

### 改动一：`Program.cs` 读取程序集版本（运行时动态获取）

删掉硬编码字符串，改为通过反射读取程序集的 `AssemblyVersion`：

```csharp
var version = typeof(Program).Assembly.GetName().Version;
var versionStr = version != null ? $"v{version.Major}.{version.Minor}.{version.Build}" : "unknown";

Console.WriteLine("==========================================================");
Console.WriteLine($" ServerLatency (LatencyMatrix) - {versionStr}");
Console.WriteLine("==========================================================");
```

`AssemblyVersion` 由编译器从 `<Version>` 属性写入，运行时读取即可，**无需再手动维护任何字符串**。

---

### 改动二：`ServerLatency.csproj` 仅作本地默认值

`.csproj` 中的 `<Version>` 字段保留，但它只用于本地 `dotnet run` / `dotnet build` 的默认值，**CI 发版时会被覆盖**：

```xml
<PropertyGroup>
  <Version>1.0.9</Version>  <!-- 本地开发默认值，CI 会通过 -p:Version= 覆盖 -->
</PropertyGroup>
```

> 每次发版后，将此处同步更新到下一个目标版本（如 `1.0.10`），作为下一次本地开发的默认值即可。

---

### 改动三：`desktop-release.yml` 从 Git Tag 提取版本并注入

在 GitHub Actions 的 `Publish Native AOT` 步骤中，从 `github.ref`（如 `refs/tags/v1.0.9`）提取纯版本号（去掉 `v` 前缀），然后通过 `-p:Version=` 参数传给 `dotnet publish`：

```yaml
- name: Publish Native AOT
  shell: bash
  run: |
    REF="${{ github.ref }}"
    if [[ "$REF" == refs/tags/v* ]]; then
      VERSION="${REF#refs/tags/v}"
    else
      VERSION=""
    fi

    if [ -n "$VERSION" ]; then
      echo "Using version from tag: $VERSION"
      dotnet publish ServerLatency/ServerLatency.csproj -c Release -r ${{ matrix.rid }} \
        -o publish_output -p:Version=$VERSION
    else
      echo "No tag version, using csproj default version"
      dotnet publish ServerLatency/ServerLatency.csproj -c Release -r ${{ matrix.rid }} \
        -o publish_output
    fi
```

> **注意**：此步骤中使用了 `shell: bash` 显式指定解释器。这是因为矩阵构建中包含 Windows Runner，其默认 Shell 为 PowerShell，不支持 `${REF#refs/tags/v}` 这类 bash 字符串操作语法。

---

## 🔗 完整数据流

```
开发者操作：git tag v1.1.0 && git push origin v1.1.0
                           │
                           ▼
              GitHub Actions 触发 (push tags: v*)
                           │
                ┌──────────┴──────────┐
                │                     │
        desktop-release.yml     docker-build.yml
                │                     │
    提取 tag → "1.1.0"     docker/metadata-action
    dotnet publish           type=semver → "1.1.0" / "1.1"
    -p:Version=1.1.0                 │
                │              推送镜像标签 1.1.0 / 1.1 / latest
    Assembly 版本 = 1.1.0            │
                │                    │
    Program.cs 运行时读取             │
    → 打印 "v1.1.0"                  │
                │                    │
    打包各平台二进制 (.tar.gz / .zip) │
                │                    │
        softprops/action-gh-release  │
        → 创建 GitHub Release v1.1.0 │
```

---

## 💡 经验总结

1. **版本号的唯一真实来源应是 Git Tag**，代码内不应有任何硬编码版本字符串。
2. `dotnet publish -p:Version=X.Y.Z` 可在不修改 `.csproj` 文件的前提下覆盖程序集版本，是 CI/CD 注入版本的标准姿势。
3. 在多 OS Runner 的矩阵构建中，若 `run` 步骤涉及 bash 语法，**务必显式声明 `shell: bash`**，否则在 Windows Runner 上将静默失败或报错。
4. 对于 Docker 镜像，`docker/metadata-action` 的 `type=semver` 模式会自动从 git tag 中提取版本号并生成标准镜像标签，**无需手动拼接**。
