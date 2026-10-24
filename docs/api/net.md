# Net API

命名空间：`Lunarium.Common.Net`

两个不可变记录：`ProxyUrl` 表示代理配置，`EnvUrl` 表示普通环境 URL。二者都只保存字符串，并提供 `ToUri()`；`ProxyUrl` 额外提供 `ToWebProxy()`。

## ProxyUrl

```csharp
public sealed record ProxyUrl
{
    public ProxyUrl(string url);

    public string Value { get; }

    public static ProxyUrl? FromEnvironment(string? envVarName = null);

    public IWebProxy ToWebProxy();
    public Uri ToUri();

    public override string ToString();
    public static implicit operator string?(ProxyUrl? proxyUrl);
}
```

| 成员 | 行为 |
| --- | --- |
| `ProxyUrl(string url)` | 只校验非空白；不裁剪、不校验 URI 格式 |
| `Value` | 保存的代理 URL 字符串 |
| `FromEnvironment()` | 依次读取 `http_proxy` → `HTTP_PROXY`；未设置/空白 → `null` |
| `FromEnvironment("MY_PROXY")` | 读取指定变量；未设置/空白 → `null`；名称为空白 → `ArgumentException`（传 `null` 表示使用默认变量名） |
| `ToWebProxy()` | 返回 `new WebProxy(Value)` |
| `ToUri()` | 返回 `new Uri(Value)`；格式非法 → `UriFormatException` |
| `ToString()` | 返回 `Value` |
| 隐式 `string?` | 返回 `Value`（`null` 实例转为 `null`） |

`FromEnvironment` 的值经 `EnvVar` 读取，因此会裁剪首尾空白；直接 `new` 不会。默认变量名小写优先：`http_proxy` 有效时不会再读 `HTTP_PROXY`。

## EnvUrl

```csharp
public sealed record EnvUrl
{
    public EnvUrl(string url);

    public string Value { get; }

    public static EnvUrl? FromEnvironment(string envVarName);

    public Uri ToUri();

    public override string ToString();
    public static implicit operator string?(EnvUrl? envUrl);
}
```

| 成员 | 行为 |
| --- | --- |
| `EnvUrl(string url)` | 只校验非空白；不裁剪、不校验 URI 格式 |
| `Value` | 保存的 URL 字符串 |
| `FromEnvironment("API_BASE_URL")` | 必须显式指定变量名；未设置/空白 → `null`；名称 null/空白 → 参数异常 |
| `ToUri()` | 返回 `new Uri(Value)`；格式非法 → `UriFormatException` |
| `ToString()` | 返回 `Value` |
| 隐式 `string?` | 返回 `Value`（`null` 实例转为 `null`） |

`EnvUrl` 没有默认变量名，也没有 `ToWebProxy()`；它只表示“环境里的普通 URL”。

## 两者对比

| | `ProxyUrl` | `EnvUrl` |
| --- | --- | --- |
| 语义 | 代理配置 | 任意环境 URL |
| 默认环境变量 | `http_proxy`、`HTTP_PROXY` | 无，必须显式指定 |
| 转 `IWebProxy` | 有 | 无 |
| 转 `Uri` | 有 | 有 |
| 隐式 `string` | 有 | 有 |

## 示例

```csharp
using System.Net;
using Lunarium.Common.Net;

ProxyUrl? proxy = ProxyUrl.FromEnvironment();
IWebProxy? webProxy = proxy?.ToWebProxy();

ProxyUrl? customProxy = ProxyUrl.FromEnvironment("MY_PROXY");
Uri? proxyUri = customProxy?.ToUri();

EnvUrl? apiBase = EnvUrl.FromEnvironment("API_BASE_URL");
Uri? apiUri = apiBase?.ToUri();
```

需要“缺失就报错”时，可以先取 `EnvVar.GetRequired("API_BASE_URL").Value`，再构造 `EnvUrl`；或者在使用处对 `null` 做显式处理。

## 异常速查

| 操作 | 异常 |
| --- | --- |
| 构造函数 | `ArgumentNullException` / `ArgumentException`（URL 为 null/空白） |
| `ProxyUrl.FromEnvironment` 名称为空白 | `ArgumentException` |
| `EnvUrl.FromEnvironment` 名称为 null/空白 | `ArgumentNullException` / `ArgumentException` |
| `ToUri()` | `UriFormatException`（URL 不是有效 URI） |
| `ToWebProxy()` | 无主动文档化异常；底层 `WebProxy` 行为为准 |

## 线程安全 / AOT

- 两个类型都是只含只读 `Value` 的 `sealed record`，可安全共享。
- 无反射、无动态代码；`Uri` / `WebProxy` 均为 BCL 类型，AOT 安全。
- 更完整的代理/URL 选型见[使用指南](../README.md)。

可运行示例：[EnvExample.cs](../examples/EnvExample.cs)。
