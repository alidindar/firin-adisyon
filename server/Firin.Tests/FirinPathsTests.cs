using Firin.Api;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Firin.Tests;

public sealed class FirinPathsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "firin-paths-" + Guid.NewGuid().ToString("N"));

    public FirinPathsTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, recursive: true);

    private sealed class Env(string root) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private FirinPaths Make(string? dataDir)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(dataDir is null ? [] : new Dictionary<string, string?> { ["Firin:DataDir"] = dataDir })
            .Build();
        return new FirinPaths(config, new Env(_root));
    }

    [Fact]
    public void Relative_DataDir_resolves_under_content_root()
    {
        var paths = Make("sub/veri");
        Assert.Equal(Path.GetFullPath(Path.Combine(_root, "sub", "veri")), paths.DataDir);
        Assert.True(Directory.Exists(paths.PhotosDir));
    }

    [Fact]
    public void Empty_DataDir_defaults_to_exe_folder_data() =>
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "data"), Make(null).DataDir);
}
