# NarutoCode 使用说明

NarutoCode 是一个在终端中使用的 Agent 编程工具。启动后，你可以直接用自然语言让它分析项目、修改文件、执行命令、排查问题或整理文档。

## 1. 准备配置文件

首次使用前，需要创建配置文件：

```text
~/.narutocode/config.json
```

示例配置：

```json
{
  "llms": [
    {
      "provider": "OpenAI",
      "protocol": "OpenAIChat",
      "address": "https://api.openai.com/v1",
      "apiKey": "YOUR_OPENAI_API_KEY",
      "model": "gpt-4.1",
      "maxContextWindowTokens": 128000
    },
    {
      "provider": "DeepSeek",
      "protocol": "OpenAIChat",
      "address": "https://api.deepseek.com",
      "apiKey": "YOUR_DEEPSEEK_API_KEY",
      "model": "deepseek-chat",
      "maxContextWindowTokens": 64000
    }
  ],
  "system": {
    "logLevel": "Error",
    "compactionThresholds": {
      "imageCompaction": 0.4,
      "toolEviction": 0.6,
      "summarization": 0.8
    }
  },
  "enableApproval": false,
  "maxTurnCount": 10
}
```

把 `apiKey`、`model` 和 `address` 替换成你自己的模型配置。`provider` 是模型供应商的唯一标识，后续可以通过 `/provider` 命令切换。

如果使用 Anthropic 协议，可以增加一个 `protocol` 为 `Anthropic` 的配置：

```json
{
  "provider": "Anthropic",
  "protocol": "Anthropic",
  "address": "https://api.anthropic.com",
  "apiKey": "YOUR_API_KEY",
  "model": "claude-sonnet-4-20250514",
  "maxContextWindowTokens": 200000
}
```

NarutoCode 会额外使用运行时设置文件保存当前默认模型：

```text
~/.narutocode/settings.json
```

如果首次启动时 `settings.json` 不存在，或里面没有有效的 `provider`，NarutoCode 会自动写入 `llms` 数组中的第一个 `provider`。

### 配置项说明

| 配置项 | 必填 | 说明 |
| --- | --- | --- |
| `llms` | 是 | LLM 模型配置数组，至少配置一个模型。 |
| `llms[].provider` | 是 | 模型厂商唯一标识，例如 `OpenAI`、`DeepSeek`、`Anthropic` 或自定义名称。 |
| `llms[].protocol` | 是 | 模型接入协议，支持 `OpenAIChat`、`OpenAIResponses`、`Anthropic`。 |
| `llms[].address` | 是 | 模型服务地址，必须是完整 URL。 |
| `llms[].apiKey` | 是 | 模型服务访问密钥。 |
| `llms[].model` | 是 | 要使用的模型名称。 |
| `llms[].maxContextWindowTokens` | 否 | 最大上下文窗口 Token 数。 |
| `system.logLevel` | 否 | 日志最小输出级别，支持 `Trace`、`Debug`、`Information`、`Warning`、`Error`、`Critical`；未配置或配置无效时默认 `Error`。 |
| `system.compactionThresholds.imageCompaction` | 否 | 图片压缩触发阈值（相对于上下文窗口的比例），默认 `0.4`。当 Token 使用率达到上下文窗口的 40% 时触发图片压缩。 |
| `system.compactionThresholds.toolEviction` | 否 | 工具结果压缩触发阈值（相对于上下文窗口的比例），默认 `0.6`。当 Token 使用率达到上下文窗口的 60% 时触发工具结果压缩。 |
| `system.compactionThresholds.summarization` | 否 | 摘要压缩触发阈值（相对于上下文窗口的比例），默认 `0.8`。当 Token 使用率达到上下文窗口的 80% 时触发摘要压缩。 |
| `enableApproval` | 否 | 是否开启 Shell 工具审批，默认 `false`。设置为 `true` 后，执行 Shell 工具前需要确认。 |
| `maxTurnCount` | 否 | 单次对话最大交互轮次，默认 `10`。 |

## 2. 启动工具

