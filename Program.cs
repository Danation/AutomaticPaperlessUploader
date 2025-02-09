using AutomaticPaperlessUploader;
using AutomaticPaperlessUploader.FileWatching;

var host = Host.CreateDefaultBuilder(args)
    .UseSystemd()
    .ConfigureServices((context, services) =>
    {
        services.AddSingleton<FileWatcher>();
        services.AddHostedService<Worker>();

        var config = context.Configuration;
        services.Configure<FileWatcherOptions>(config.GetSection("FileWatching"));
    })
    .Build();

host.Run();
