# EnvVar API

命名空间：`Lunarium.Common` ｜ 类型：`public sealed record EnvVar`

环境变量的统一包装：读取、写入、删除，以及基于 `IParsable<T>` 的类型化解析。`Name` 与 `Value` 在构造时都会裁剪首尾空白。

## 构造与属性

```csharp
public EnvVar(string name, string value);

public string Name { get; }
public string Value { get; }
```

- `name` / `value` 为 `null` → `ArgumentNullException`。
- `name` / `value` 为空或仅空白 → `ArgumentException`。
- 构造成功后不会保留首尾空白。

## 读取

| 成员 | 签名 | 行为 |
| --- | --- | --- |
| `Get` | `public static EnvVar? Get(string envVarName)` | 未设置或值为空白 → `null` |
| `GetAny` | `public static EnvVar? GetAny(params string[] candidateNames)` | 按候选名顺序返回第一个非空白值；全部未命中 → `null` |
| `GetRequired` | `public static EnvVar GetRequired(string envVarName)` | 未设置或值为空白 → `InvalidOperationException` |
| `GetAnyRequired` | `public static EnvVar GetAnyRequired(params string[] candidateNames)` | 全部候选都未命中 → `InvalidOperationException` |

读取语义：

- “未设置”和“值为空白”无法区分，两者都按未设置处理。
- `GetAny` / `GetAnyRequired` 的候选名按参数顺序从高到低；数组不能为空，扫描到的候选名不能为 `null`/空白（否则抛 `ArgumentException` / `ArgumentNullException`）。
- 变量名大小写敏感性随平台而异（Windows 通常不区分，Linux 区分）。

## 写入与删除

| 成员 | 签名 | 行为 |
| --- | --- | --- |
| `Set` | `public static EnvVar Set(string name, string value)` | 裁剪后写入进程环境，返回新实例 |
| `Set` | `public EnvVar Set()` | 把当前 `Name` / `Value` 写回进程环境，返回 `this` |
| `Unset` | `public static void Unset(string name)` | 裁剪名称后删除变量 |
| `Unset` | `public void Unset()` | 删除当前实例对应的变量 |

- `Set` 拒绝空白值；通过本类型写不出空字符串变量。要清空只能 `Unset`（彻底删除）。
- `Set` 返回的实例与写入内容一致（名称、值均已裁剪）；`Set()` 便于链式调用。
- 名称被平台拒绝（例如包含 `=`）时，底层 `Environment.SetEnvironmentVariable` 的异常会透传。

## 类型化解析

```csharp
public T? As<T>(T? defaultValue = default) where T : IParsable<T>;

public T RequiredAs<T>() where T : IParsable<T>;

public bool AsBoolean(string trueName, string falseName, bool defaultValue = false);

public bool RequiredAsBoolean(string trueName, string falseName);

public TEnum AsEnum<TEnum>(
    TEnum defaultValue = default,
    bool ignoreCase = true,
    bool requireDefined = false)
    where TEnum : struct, Enum;

public TEnum RequiredAsEnum<TEnum>(
    bool ignoreCase = true,
    bool requireDefined = true)
    where TEnum : struct, Enum;
```

语义：

- `As<T>` / `RequiredAs<T>` 使用 `IParsable<T>`，固定 `CultureInfo.InvariantCulture`。
- `T` 为 `bool` 时使用宽松字面量：`true`/`false`、`1`/`yes`/`on`、`0`/`no`/`off`，大小写不敏感。
- 需要自定义真/假字面量时用 `AsBoolean` / `RequiredAsBoolean`；它们仍然先接受标准 `true`/`false`。
- 枚举不实现 `IParsable<T>`，必须用 `AsEnum` / `RequiredAsEnum`。
- **默认值不对称**：`AsEnum` 默认 `requireDefined: false`，`"99"` 这类可解析但未定义的纯数字值会被接受；`RequiredAsEnum` 默认 `requireDefined: true`，会拒绝未定义值。
- `As<T>` / `AsEnum` / `AsBoolean` 解析失败时返回 `defaultValue`；`RequiredAs*` 失败时抛 `InvalidOperationException`。

## 输出与相等性

- `override string ToString()` 只返回 `Name`，不返回值，避免日志或异常信息泄露密钥。
- 刻意不提供 `implicit operator string`，避免 `"" + envVar` 这类写法绕过脱敏；取值请用 `.Value`。
- `EnvVar` 是 `record`，相等性按 `Name` 与 `Value` 比较。

## 异常速查

| 操作 | 异常 |
| --- | --- |
| 构造、`Get`、`GetAny`、`Set`、`Unset` | `ArgumentNullException` / `ArgumentException`（名称或值为空/空白） |
| `GetRequired`、`GetAnyRequired` | 上述参数异常 + `InvalidOperationException`（变量缺失或为空白） |
| `RequiredAs<T>`、`RequiredAsBoolean`、`RequiredAsEnum<TEnum>` | `InvalidOperationException`（无法解析/未定义） |
| `As<T>`、`AsBoolean`、`AsEnum<TEnum>` | 无主动抛出的解析异常；失败返回默认值 |

## 示例

```csharp
using Lunarium.Common;

string? port = EnvVar.Get("PORT")?.Value;

int timeout = EnvVar.GetRequired("TIMEOUT").As(30);
bool enabled = EnvVar.GetRequired("FEATURE_X").As<bool>();

DayOfWeek day = EnvVar.GetRequired("REPORT_DAY").AsEnum(DayOfWeek.Monday);
DayOfWeek safeDay = EnvVar.GetRequired("REPORT_DAY").RequiredAsEnum<DayOfWeek>();

EnvVar.Set("MODE", "production");
EnvVar.GetRequired("MODE").Unset();
```

## 线程安全 / AOT

- 只是 `Environment` 的薄包装，不引入额外锁；并发写同一变量仍是进程环境层面的竞争。
- 解析走 `IParsable<T>` / `Enum.TryParse`，不使用反射序列化或动态代码，AOT 安全。
- 代理/URL 场景见 [net.md](net.md)；更完整的环境变量说明见[使用指南](../README.md)。

可运行示例：[EnvExample.cs](../examples/EnvExample.cs)。