如果你拿到的是压缩包，先解压并进入目录：

```bash
tar -xzf narutocode-osx-arm64-aot.tar.gz
cd narutocode-osx-arm64-aot
```

在要操作的项目目录中运行：

```bash
./narutocode
```

也可以直接指定工作目录：

```bash
./narutocode /path/to/workspace
```

如果已经把 `narutocode` 放到了 PATH 中，也可以直接使用：

```bash
narutocode /path/to/workspace
```

启动后，NarutoCode 会以该目录作为当前工作区。后续让 Agent 查看、修改或执行的内容都会基于这个工作区。

## 3. 切换模型 Provider

如果 `config.json` 中配置了多个模型，可以在运行中使用 `/provider` 查看和切换当前模型。

查看当前 provider 和可选列表：

```text
/provider
```

切换到指定 provider：

```text
/provider DeepSeek
```

切换成功后，NarutoCode 会把当前 provider 写入：

```text
~/.narutocode/settings.json
```

下次启动时会优先使用这个 provider。如果 `settings.json` 没有有效 provider，则自动使用 `llms` 数组的第一个配置。

## 4. 配置推理强度

可以使用 `/effort` 查看和切换当前推理强度。

查看当前 effort 和可选列表：

```text
/effort
```

切换到指定 effort：

```text
/effort high
```

可选值为 `low`、`medium`、`high`、`xhigh`。切换成功后，NarutoCode 会把当前 effort 写入：

```text
~/.narutocode/settings.json
```

下次启动时会优先使用这个 effort。如果 `settings.json` 没有配置 effort，则默认使用 `medium`。

## 5. 输入任务

启动后可以直接输入自然语言任务，例如：

```text
帮我分析这个项目的目录结构
```

```text
帮我找出登录接口相关代码，并说明调用链路
```

```text
帮我修改 README，把新配置项补充进去
```

```text
运行测试并修复失败的问题
```

## 6. 图片输入

如果需要让 Agent 分析图片，可以使用 `/image`：

```text
/image ./docs/screenshot.png 请分析这张截图里的错误
```

支持的图片格式：

```text
png、jpg、jpeg、webp、gif
```

也可以一次传入多张图片：

```text
/image ./before.png ./after.png 对比这两张图的差异
```

## 6. 工具审批

默认情况下：

```json
"enableApproval": false
```

Shell 工具不需要额外审批。

如果希望执行 Shell 工具前先确认，可以改为：

```json
"enableApproval": true
```

开启后，当 Agent 请求执行 Shell 工具时，终端会提示审批：

```text
1 agree / 0 deny
```

输入：

- `1`：同意执行
- `0`：拒绝执行

## 7. 运行中继续输入

当 Agent 正在回复或执行任务时，你仍然可以继续输入下一条消息。NarutoCode 会把新输入加入队列，等当前任务结束后继续处理。

## 8. 取消和退出

- 取消当前操作：按 `Ctrl+C`
- 退出工具：输入 `exit` 或 `quit`

如果当前有正在运行的 Agent 操作，第一次 `Ctrl+C` 会优先取消当前操作。

## 9. 会话历史和本地数据

NarutoCode 会按工作目录保存会话历史。默认数据文件位置：

```text
~/.narutocode/data/data.db
```

下次在同一个工作目录启动时，会自动加载对应的历史会话。

## 10. 常见问题

### 提示配置文件不存在怎么办？

确认是否已经创建：

```text
~/.narutocode/config.json
```

并检查 JSON 格式是否正确。

### 切换模型后需要重启吗？

如果只是切换已配置的模型，不需要重启，直接使用 `/provider <provider>` 即可。

如果修改了 `config.json` 中的模型列表、地址、密钥或协议，建议重新启动 NarutoCode，让新配置生效。

### 为什么工具执行前没有审批？

检查配置中是否设置了：

```json
"enableApproval": true
```

未设置时默认关闭审批。

## 作者

- 作者：Naruto
- GitHub：<https://github.com/NarutoAI>

# 公众号
![](/doc/gzh.jpg)
