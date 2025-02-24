using AutomaticPaperlessUploader;
using AutomaticPaperlessUploader.UserInput;

var host = Host.CreateDefaultBuilder(args)
    .UseSystemd()
    .ConfigureServices((context, services) =>
    {
        services.AddSingleton<KeyMatrixReader>();
        services.AddSingleton<UserInputInterpreter>();
        services.AddHostedService<Worker>();

        var config = context.Configuration;
        services.Configure<UserInputOptions>(config.GetSection("UserInput"));
    })
    .Build();

host.Run();
