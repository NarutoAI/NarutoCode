using NarutoCode.Gateway.Configuration;
namespace NarutoCode.Gateway.Tests.Configuration;
[TestClass] public sealed class GatewayConfigurationTests
{
 [TestMethod] public async Task LoadAsync_WhenBindingIdsRepeat_ThrowsInvalidOperationException()
 { var path=Path.GetTempFileName(); try { await File.WriteAllTextAsync(path,"""{"weComBots":[{"id":"same","workspace":"/a"},{"id":"same","workspace":"/b"}]}"""); await Assert.ThrowsExactlyAsync<InvalidOperationException>(()=>GatewayConfiguration.LoadAsync(path)); } finally { File.Delete(path); } }
}
