namespace Firin.Api;

public sealed class FirinPaths
{
    public FirinPaths(IConfiguration config, IHostEnvironment env)
    {
        var dir = config["Firin:DataDir"];
        DataDir = string.IsNullOrWhiteSpace(dir) ? Path.Combine(env.ContentRootPath, "data") : Path.GetFullPath(dir, env.ContentRootPath);
        PhotosDir = Path.Combine(DataDir, "photos");
        BackupsDir = Path.Combine(DataDir, "backups");
        Directory.CreateDirectory(PhotosDir);
        Directory.CreateDirectory(BackupsDir);
    }

    public string DataDir { get; }
    public string PhotosDir { get; }
    public string BackupsDir { get; }
    public string DbPath => Path.Combine(DataDir, "firin.db");
    public string ConnectionString => $"Data Source={DbPath}";
}
