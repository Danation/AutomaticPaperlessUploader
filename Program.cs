using AutomaticPaperlessUploader;
using AutomaticPaperlessUploader.Paperless;
using AutomaticPaperlessUploader.Status;
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

        // Attach status indicators here. Every registration receives every update, so
        // adding an LED or an OLED display is a line in this list rather than a change
        // to the upload logic.
        services.AddSingleton<IStatusIndicator, LoggingStatusIndicator>();
        services.AddSingleton<StatusReporter>();

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
