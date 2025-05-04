using System.IO.Abstractions.TestingHelpers;
using Scribal.Cli;

namespace Scribal.Tests;

public class DocumentScannerTests
{
    [Fact]
    public async Task Test()
    {
        var filesystem = new MockFileSystem();
        
        var sut = new DocumentScanService(filesystem);

        var foo = filesystem.DirectoryInfo.New("foo");
        foo.Create();
        
        var result = await sut.ScanDirectoryForMarkdownAsync(foo);
        
        await Verifier.Verify(result)
            .UseDirectory("Snapshots")
            .UseFileName("PromptRenderer_ComplexTemplate");

    }
}