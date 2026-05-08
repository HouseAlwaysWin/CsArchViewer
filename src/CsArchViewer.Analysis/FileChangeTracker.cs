namespace CsArchViewer.Analysis;

public sealed class FileChangeTracker : IDisposable
{
    private FileSystemWatcher? _csWatcher;
    private FileSystemWatcher? _axamlWatcher;
    private FileSystemWatcher? _xamlWatcher;
    private FileSystemWatcher? _razorWatcher;
    private FileSystemWatcher? _cshtmlWatcher;
    private FileSystemWatcher? _csProjWatcher;
    private FileSystemWatcher? _slnWatcher;

    public event Action<string>? FileChanged;

    public void Start(string rootPath)
    {
        Stop();
        if (!Directory.Exists(rootPath))
        {
            return;
        }

        _csWatcher = CreateWatcher(rootPath, "*.cs");
        _axamlWatcher = CreateWatcher(rootPath, "*.axaml");
        _xamlWatcher = CreateWatcher(rootPath, "*.xaml");
        _razorWatcher = CreateWatcher(rootPath, "*.razor");
        _cshtmlWatcher = CreateWatcher(rootPath, "*.cshtml");
        _csProjWatcher = CreateWatcher(rootPath, "*.csproj");
        _slnWatcher = CreateWatcher(rootPath, "*.sln");
    }

    public void Stop()
    {
        DisposeWatcher(_csWatcher);
        DisposeWatcher(_axamlWatcher);
        DisposeWatcher(_xamlWatcher);
        DisposeWatcher(_razorWatcher);
        DisposeWatcher(_cshtmlWatcher);
        DisposeWatcher(_csProjWatcher);
        DisposeWatcher(_slnWatcher);
        _csWatcher = null;
        _axamlWatcher = null;
        _xamlWatcher = null;
        _razorWatcher = null;
        _cshtmlWatcher = null;
        _csProjWatcher = null;
        _slnWatcher = null;
    }

    private FileSystemWatcher CreateWatcher(string rootPath, string filter)
    {
        var watcher = new FileSystemWatcher(rootPath, filter)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime
        };

        watcher.Changed += OnChanged;
        watcher.Created += OnChanged;
        watcher.Renamed += (_, args) => FileChanged?.Invoke(args.FullPath);
        watcher.Deleted += (_, args) => FileChanged?.Invoke(args.FullPath);
        watcher.EnableRaisingEvents = true;
        return watcher;
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        FileChanged?.Invoke(e.FullPath);
    }

    private static void DisposeWatcher(FileSystemWatcher? watcher)
    {
        if (watcher is null)
        {
            return;
        }

        watcher.EnableRaisingEvents = false;
        watcher.Dispose();
    }

    public void Dispose()
    {
        Stop();
    }
}
