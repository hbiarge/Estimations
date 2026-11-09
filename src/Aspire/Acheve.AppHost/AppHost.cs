using Acheve.Common.Shared;

var builder = DistributedApplication.CreateBuilder(args);

// Message broker used by Rebus. The dashboard links to the RabbitMQ management UI.
var messaging = builder.AddRabbitMQ(Constants.Resources.Messaging)
    .WithManagementPlugin();

// Database for the process manager's saga data and deferred messages. The volume keeps the data between runs.
var processManagerDatabase = builder.AddPostgres(Constants.Resources.Postgres)
    .WithDataVolume()
    .AddDatabase(Constants.Resources.ProcessManagerDatabase);

// Local folder shared by all services for Rebus data bus attachments (images and estimations).
var storagePath = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "..", "..", ".data"));
Directory.CreateDirectory(storagePath);

// Key that signs the callback URLs given to the external systems. Aspire generates it on the first run
// and keeps it in the AppHost user secrets, so callbacks for cases in progress still work after a restart.
var callbackSigningKey = builder.AddParameter(
    Constants.Callbacks.SigningKeyParameter,
    new GenerateParameterDefault { MinLength = 32, Special = false },
    secret: true,
    persist: true);

// Fake external systems the application talks to
var externalImages = builder.AddProject<Projects.Acheve_External_Images>(Constants.Resources.ExternalImages);
var externalImageProcess = builder.AddProject<Projects.Acheve_External_ImageProcess>(Constants.Resources.ExternalImageProcess);
var externalEstimations = builder.AddProject<Projects.Acheve_External_Estimations>(Constants.Resources.ExternalEstimations);
var externalNotifications = builder.AddProject<Projects.Acheve_External_Notifications>(Constants.Resources.ExternalNotifications);

// Application services
var stateHolder = AddService<Projects.Acheve_Application_StateHolder>(Constants.Resources.StateHolder);

var api = AddService<Projects.Acheve_Application_Api>(Constants.Resources.Api)
    .WithCallbackSigningKey()
    .WithReference(stateHolder)
    .WaitFor(stateHolder);

AddService<Projects.Acheve_Application_ProcessManager>(Constants.Resources.ProcessManager)
    .WithReference(processManagerDatabase)
    .WaitFor(processManagerDatabase);

AddService<Projects.Acheve_Application_ImageDownloads>(Constants.Resources.ImageDownloads)
    .WithReference(externalImages);

AddService<Projects.Acheve_Application_ExternalImageProcessor>(Constants.Resources.ExternalImageProcessor)
    .WithCallbackSigningKey()
    .WithReference(externalImageProcess)
    .WithReference(api); // Sends the Api URL as the callback URL

AddService<Projects.Acheve_Application_EstimationProcessor>(Constants.Resources.EstimationProcessor)
    .WithCallbackSigningKey()
    .WithReference(externalEstimations)
    .WithReference(api); // Sends the Api URL as the callback URL

AddService<Projects.Acheve_Application_CallbackNotifier>(Constants.Resources.CallbackNotifier)
    .WithReference(externalNotifications);

builder.Build().Run();

// Every application service uses RabbitMQ and the shared storage folder.
IResourceBuilder<ProjectResource> AddService<TProject>(string name) where TProject : IProjectMetadata, new() =>
    builder.AddProject<TProject>(name)
        .WithReference(messaging)
        .WaitFor(messaging)
        .WithEnvironment(Constants.Storage.RootPathKey.Replace(":", "__"), storagePath);

static class CallbackSigningExtensions
{
    // The services that sign (processors) or check (Api) the callback URLs.
    public static IResourceBuilder<ProjectResource> WithCallbackSigningKey(this IResourceBuilder<ProjectResource> project)
    {
        var key = project.ApplicationBuilder.Resources
            .OfType<ParameterResource>()
            .Single(x => x.Name == Constants.Callbacks.SigningKeyParameter);

        return project.WithEnvironment(Constants.Callbacks.SigningKeyKey.Replace(":", "__"), key);
    }
}
