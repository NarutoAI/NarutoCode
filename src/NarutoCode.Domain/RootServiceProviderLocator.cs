namespace NarutoCode.Domain;

/// <summary>
/// 根服务
/// </summary>
public class RootServiceProviderLocator
{
    private static IServiceProvider _serviceProvider;

    public static void Init(IServiceProvider serviceProvider)
    {
        _serviceProvider ??= serviceProvider;
    }
    
    /// <summary>
    /// 获取服务提供者实例。
    /// </summary>
    public static IServiceProvider ServiceProvider => _serviceProvider;
}