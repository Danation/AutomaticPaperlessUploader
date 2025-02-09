using AutomaticPaperlessUploader;

var host = Host.CreateDefaultBuilder(args)
    .UseSystemd()
    .ConfigureServices(services =>
    {
        services.AddSingleton<FileWatcher>();
        services.AddHostedService<Worker>();
    })
    .Build();

host.Run();
