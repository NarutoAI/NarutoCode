// using System.Net.Http.Headers;
// using System.Runtime.CompilerServices;
// using System.Text;
// using System.Text.Json;
// using System.Text.Json.Nodes;
// using System.Text.Json.Serialization;
// using Microsoft.Extensions.AI;
//
// namespace NarutoCode.Infrastructure.ChatClients;
//
// //forkf https://github.com/AIDotNet/OpenDeepWiki/blob/main/src/OpenDeepWiki/Agents/AgentFactory.cs
// public sealed class DeepSeekOpenAIChatClient : IChatClient
// {
//     private const string DefaultEndpoint = "https://api.deepseek.com/v1";
//     private const string DefaultPromptCacheKey = "narutocode";
//
//     private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
//     {
//         DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
//     };
//
//     private readonly string _model;
//     private readonly string _apiKey;
//     private readonly HttpClient _httpClient;
//     private readonly AiRequestOptions _options;
//     private readonly bool _disposeHttpClient;
//
//     public DeepSeekOpenAIChatClient(
//         string model,
//         string? endpoint,
//         string apiKey,
//         HttpClient httpClient,
//         AiRequestOptions options,
//         bool disposeHttpClient = false)
//     {
//         _model = string.IsNullOrWhiteSpace(model)
//             ? throw new ArgumentException("Model is required.", nameof(model))
//             : model;
//         _apiKey = string.IsNullOrWhiteSpace(apiKey)
//             ? throw new ArgumentException("API key is required.", nameof(apiKey))
//             : apiKey;
//         _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
//         _options = options ?? throw new ArgumentNullException(nameof(options));
//         _options.Endpoint = NormalizeEndpoint(endpoint);
//         _disposeHttpClient = disposeHttpClient;
//     }
//
//     public async Task<ChatResponse> GetResponseAsync(
//         IEnumerable<ChatMessage> messages,
//         ChatOptions? options = null,
//         CancellationToken cancellationToken = default)
//     {
//         using var request = CreateRequest(messages, options, stream: false);
//         using var response = await _httpClient.SendAsync(request, cancellationToken);
//         var content = await response.Content.ReadAsStringAsync(cancellationToken);
//         EnsureSuccessStatusCode(response, content);
//
//         var completion = JsonSerializer.Deserialize<ChatCompletionResponse>(content, JsonOptions)
//                          ?? throw new JsonException("DeepSeek returned an empty chat completion response.");
//         var choice = completion.Choices?.FirstOrDefault();
//         var message = choice?.Message ?? new ChatMessageDto();
//         var chatMessage = CreateAssistantMessage(message);
//
//         return new ChatResponse(chatMessage)
//         {
//             ResponseId = completion.Id,
//             ModelId = completion.Model ?? _model,
//             CreatedAt = ConvertCreatedAt(completion.Created),
//             FinishReason = MapFinishReason(choice?.FinishReason),
//             Usage = MapUsage(completion.Usage),
//             RawRepresentation = content
//         };
//     }
//
//     public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
//         IEnumerable<ChatMessage> messages,
//         ChatOptions? options = null,
//         [EnumeratorCancellation] CancellationToken cancellationToken = default)
//     {
//         using var request = CreateRequest(messages, options, stream: true);
//         using var response = await _httpClient.SendAsync(
//             request,
//             HttpCompletionOption.ResponseHeadersRead,
//             cancellationToken);
//         var errorContent = response.IsSuccessStatusCode
//             ? null
//             : await response.Content.ReadAsStringAsync(cancellationToken);
//         EnsureSuccessStatusCode(response, errorContent);
//
//         await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
//         var toolCalls = new Dictionary<int, StreamingToolCallBuilder>();
//         var emittedToolCalls = false;
//         string? responseId = null;
//         string? modelId = null;
//         DateTimeOffset? createdAt = null;
//         var messageId = Guid.NewGuid().ToString("N");
//
//         await foreach (var payload in ReadSseDataAsync(stream, cancellationToken))
//         {
//             if (IsDonePayload(payload))
//             {
//                 break;
//             }
//
//             ChatCompletionChunk chunk;
//             try
//             {
//                 chunk = JsonSerializer.Deserialize<ChatCompletionChunk>(payload, JsonOptions)
//                         ?? throw new JsonException("DeepSeek returned an empty stream chunk.");
//             }
//             catch (JsonException ex)
//             {
//                 throw new JsonException($"Invalid DeepSeek stream data: {TrimForError(payload)}", ex);
//             }
//
//             responseId ??= chunk.Id;
//             modelId = chunk.Model ?? modelId ?? _model;
//             createdAt ??= ConvertCreatedAt(chunk.Created);
//
//             if (chunk.Usage != null)
//             {
//                 yield return new ChatResponseUpdate
//                 {
//                     ResponseId = responseId,
//                     MessageId = messageId,
//                     ModelId = modelId,
//                     CreatedAt = createdAt,
//                     Contents = [new UsageContent(MapUsage(chunk.Usage)!)],
//                     RawRepresentation = payload
//                 };
//             }
//
//             if (chunk.Choices is not {Count: > 0})
//             {
//                 continue;
//             }
//
//             foreach (var choice in chunk.Choices)
//             {
//                 var delta = choice.Delta;
//                 if (!string.IsNullOrEmpty(delta?.Content))
//                 {
//                     yield return new ChatResponseUpdate(ChatRole.Assistant, delta.Content)
//                     {
//                         ResponseId = responseId,
//                         MessageId = messageId,
//                         ModelId = modelId,
//                         CreatedAt = createdAt,
//                         RawRepresentation = payload
//                     };
//                 }
//
//                 if (!string.IsNullOrEmpty(delta?.ReasoningContent))
//                 {
//                     var reasoningContent = new TextReasoningContent(delta.ReasoningContent)
//                     {
//                         AdditionalProperties = new AdditionalPropertiesDictionary
//                         {
//                             ["reasoning_content"] = delta.ReasoningContent
//                         }
//                     };
//                     var reasoningUpdate = new ChatResponseUpdate(ChatRole.Assistant, [reasoningContent])
//                     {
//                         ResponseId = responseId,
//                         MessageId = messageId,
//                         ModelId = modelId,
//                         CreatedAt = createdAt,
//                         RawRepresentation = payload,
//                         AdditionalProperties = new AdditionalPropertiesDictionary
//                         {
//                             ["reasoning_content"] = delta.ReasoningContent
//                         }
//                     };
//                     yield return reasoningUpdate;
//                 }
//
//                 AccumulateToolCalls(toolCalls, delta?.ToolCalls);
//
//                 var finishReason = MapFinishReason(choice.FinishReason);
//                 if (finishReason == ChatFinishReason.ToolCalls && toolCalls.Count > 0)
//                 {
//                     emittedToolCalls = true;
//                     yield return CreateToolCallsUpdate(
//                         toolCalls,
//                         responseId,
//                         messageId,
//                         modelId,
//                         createdAt,
//                         finishReason,
//                         payload);
//                 }
//                 else if (finishReason != null)
//                 {
//                     yield return new ChatResponseUpdate
//                     {
//                         Role = ChatRole.Assistant,
//                         ResponseId = responseId,
//                         MessageId = messageId,
//                         ModelId = modelId,
//                         CreatedAt = createdAt,
//                         FinishReason = finishReason,
//                         RawRepresentation = payload
//                     };
//                 }
//             }
//         }
//
//         if (!emittedToolCalls && toolCalls.Count > 0)
//         {
//             yield return CreateToolCallsUpdate(
//                 toolCalls,
//                 responseId,
//                 messageId,
//                 modelId ?? _model,
//                 createdAt,
//                 ChatFinishReason.ToolCalls,
//                 null);
//         }
//     }
//
//     public object? GetService(Type serviceType, object? serviceKey = null)
//     {
//         return serviceKey == null && serviceType.IsInstanceOfType(this) ? this : null;
//     }
//
//     public void Dispose()
//     {
//         if (_disposeHttpClient)
//         {
//             _httpClient.Dispose();
//         }
//     }
//
//     private HttpRequestMessage CreateRequest(
//         IEnumerable<ChatMessage> messages,
//         ChatOptions? options,
//         bool stream)
//     {
//         var requestMessages = BuildMessages(messages);
//         PrependInstructions(requestMessages, options?.Instructions);
//
//         var body = new JsonObject
//         {
//             ["model"] = options?.ModelId ?? _model,
//             ["stream"] = stream,
//             ["prompt_cache_key"] = ResolvePromptCacheKey(options),
//             ["messages"] = requestMessages
//         };
//
//         if (stream)
//         {
//             body["stream_options"] = new JsonObject
//             {
//                 ["include_usage"] = true
//             };
//         }
//
//         ApplyCommonOptions(body, options);
//         ApplyTools(body, options);
//         ApplyRequestOverrides(body, null, _options.ProviderRequestOverridesJson);
//         ApplyRequestOverrides(body, null, _options.ModelRequestOverridesJson);
//         ApplyThinkingConfig(body, options);
//
//         var request = new HttpRequestMessage(
//             HttpMethod.Post,
//             AppendEndpointPath(_options.Endpoint ?? DefaultEndpoint, "chat/completions"))
//         {
//             Content = new StringContent(body.ToJsonString(JsonOptions), Encoding.UTF8, "application/json")
//         };
//
//         request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
//         request.Headers.Accept.Add(
//             new MediaTypeWithQualityHeaderValue(stream ? "text/event-stream" : "application/json"));
//         ApplyRequestOverrides(body, request, _options.ProviderRequestOverridesJson);
//         ApplyRequestOverrides(body, request, _options.ModelRequestOverridesJson);
//         return request;
//     }
//
//     private static JsonArray BuildMessages(IEnumerable<ChatMessage> messages)
//     {
//         var array = new JsonArray();
//         foreach (var message in messages)
//         {
//             var functionResults = message.Contents.OfType<FunctionResultContent>().ToList();
//             if (functionResults.Count > 0)
//             {
//                 foreach (var result in functionResults)
//                 {
//                     array.Add(new JsonObject
//                     {
//                         ["role"] = "tool",
//                         ["tool_call_id"] = result.CallId,
//                         ["content"] = SerializeToolResult(result.Result)
//                     });
//                 }
//
//                 continue;
//             }
//
//             var functionCalls = message.Contents.OfType<FunctionCallContent>().ToList();
//             var role = ToOpenAIRole(message.Role);
//             var node = new JsonObject
//             {
//                 ["role"] = role
//             };
//
//             var text = GetText(message.Contents);
//             node["content"] = string.IsNullOrEmpty(text) && functionCalls.Count > 0 ? null : text;
//
//             var reasoningContent = GetReasoningContent(message);
//             if (role == "assistant" && !string.IsNullOrEmpty(reasoningContent))
//             {
//                 node["reasoning_content"] = reasoningContent;
//             }
//
//             if (functionCalls.Count > 0)
//             {
//                 var toolCalls = new JsonArray();
//                 foreach (var call in functionCalls)
//                 {
//                     toolCalls.Add(new JsonObject
//                     {
//                         ["id"] = call.CallId,
//                         ["type"] = "function",
//                         ["function"] = new JsonObject
//                         {
//                             ["name"] = call.Name,
//                             ["arguments"] = JsonSerializer.Serialize(call.Arguments, JsonOptions)
//                         }
//                     });
//                 }
//
//                 node["tool_calls"] = toolCalls;
//             }
//
//             array.Add(node);
//         }
//
//         return array;
//     }
//
//     private static void PrependInstructions(JsonArray messages, string? instructions)
//     {
//         if (string.IsNullOrWhiteSpace(instructions) || HasSystemMessage(messages))
//         {
//             return;
//         }
//
//         messages.Insert(0, new JsonObject
//         {
//             ["role"] = "system",
//             ["content"] = instructions
//         });
//     }
//
//     private static bool HasSystemMessage(JsonArray messages)
//     {
//         foreach (var message in messages)
//         {
//             if (message is JsonObject node &&
//                 node["role"]?.GetValue<string>().Equals("system", StringComparison.OrdinalIgnoreCase) == true)
//             {
//                 return true;
//             }
//         }
//
//         return false;
//     }
//
//     private static void ApplyCommonOptions(JsonObject body, ChatOptions? options)
//     {
//         if (options == null)
//         {
//             return;
//         }
//
//         if (options.MaxOutputTokens.HasValue)
//         {
//             body["max_tokens"] = options.MaxOutputTokens.Value;
//         }
//
//         if (options.Temperature.HasValue)
//         {
//             body["temperature"] = options.Temperature.Value;
//         }
//
//         if (options.TopP.HasValue)
//         {
//             body["top_p"] = options.TopP.Value;
//         }
//
//         if (options.FrequencyPenalty.HasValue)
//         {
//             body["frequency_penalty"] = options.FrequencyPenalty.Value;
//         }
//
//         if (options.PresencePenalty.HasValue)
//         {
//             body["presence_penalty"] = options.PresencePenalty.Value;
//         }
//
//         if (options.Seed.HasValue)
//         {
//             body["seed"] = options.Seed.Value;
//         }
//
//         if (options.StopSequences is {Count: > 0})
//         {
//             var stops = new JsonArray();
//             foreach (var stop in options.StopSequences)
//             {
//                 stops.Add(stop);
//             }
//
//             body["stop"] = stops;
//         }
//     }
//
//     private static void ApplyTools(JsonObject body, ChatOptions? options)
//     {
//         if (options?.Tools is not {Count: > 0})
//         {
//             return;
//         }
//
//         var tools = new JsonArray();
//         foreach (var tool in options.Tools)
//         {
//             if (tool is not AIFunctionDeclaration function)
//             {
//                 continue;
//             }
//
//             tools.Add(new JsonObject
//             {
//                 ["type"] = "function",
//                 ["function"] = new JsonObject
//                 {
//                     ["name"] = function.Name,
//                     ["description"] = function.Description ?? string.Empty,
//                     ["parameters"] = CloneJsonElement(function.JsonSchema) ?? new JsonObject
//                     {
//                         ["type"] = "object"
//                     }
//                 }
//             });
//         }
//
//         if (tools.Count == 0)
//         {
//             return;
//         }
//
//         body["tools"] = tools;
//         body["tool_choice"] = options.ToolMode switch
//         {
//             NoneChatToolMode => JsonValue.Create("none"),
//             RequiredChatToolMode required when !string.IsNullOrWhiteSpace(required.RequiredFunctionName) => new
//                 JsonObject
//                 {
//                     ["type"] = "function",
//                     ["function"] = new JsonObject
//                     {
//                         ["name"] = required.RequiredFunctionName
//                     }
//                 },
//             RequiredChatToolMode => JsonValue.Create("required"),
//             _ => JsonValue.Create("auto")
//         };
//     }
//
//     private void ApplyThinkingConfig(JsonObject body, ChatOptions? options)
//     {
//         if (!_options.SupportsThinking || string.IsNullOrWhiteSpace(_options.ThinkingConfigJson))
//         {
//             return;
//         }
//
//         var enabled = ResolveThinkingEnabled(options);
//         if (enabled == null)
//         {
//             return;
//         }
//
//         JsonNode? parsed;
//         try
//         {
//             parsed = JsonNode.Parse(_options.ThinkingConfigJson);
//         }
//         catch (JsonException)
//         {
//             return;
//         }
//
//         if (parsed is not JsonObject config)
//         {
//             return;
//         }
//
//         var bodyParamName = enabled.Value ? "bodyParams" : "disabledBodyParams";
//         if (config[bodyParamName] is JsonObject bodyParams)
//         {
//             MergeJsonObject(body, bodyParams);
//         }
//
//         if (enabled.Value && config["forceTemperature"] is JsonValue forceTemperature)
//         {
//             body["temperature"] = forceTemperature.DeepClone();
//         }
//     }
//
//     private bool? ResolveThinkingEnabled(ChatOptions? options)
//     {
//         if (TryReadBoolean(options?.AdditionalProperties, "thinkingEnabled", out var thinkingEnabled) ||
//             TryReadBoolean(options?.AdditionalProperties, "enableThinking", out thinkingEnabled) ||
//             TryReadBoolean(options?.AdditionalProperties, "enable_thinking", out thinkingEnabled))
//         {
//             return thinkingEnabled;
//         }
//
//         return _options.ThinkingEnabled ?? true;
//     }
//
//     private string ResolvePromptCacheKey(ChatOptions? options)
//     {
//         if (TryReadString(options?.AdditionalProperties, "promptCacheKey", out var promptCacheKey) ||
//             TryReadString(options?.AdditionalProperties, "prompt_cache_key", out promptCacheKey))
//         {
//             return NormalizePromptCacheKey(promptCacheKey);
//         }
//
//         return NormalizePromptCacheKey(_options.PromptCacheKey);
//     }
//
//     private static string NormalizePromptCacheKey(string? promptCacheKey)
//     {
//         return string.IsNullOrWhiteSpace(promptCacheKey)
//             ? DefaultPromptCacheKey
//             : promptCacheKey.Trim();
//     }
//
//     private static void ApplyRequestOverrides(
//         JsonObject body,
//         HttpRequestMessage? request,
//         string? overridesJson)
//     {
//         if (string.IsNullOrWhiteSpace(overridesJson))
//         {
//             return;
//         }
//
//         JsonNode? parsed;
//         try
//         {
//             parsed = JsonNode.Parse(overridesJson);
//         }
//         catch (JsonException)
//         {
//             return;
//         }
//
//         if (parsed is not JsonObject overrides)
//         {
//             return;
//         }
//
//         if (request == null)
//         {
//             if (overrides["bodyParams"] is JsonObject bodyParams)
//             {
//                 MergeJsonObject(body, bodyParams);
//             }
//
//             if (overrides["body"] is JsonObject bodyOverride)
//             {
//                 MergeJsonObject(body, bodyOverride);
//             }
//
//             foreach (var pair in overrides)
//             {
//                 if (pair.Key.Equals("headers", StringComparison.OrdinalIgnoreCase) ||
//                     pair.Key.Equals("bodyParams", StringComparison.OrdinalIgnoreCase) ||
//                     pair.Key.Equals("body", StringComparison.OrdinalIgnoreCase))
//                 {
//                     continue;
//                 }
//
//                 body[pair.Key] = pair.Value?.DeepClone();
//             }
//
//             return;
//         }
//
//         if (overrides["headers"] is not JsonObject headers)
//         {
//             return;
//         }
//
//         foreach (var header in headers)
//         {
//             if (header.Value == null)
//             {
//                 continue;
//             }
//
//             var value = header.Value.GetValueKind() == JsonValueKind.String
//                 ? header.Value.GetValue<string>()
//                 : header.Value.ToJsonString(JsonOptions);
//             if (string.IsNullOrWhiteSpace(value))
//             {
//                 continue;
//             }
//
//             if (header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
//             {
//                 request.Content?.Headers.Remove(header.Key);
//                 request.Content?.Headers.TryAddWithoutValidation(header.Key, value);
//             }
//             else
//             {
//                 request.Headers.Remove(header.Key);
//                 request.Headers.TryAddWithoutValidation(header.Key, value);
//             }
//         }
//     }
//
//     private static ChatMessage CreateAssistantMessage(ChatMessageDto message)
//     {
//         var contents = new List<AIContent>();
//         if (!string.IsNullOrEmpty(message.ReasoningContent))
//         {
//             contents.Add(new TextReasoningContent(message.ReasoningContent)
//             {
//                 AdditionalProperties = new AdditionalPropertiesDictionary
//                 {
//                     ["reasoning_content"] = message.ReasoningContent
//                 }
//             });
//         }
//
//         if (!string.IsNullOrEmpty(message.Content))
//         {
//             contents.Add(new TextContent(message.Content));
//         }
//
//         if (message.ToolCalls is {Count: > 0})
//         {
//             foreach (var toolCall in message.ToolCalls)
//             {
//                 var function = toolCall.Function;
//                 if (string.IsNullOrWhiteSpace(toolCall.Id) || string.IsNullOrWhiteSpace(function?.Name))
//                 {
//                     continue;
//                 }
//
//                 contents.Add(new FunctionCallContent(
//                     toolCall.Id,
//                     function.Name,
//                     ParseArguments(function.Arguments)));
//             }
//         }
//
//         return new ChatMessage(ChatRole.Assistant, contents);
//     }
//
//     private static ChatResponseUpdate CreateToolCallsUpdate(
//         Dictionary<int, StreamingToolCallBuilder> toolCalls,
//         string? responseId,
//         string messageId,
//         string? modelId,
//         DateTimeOffset? createdAt,
//         ChatFinishReason? finishReason,
//         string? rawPayload)
//     {
//         var contents = toolCalls
//             .OrderBy(pair => pair.Key)
//             .Select(pair => pair.Value)
//             .Where(call => !string.IsNullOrWhiteSpace(call.Id) && !string.IsNullOrWhiteSpace(call.Name))
//             .Select(call => (AIContent) new FunctionCallContent(
//                 call.Id!,
//                 call.Name!,
//                 ParseArguments(call.Arguments.ToString())))
//             .ToList();
//
//         return new ChatResponseUpdate(ChatRole.Assistant, contents)
//         {
//             ResponseId = responseId,
//             MessageId = messageId,
//             ModelId = modelId,
//             CreatedAt = createdAt,
//             FinishReason = finishReason,
//             RawRepresentation = rawPayload
//         };
//     }
//
//     private static void AccumulateToolCalls(
//         Dictionary<int, StreamingToolCallBuilder> toolCalls,
//         IReadOnlyList<ToolCallDeltaDto>? deltas)
//     {
//         if (deltas is not {Count: > 0})
//         {
//             return;
//         }
//
//         for (var position = 0; position < deltas.Count; position++)
//         {
//             var delta = deltas[position];
//             var index = delta.Index ?? position;
//             if (!toolCalls.TryGetValue(index, out var builder))
//             {
//                 builder = new StreamingToolCallBuilder();
//                 toolCalls[index] = builder;
//             }
//
//             if (!string.IsNullOrWhiteSpace(delta.Id))
//             {
//                 builder.Id = delta.Id;
//             }
//
//             if (!string.IsNullOrWhiteSpace(delta.Function?.Name))
//             {
//                 builder.Name = delta.Function.Name;
//             }
//
//             if (!string.IsNullOrEmpty(delta.Function?.Arguments))
//             {
//                 builder.Arguments.Append(delta.Function.Arguments);
//             }
//         }
//     }
//
//     private static async IAsyncEnumerable<string> ReadSseDataAsync(
//         Stream stream,
//         [EnumeratorCancellation] CancellationToken cancellationToken)
//     {
//         using var reader = new StreamReader(stream, Encoding.UTF8);
//         var data = new StringBuilder();
//         while (await reader.ReadLineAsync(cancellationToken) is { } line)
//         {
//             if (line.Length == 0)
//             {
//                 if (data.Length > 0)
//                 {
//                     yield return data.ToString();
//                     data.Clear();
//                 }
//
//                 continue;
//             }
//
//             if (line.StartsWith(':'))
//             {
//                 continue;
//             }
//
//             if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
//             {
//                 continue;
//             }
//
//             if (data.Length > 0)
//             {
//                 data.Append('\n');
//             }
//
//             data.Append(line[5..].TrimStart());
//         }
//
//         if (data.Length > 0)
//         {
//             yield return data.ToString();
//         }
//     }
//
//     private static UsageDetails? MapUsage(UsageDto? usage)
//     {
//         if (usage == null)
//         {
//             return null;
//         }
//
//         return new UsageDetails
//         {
//             InputTokenCount = usage.PromptTokens,
//             OutputTokenCount = usage.CompletionTokens,
//             TotalTokenCount = usage.TotalTokens,
//             CachedInputTokenCount = usage.PromptTokensDetails?.CachedTokens,
//             ReasoningTokenCount = usage.CompletionTokensDetails?.ReasoningTokens
//         };
//     }
//
//     private static ChatFinishReason? MapFinishReason(string? finishReason)
//     {
//         return finishReason?.Trim().ToLowerInvariant() switch
//         {
//             "stop" => ChatFinishReason.Stop,
//             "length" => ChatFinishReason.Length,
//             "tool_calls" or "function_call" => ChatFinishReason.ToolCalls,
//             "content_filter" => ChatFinishReason.ContentFilter,
//             _ => null
//         };
//     }
//
//     private static IDictionary<string, object?> ParseArguments(string? arguments)
//     {
//         if (string.IsNullOrWhiteSpace(arguments))
//         {
//             return new Dictionary<string, object?>();
//         }
//
//         try
//         {
//             using var document = JsonDocument.Parse(arguments);
//             if (document.RootElement.ValueKind != JsonValueKind.Object)
//             {
//                 return new Dictionary<string, object?>
//                 {
//                     ["value"] = ConvertJsonElement(document.RootElement)
//                 };
//             }
//
//             return document.RootElement.EnumerateObject()
//                 .ToDictionary(
//                     property => property.Name,
//                     property => ConvertJsonElement(property.Value));
//         }
//         catch (JsonException)
//         {
//             return new Dictionary<string, object?>
//             {
//                 ["value"] = arguments
//             };
//         }
//     }
//
//     private static object? ConvertJsonElement(JsonElement element)
//     {
//         return element.ValueKind switch
//         {
//             JsonValueKind.Object => element.EnumerateObject()
//                 .ToDictionary(property => property.Name, property => ConvertJsonElement(property.Value)),
//             JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonElement).ToList(),
//             JsonValueKind.String => element.GetString(),
//             JsonValueKind.Number when element.TryGetInt64(out var longValue) => longValue,
//             JsonValueKind.Number when element.TryGetDouble(out var doubleValue) => doubleValue,
//             JsonValueKind.True => true,
//             JsonValueKind.False => false,
//             JsonValueKind.Null => null,
//             _ => element.GetRawText()
//         };
//     }
//
//     private static string SerializeToolResult(object? result)
//     {
//         return result switch
//         {
//             null => "null",
//             string text => text,
//             JsonElement element => element.GetRawText(),
//             JsonNode node => node.ToJsonString(JsonOptions),
//             _ => JsonSerializer.Serialize(result, JsonOptions)
//         };
//     }
//
//     private static string GetText(IEnumerable<AIContent> contents)
//     {
//         return string.Concat(contents.OfType<TextContent>().Select(content => content.Text));
//     }
//
//     private static string GetReasoningContent(ChatMessage message)
//     {
//         var reasoning = string.Concat(message.Contents
//             .OfType<TextReasoningContent>()
//             .Select(content => content.Text));
//         if (!string.IsNullOrEmpty(reasoning))
//         {
//             return reasoning;
//         }
//
//         if (TryReadString(message.AdditionalProperties, "reasoning_content", out var rawReasoning))
//         {
//             return rawReasoning;
//         }
//
//         return string.Empty;
//     }
//
//     private static JsonNode? CloneJsonElement(JsonElement element)
//     {
//         return element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
//             ? null
//             : JsonNode.Parse(element.GetRawText());
//     }
//
//     private static void MergeJsonObject(JsonObject target, JsonObject source)
//     {
//         foreach (var pair in source)
//         {
//             target[pair.Key] = pair.Value?.DeepClone();
//         }
//     }
//
//     private static bool TryReadBoolean(
//         AdditionalPropertiesDictionary? properties,
//         string key,
//         out bool value)
//     {
//         value = false;
//         if (properties == null || !properties.TryGetValue(key, out var raw))
//         {
//             return false;
//         }
//
//         switch (raw)
//         {
//             case bool boolValue:
//                 value = boolValue;
//                 return true;
//             case string text when bool.TryParse(text, out var parsed):
//                 value = parsed;
//                 return true;
//             default:
//                 return false;
//         }
//     }
//
//     private static bool TryReadString(
//         AdditionalPropertiesDictionary? properties,
//         string key,
//         out string value)
//     {
//         value = string.Empty;
//         if (properties == null || !properties.TryGetValue(key, out var raw))
//         {
//             return false;
//         }
//
//         switch (raw)
//         {
//             case string text:
//                 value = text;
//                 return true;
//             case JsonElement element when element.ValueKind == JsonValueKind.String:
//                 value = element.GetString() ?? string.Empty;
//                 return true;
//             case JsonNode node when node.GetValueKind() == JsonValueKind.String:
//                 value = node.GetValue<string>();
//                 return true;
//             default:
//                 return false;
//         }
//     }
//
//     private static DateTimeOffset? ConvertCreatedAt(long? unixSeconds)
//     {
//         return unixSeconds.HasValue ? DateTimeOffset.FromUnixTimeSeconds(unixSeconds.Value) : null;
//     }
//
//     private static string ToOpenAIRole(ChatRole role)
//     {
//         if (role == ChatRole.System)
//         {
//             return "system";
//         }
//
//         if (role == ChatRole.Assistant)
//         {
//             return "assistant";
//         }
//
//         if (role == ChatRole.Tool)
//         {
//             return "tool";
//         }
//
//         return "user";
//     }
//
//     private static string NormalizeEndpoint(string? endpoint)
//     {
//         return string.IsNullOrWhiteSpace(endpoint)
//             ? DefaultEndpoint
//             : endpoint.TrimEnd('/');
//     }
//
//     private static string AppendEndpointPath(string baseUrl, string path)
//     {
//         var trimmed = baseUrl.TrimEnd('/');
//         if (trimmed.EndsWith($"/{path}", StringComparison.OrdinalIgnoreCase))
//         {
//             return trimmed;
//         }
//
//         return $"{trimmed}/{path}";
//     }
//
//     private static bool IsDonePayload(string payload)
//     {
//         return payload.Equals("[DONE]", StringComparison.OrdinalIgnoreCase) ||
//                payload.Equals("DONE", StringComparison.OrdinalIgnoreCase);
//     }
//
//     private static void EnsureSuccessStatusCode(HttpResponseMessage response, string? content)
//     {
//         if (response.IsSuccessStatusCode)
//         {
//             return;
//         }
//
//         var message = string.IsNullOrWhiteSpace(content)
//             ? response.ReasonPhrase
//             : TrimForError(content);
//         throw new HttpRequestException(
//             $"DeepSeek OpenAI-compatible request failed with HTTP {(int) response.StatusCode}: {message}",
//             null,
//             response.StatusCode);
//     }
//
//     private static string TrimForError(string text)
//     {
//         var trimmed = text.Trim();
//         return trimmed.Length <= 600 ? trimmed : trimmed[..600];
//     }
//
//     private sealed class StreamingToolCallBuilder
//     {
//         public string? Id { get; set; }
//         public string? Name { get; set; }
//         public StringBuilder Arguments { get; } = new();
//     }
//
//     private sealed class ChatCompletionResponse
//     {
//         [JsonPropertyName("id")] public string? Id { get; init; }
//
//         [JsonPropertyName("model")] public string? Model { get; init; }
//
//         [JsonPropertyName("created")] public long? Created { get; init; }
//
//         [JsonPropertyName("choices")] public List<ChatCompletionChoice>? Choices { get; init; }
//
//         [JsonPropertyName("usage")] public UsageDto? Usage { get; init; }
//     }
//
//     private sealed class ChatCompletionChunk
//     {
//         [JsonPropertyName("id")] public string? Id { get; init; }
//
//         [JsonPropertyName("model")] public string? Model { get; init; }
//
//         [JsonPropertyName("created")] public long? Created { get; init; }
//
//         [JsonPropertyName("choices")] public List<ChatCompletionChunkChoice>? Choices { get; init; }
//
//         [JsonPropertyName("usage")] public UsageDto? Usage { get; init; }
//     }
//
//     private sealed class ChatCompletionChoice
//     {
//         [JsonPropertyName("message")] public ChatMessageDto? Message { get; init; }
//
//         [JsonPropertyName("finish_reason")] public string? FinishReason { get; init; }
//     }
//
//     private sealed class ChatCompletionChunkChoice
//     {
//         [JsonPropertyName("delta")] public ChatDeltaDto? Delta { get; init; }
//
//         [JsonPropertyName("finish_reason")] public string? FinishReason { get; init; }
//     }
//
//     private sealed class ChatMessageDto
//     {
//         [JsonPropertyName("content")] public string? Content { get; init; }
//
//         [JsonPropertyName("reasoning_content")]
//         public string? ReasoningContent { get; init; }
//
//         [JsonPropertyName("tool_calls")] public List<ToolCallDto>? ToolCalls { get; init; }
//     }
//
//     private sealed class ChatDeltaDto
//     {
//         [JsonPropertyName("content")] public string? Content { get; init; }
//
//         [JsonPropertyName("reasoning_content")]
//         public string? ReasoningContent { get; init; }
//
//         [JsonPropertyName("tool_calls")] public List<ToolCallDeltaDto>? ToolCalls { get; init; }
//     }
//
//     private sealed class ToolCallDto
//     {
//         [JsonPropertyName("id")] public string? Id { get; init; }
//
//         [JsonPropertyName("function")] public ToolFunctionDto? Function { get; init; }
//     }
//
//     private sealed class ToolCallDeltaDto
//     {
//         [JsonPropertyName("index")] public int? Index { get; init; }
//
//         [JsonPropertyName("id")] public string? Id { get; init; }
//
//         [JsonPropertyName("function")] public ToolFunctionDto? Function { get; init; }
//     }
//
//     private sealed class ToolFunctionDto
//     {
//         [JsonPropertyName("name")] public string? Name { get; init; }
//
//         [JsonPropertyName("arguments")] public string? Arguments { get; init; }
//     }
//
//     private sealed class UsageDto
//     {
//         [JsonPropertyName("prompt_tokens")] public int? PromptTokens { get; init; }
//
//         [JsonPropertyName("completion_tokens")]
//         public int? CompletionTokens { get; init; }
//
//         [JsonPropertyName("total_tokens")] public int? TotalTokens { get; init; }
//
//         [JsonPropertyName("prompt_tokens_details")]
//         public PromptTokensDetailsDto? PromptTokensDetails { get; init; }
//
//         [JsonPropertyName("completion_tokens_details")]
//         public CompletionTokensDetailsDto? CompletionTokensDetails { get; init; }
//     }
//
//     private sealed class PromptTokensDetailsDto
//     {
//         [JsonPropertyName("cached_tokens")] public int? CachedTokens { get; init; }
//     }
//
//     private sealed class CompletionTokensDetailsDto
//     {
//         [JsonPropertyName("reasoning_tokens")] public int? ReasoningTokens { get; init; }
//     }
// }