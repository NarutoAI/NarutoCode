# 日志最佳实践

框架推荐使用 .NET **编译时日志源生成器（LoggerMessage Source Generator）** 来编写日志，避免运行时的装箱和字符串格式化开销。

## 核心模式

在项目中创建一个 `static partial class Log`，使用 `[LoggerMessage]` 特性定义日志方法：

```csharp
using Microsoft.Extensions.Logging;


internal static partial class Log
{
    [LoggerMessage(
        EventId = 0,
        Level = LogLevel.Warning,
        Message = "test: {Duration} : {CommandText}")]
    public static partial void LogWrite(ILogger logger, double duration, string commandText);
}
```

## 使用方式

在需要记录日志的类中注入 `ILogger<T>`，然后调用 `Log` 类的静态方法：

```csharp
public class Test(ILogger<SqlExecInterceptor> logger)
{

    public  void LogWrite()
    {
       Log.LogWrite(_logger,1, "test文本");
    }

}
```

## 规范要点

### 1. 类定义规范

- 使用 `static partial class`，类名推荐为 `Log`
- 命名空间与使用方保持一致
- 访问级别选择 `internal` 

### 2. 方法定义规范

- 方法必须是 `static partial`
- 第一个参数始终为 `ILogger`
- 使用 `[LoggerMessage]` 特性配置：
  - `EventId`：每个方法分配唯一递增 ID
  - `Level`：使用 `LogLevel` 枚举（Trace/Debug/Information/Warning/Error/Critical）
  - `Message`：使用模板占位符 `{PropertyName}`，**不要**使用字符串插值 `$""`

### 3. 异常处理

- 日志方法包含 `Exception` 参数时，异常会自动作为日志的 `Exception` 属性记录
- 无需在 Message 模板中拼接异常信息，但可以包含异常的特定字段（如 `Message`）

### 4. 日志级别选择

| 级别 | 场景 |
|------|------|
| `LogLevel.Trace` | 详细的调试信息，仅在开发环境使用 |
| `LogLevel.Debug` | 调试信息 |
| `LogLevel.Information` | 一般业务信息 |
| `LogLevel.Warning` | 警告 |
| `LogLevel.Error` | 错误|
| `LogLevel.Critical` | 严重错误 |

## 与传统日志写法对比

### 不推荐：传统写法（有运行时开销）

```csharp
// 即使日志级别未启用，字符串格式化仍会执行
_logger.LogWarning($"test: {duration} {commandText}");

```

### 推荐：源生成器写法（编译时生成，零运行时开销）

```csharp
    [LoggerMessage(
        EventId = 0,
        Level = LogLevel.Warning,
        Message = "test: {Duration} : {CommandText}")]
    public static partial void LogWrite(ILogger logger, double duration, string commandText);
```

**优势：**
- 编译时生成实现代码，无反射开销
- 日志级别未启用时，跳过模板参数求值
- 类型安全，编译器检查参数类型与模板匹配
- 代码简洁，一个特性即可完成定义
