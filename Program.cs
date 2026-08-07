using AutomaticPaperlessUploader;
using AutomaticPaperlessUploader.Paperless;
using AutomaticPaperlessUploader.Storage;
using AutomaticPaperlessUploader.UserInput;

var host = Host.CreateDefaultBuilder(args)
    .UseSystemd()
    .ConfigureServices((context, services) =>
    {
        var config = context.Configuration;

        services.Configure<UserInputOptions>(config.GetSection("UserInput"));
        services.Configure<StorageOptions>(config.GetSection("Storage"));
        services.Configure<PaperlessOptions>(config.GetSection("Paperless"));

        services.AddSingleton<KeyMatrixReader>();
        services.AddSingleton<UserInputInterpreter>();
        services.AddSingleton<GadgetController>();
        services.AddSingleton<ImageMounter>();
        services.AddSingleton<UploadCycle>();

        // A single long lived HttpClient is the right shape here: one service, one
        // endpoint, no DNS churn. Avoids pulling in Microsoft.Extensions.Http.
        services.AddSingleton(_ =>
        {
            var options = config.GetSection("Paperless").Get<PaperlessOptions>() ?? new PaperlessOptions();
            return new HttpClient { Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds) };
        });

        services.AddSingleton<PaperlessClient>();

        services.AddHostedService<Worker>();
    })
    .Build();

host.Run();
