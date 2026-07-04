//
// using NarutoCode.Infrastructure.AIAgents.AIContextProviders;
//
// namespace NarutoCode.Infrastructure.Tests.Tools;
//
// [TestClass]
// public sealed class SearchAgentToolsTests
// {
//     private readonly string tempDirectory;
//     private readonly FSTollsAiContextProvider _fsTollsAiContextProvider;
//
//     public SearchAgentToolsTests()
//     {
//         tempDirectory = Path.Combine(Directory.GetCurrentDirectory(), "NarutoCodeTests", Guid.NewGuid().ToString("N"));
//         Directory.CreateDirectory(tempDirectory);
//         _fsTollsAiContextProvider=new FSTollsAiContextProvider(new NullWorkspaceContextAccessor());
//     }
//
//     [TestCleanup]
//     public void Cleanup()
//     {
//         if (Directory.Exists(tempDirectory))
//         {
//             Directory.Delete(tempDirectory, recursive: true);
//         }
//     }
//
//     [TestMethod]
//     public async Task ReadFileLines_WithValidRange_ReturnsInclusiveNumberedLines()
//     {
//         // Arrange
//         var filePath = Path.Combine(tempDirectory, "sample.txt");
//         await File.WriteAllTextAsync(filePath, "one\ntwo\nthree\nfour");
//
//         // Act
//         var result = await _fsTollsAiContextProvider.ReadFileLines(filePath, 2, 3);
//
//         // Assert
//         var normalizedPath = filePath.Replace(Path.DirectorySeparatorChar, '/');
//         Assert.AreEqual($"{normalizedPath}:2:two{Environment.NewLine}{normalizedPath}:3:three", result);
//     }
//
//     [TestMethod]
//     public async Task ReplaceFileLines_WithValidRange_ReplacesInclusiveRange()
//     {
//         // Arrange
//         var filePath = Path.Combine(tempDirectory, "sample.txt");
//         await File.WriteAllTextAsync(filePath, "one\ntwo\nthree\nfour");
//
//         // Act
//         var result = await _fsTollsAiContextProvider.ReplaceFileLines(filePath, 2, 3, "[\"TWO\",\"Three\"]");
//
//         // Assert
//         Assert.Contains("已替换", result);
//         Assert.Contains("2-3", result);
//         var content = await File.ReadAllTextAsync(filePath);
//         Assert.AreEqual($"one{Environment.NewLine}TWO{Environment.NewLine}THREE{Environment.NewLine}four{Environment.NewLine}", content);
//     }
//
//     [TestMethod]
//     public async Task ReplaceFileLines_WithEmptyContent_DeletesInclusiveRange()
//     {
//         // Arrange
//         var filePath = Path.Combine(tempDirectory, "sample.txt");
//         await File.WriteAllTextAsync(filePath, "one\ntwo\nthree\nfour\n");
//
//         // Act
//         var result = await _fsTollsAiContextProvider.ReplaceFileLines(filePath, 2, 3, string.Empty);
//
//         // Assert
//         Assert.Contains("已替换", result);
//         var content = await File.ReadAllTextAsync(filePath);
//         Assert.AreEqual($"one{Environment.NewLine}four{Environment.NewLine}", content);
//     }
//
//     [TestMethod]
//     public async Task ReadFileLines_WhenRangeExceedsFile_ReturnsErrorMessage()
//     {
//         // Arrange
//         var filePath = Path.Combine(tempDirectory, "sample.txt");
//         await File.WriteAllTextAsync(filePath, "one\ntwo");
//
//         // Act
//         var result = await _fsTollsAiContextProvider.ReadFileLines(filePath, 1, 3);
//
//         // Assert
//         Assert.Contains("结束行超出文件总行数", result);
//         Assert.Contains("共 2 行", result);
//     }
// }
