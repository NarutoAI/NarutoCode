# JSON 序列化规范

所有 JSON 序列化必须使用 `JsonSerializerContext` 源生成器，禁用运行期反射路径（NativeAOT/裁剪不兼容）。

## 1. 定义上下文

```csharp
using System.Text.Json.Serialization;

namespace YourApp.Infrastructure.JsonSerializerContexts;

[JsonSourceGenerationOptions(
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(MyDto))]
[JsonSerializable(typeof(List<MyDto>))]
internal sealed partial class MyDomainJsonSerializerContext : JsonSerializerContext
{
}
```

要点：
- `internal sealed partial class`，继承 `JsonSerializerContext`。
- 命名后缀固定 `JsonSerializerContext`，文件同名。
- 按领域切分（配置、AI 内容、用户交互、Web API 等），新增类型就近归入已有上下文，避免一个大上下文装所有类型。
- 跨项目使用时另加 `public static` 门面类，不直接暴露上下文本体。

## 2. 注册类型

每个根类型逐个加 `[JsonSerializable(typeof(T))]`，泛型接口形态要单独注册（与具体类型是不同的 `TypeInfo`）：

```csharp
[JsonSerializable(typeof(MyDto))]
[JsonSerializable(typeof(MyDto[]))]
[JsonSerializable(typeof(List<MyDto>))]
[JsonSerializable(typeof(IReadOnlyList<MyDto>))]
[JsonSerializable(typeof(Dictionary<string, MyDto>))]
[JsonSerializable(typeof(IDictionary<string, MyDto>))]
```

## 3. 调用

必须传 `TypeInfo`：

```csharp
// 禁止
JsonSerializer.Serialize(value);
JsonSerializer.Serialize(value, new JsonSerializerOptions());
JsonSerializer.Serialize(value, Ctx.Default.Options);
JsonSerializer.Deserialize<MyDto>(json);

// 正确
JsonSerializer.Serialize(value, MyCtx.Default.MyDto);
JsonSerializer.Deserialize(json, MyCtx.Default.MyDto);
await JsonSerializer.SerializeAsync(stream, value, MyCtx.Default.MyDto, ct);
```

漏注册的 DTO 编译能过、运行期抛 `InvalidOperationException: could not find TypeInfo`。

## 4. 工具方法

上下文类内部加 `internal static` 包装，集中空值/异常处理：

```csharp
internal static string SerializeMyDto(MyDto value) => JsonSerializer.Serialize(value, Default.MyDto);
internal static MyDto? DeserializeMyDto(string json) =>
    string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize(json, Default.MyDto);
```

工具方法不要返回 `null`（除非调用方需要），失败/空值返回空集合或默认值。

## 5. 派生上下文（临时覆盖选项）

不要 `new JsonSerializerOptions` 然后调 `Default.<Type>`。正确做法是从源上下文 `Options` 派生再 `new` 上下文：

```csharp
var customOptions = new JsonSerializerOptions(MyCtx.Default.Options)
{
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower
};
var customCtx = new MyCtx(customOptions);
JsonSerializer.Serialize(value, customCtx.MyDto);
```

## 6. 验证

构建时关注 `IL2026` / `IL3050` 警告：出现即代表某条路径回退到反射。运行期报 `could not find TypeInfo for X` 即漏注册 `X`。
