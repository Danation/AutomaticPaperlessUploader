using AutomaticPaperlessUploader;
using AutomaticPaperlessUploader.FileWatching;
using AutomaticPaperlessUploader.UserInput;

var host = Host.CreateDefaultBuilder(args)
    .UseSystemd()
    .ConfigureServices((context, services) =>
    {
        services.AddSingleton<FileWatcher>();
        services.AddHostedService<Worker>();

        var config = context.Configuration;
        services.Configure<FileWatcherOptions>(config.GetSection("FileWatching"));
        services.Configure<UserInputOptions>(config.GetSection("UserInput"));
    })
    .Build();

host.Run();
