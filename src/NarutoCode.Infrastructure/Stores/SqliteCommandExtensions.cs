using System.Data.Common;
using System.Globalization;

namespace NarutoCode.Infrastructure.Stores;

/// <summary>
/// SQLite ADO.NET 通用扩展：SQL 参数绑定（null 归一为 <see cref="DBNull" />）与时间列的读写格式。
/// 供各 Store（reader/writer）复用，避免同一套绑定与解析逻辑在多个类中重复实现。
/// </summary>
internal static class SqliteCommandExtensions
{
    /// <summary>
    /// 添加 SQL 参数；值为 <see langword="null" /> 时写入 <see cref="DBNull" />，避免 SQLite 拒绝空值参数。
    /// </summary>
    /// <param name="command">目标命令。</param>
    /// <param name="name">参数名（含 $ 前缀）。</param>
    /// <param name="value">参数值。</param>
    internal static void AddParameter(this DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    /// <summary>
    /// 读取时间列：兼容 SQLite 返回 TEXT（ISO 8601）与 <see cref="DateTime" /> 两种形态。
    /// </summary>
    /// <param name="reader">数据读取器。</param>
    /// <param name="ordinal">列序号。</param>
    /// <returns>解析后的时间。</returns>
    internal static DateTime ReadDateTime(this DbDataReader reader, int ordinal)
    {
        var value = reader.GetValue(ordinal);
        return value is DateTime dateTime
            ? dateTime
            : DateTime.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// 格式化时间为 ISO 8601 往返文本（与 TEXT 列存储约定一致）。
    /// </summary>
    /// <param name="value">时间值。</param>
    /// <returns>ISO 8601 文本。</returns>
    internal static string FormatDateTime(this DateTime value)
    {
        return value.ToString("O", CultureInfo.InvariantCulture);
    }
}
