# 项目信息

当前项目是一个基于 [Microsoft Agent Framework](https://github.com/microsoft/agent-framework) 开发的智能体编程工具，项目作为TUI的形式来编写和使用，核心场景：**通过 Agent 在终端中编程、执行任务、管理会话**

## 架构

- 后端：.NET 10 + Clean Architecture
- AI框架使用Microsoft.Agents.AI

## 项目开发约定

- 当前项目使用 C# / .NET。
- 执行 `dotnet` 命令时使用：`$HOME/.dotnet/dotnet`。
- 不执行 `git add`、`git commit` 等提交类命令。
- 不允许使用 git working-tree
- 不修改系统盘、`/usr` 等特殊目录下的原有文件。
- 修改已有文件前先读取文件内容，按现有命名、缩进、风格做最小改动。
- 废弃的源码文件应从磁盘删除，不通过 `.csproj` 的 `<Compile Remove=...>` 长期排除。
- 所有的功能能一步完成的，不要多设计两三步，除非这个设计是有必要的
- 代码编写的最大原则，不要为了抽象而抽象，能一步完成就不要两步三步，除非这个步骤是有很大的价值

## 编码规范

- 新增或修改代码必须保留清晰的 XML 注释，公开 DTO、枚举、接口方法和服务方法尤其需要说明用途。
- 当写一个复杂的代码块的时候，一定要补上行注释，说明每一个逻辑的意思
- [日志](doc/logging.md) - LoggerMessage 源生成器、日志规范、使用示例
- [JSON 序列化](doc/json-source-generation.md) - JsonSerializerContext 源生成器、类型注册、调用规范、NativeAOT 兼容
- 每次增加完新的配置后，都需要更新`readme.md`文档

## 项目分层约束
当前项目基于洋葱架构搭建
- Cli: 命令交互
- Infrastructure     外部适配器和基础设施层，比如agent的交互或者数据库的访问等
- Application ：负责业务逻辑的编写
- Domain: 负责抽象的实现和模型等定义

## 输出要求
- 输出语言：中文
- 输出的设计文档位置：`doc/design`，以 Markdown 文件为主
- 输出 Plan 时，均需写入`doc/plan` 目录下，以 Markdown 文件为主文件输出，使用正确的编码格式，例如UTF-8。
- 其它的一些临时文件，均需写入`doc/tmp` 目录下，以 Markdown 文件为主文件输出，使用正确的编码格式，例如UTF-8。

## 验证约定

- 修改完成后优先构建项目验证整体引用链：
  - `$HOME/.dotnet/dotnet build src/NarutoCodeCli/NarutoCodeCli.csproj`
- 构建结果需要关注 error；已有 warning 不应在无关任务中扩大修改范围。

## macOS arm64 发布约定

- 发布入口项目：`src/NarutoCodeCli/NarutoCodeCli.csproj`。
- CLI 项目显式设置 `<AssemblyName>narutocode</AssemblyName>`，确保二进制名称为 `narutocode`。

### 最小单文件交付模式

- 默认交付给用户时优先使用 ReadyToRun 单文件模式，而不是 `PublishAot=true`。
- 发布命令：
  - `$HOME/.dotnet/dotnet publish src/NarutoCodeCli/NarutoCodeCli.csproj -c Release -r osx-arm64 -o artifacts/publish/NarutoCodeCli/osx-arm64-singlefile-r2r`
- CLI 项目在 `Release + osx-arm64 + 非 PublishAot` 时启用：`SelfContained=true`、`PublishReadyToRun=true`、`PublishTrimmed=true`、`PublishSingleFile=true`、`IncludeNativeLibrariesForSelfExtract=true`、`DebugType=None`、`DebugSymbols=false`、`CopyOutputSymbolsToPublishDirectory=false`。
- `IncludeNativeLibrariesForSelfExtract=true` 用于把 `libe_sqlite3.dylib` 等原生库包含进单文件，运行时自动解压，避免交付目录额外携带 dylib。
- 发布完成后由代理主动删除符号文件：`find artifacts/publish/NarutoCodeCli/osx-arm64-singlefile-r2r -name "*.pdb" -delete -o -name "*.dSYM" -exec rm -rf {} +`，不要为此在项目中长期保留自定义 MSBuild Target。
- 发布完成后应检查发布目录只包含 `narutocode`：
  - `artifacts/publish/NarutoCodeCli/osx-arm64-singlefile-r2r/narutocode` 应为 `Mach-O 64-bit executable arm64`。
  - 可使用 `file artifacts/publish/NarutoCodeCli/osx-arm64-singlefile-r2r/narutocode` 验证架构。
- 如需交付压缩包，推荐在发布目录基础上生成：
  - `artifacts/publish/NarutoCodeCli/narutocode-osx-arm64-singlefile-r2r.tar.gz`

### NativeAOT 模式

- 如需验证 NativeAOT，可显式使用：
  - `$HOME/.dotnet/dotnet publish src/NarutoCodeCli/NarutoCodeCli.csproj -c Release -r osx-arm64 -p:PublishAot=true -p:SelfContained=true -o artifacts/publish/NarutoCodeCli/osx-arm64-aot`
- `PublishAot=true` 下当前可能仍会输出 `libe_sqlite3.dylib`、`.dSYM` 或 `*.pdb` 等文件，不作为默认最小单文件交付方式。
- NativeAOT 构建中出现 `IL2026`、`IL3050` 等 `System.Text.Json` 裁剪/AOT 警告时，不要直接忽略为长期方案；后续应优先改为 `JsonSerializerContext` / `JsonTypeInfo` 源生成模式。
- 持久化层在 NativeAOT 场景下优先使用 ADO.NET 直接操作 SQLite，避免引入 EF Core AOT compiled model/query precompilation 复杂度。
