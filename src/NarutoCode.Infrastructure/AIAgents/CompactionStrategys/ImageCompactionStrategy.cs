using Microsoft.Agents.AI.Compaction;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace NarutoCode.Infrastructure.AIAgents.CompactionStrategys;

#pragma warning disable MAAI001
/// <summary>
/// 图片压缩
/// </summary>
public class ImageCompactionStrategy(
    CompactionTrigger trigger,
    int minimumPreservedGroups = ImageCompactionStrategy.DefaultMinimumPreserved,
    CompactionTrigger? target = null)
    : CompactionStrategy(trigger, target)
#pragma warning restore MAAI001
{
    /// <summary>
    /// 默认需要保留的最近非系统消息组的最小数量。
    /// </summary>
    private const int DefaultMinimumPreserved = 6;

    /// <summary>
    /// 设置保留的数量
    /// </summary>
    private int MinimumPreservedGroups { get; } = EnsureNonNegative(minimumPreservedGroups);

#pragma warning disable MAAI001


    protected override ValueTask<bool> CompactCoreAsync(CompactionMessageIndex index, ILogger logger,
        CancellationToken cancellationToken)
    {
        //获取用户的消息
        List<int> nonSystemIncludedIndices = [];
        for (var i = 0; i < index.Groups.Count; i++)
        {
            var group = index.Groups[i];
            if (group is {IsExcluded: false, Kind: CompactionGroupKind.User})
            {
                nonSystemIncludedIndices.Add(i);
            }
        }

        //设置要处理的消息组下标
        var processCount = EnsureNonNegative(nonSystemIncludedIndices.Count - this.MinimumPreservedGroups);
        List<int> protectedGroupIndices = [];
        for (var i = 0; i < processCount; i++)
        {
            protectedGroupIndices.Add(nonSystemIncludedIndices[i]);
        }

        if (protectedGroupIndices is not {Count: > 0})
        {
            return ValueTask.FromResult(false);
        }

        var compacted = false;
        var offset = 0;

        for (var i = 0; i < protectedGroupIndices.Count; i++)
        {
            var item = index.Groups[i];
            var idx = protectedGroupIndices[i] + offset;
            if (item.Messages.Any(b => b.Contents.OfType<DataContent>().Any()))
            {
                //用户的消息里面只有一条message
                var userMessage = item.Messages.FirstOrDefault()!;

                var chatMessage = new ChatMessage()
                {
                    Role = ChatRole.User,
                    Contents = []
                };
                item.IsExcluded = true;
                foreach (var aiContent in userMessage.Contents)
                {
                    if (aiContent is DataContent)
                    {
                        //使用占位符替换
                        chatMessage.Contents.Add(new TextContent("[image]"));
                    }
                    else
                    {
                        chatMessage.Contents.Add(aiContent);
                    }
                }

                //插入原有的位置
                index.InsertGroup(idx + 1, CompactionGroupKind.User, [chatMessage],
                    item.TurnIndex);

                offset++; // 每次插入都会使后续索引后移 1 位
                compacted = true;
            }
        }

        return ValueTask.FromResult(compacted);
    }
#pragma warning restore MAAI001
}