using Smart_Training_Server.Hubs;
using Smart_Training_Server.Options;
using Smart_Training_Server.Services;
using Smart_Training_Server.Services.Auth;
using Smart_Training_Server.Services.Lobby;
using Smart_Training_Server.Services.Room;
using Smart_Training_Server.Services.Api;

var builder = WebApplication.CreateBuilder(args);
int apiPort = builder.Configuration.GetValue<int>("Server:Port", 5229);
if (apiPort is < 1 or > 65535) throw new InvalidOperationException("Server:Port는 1~65535이어야 합니다.");
builder.WebHost.ConfigureKestrel(options => options.ListenAnyIP(apiPort));

// Add services to the container.

builder.Services.AddTrainingApiControllers();

builder.Services.AddTrainingOptions(builder.Configuration);

builder.Services.AddSignalR();
builder.Services.AddAuthentication(UserTokenAuthenticationHandler.SchemeName)
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, UserTokenAuthenticationHandler>(
        UserTokenAuthenticationHandler.SchemeName, _ => { });
builder.Services.AddAuthorization();

builder.Services.AddSingleton<UserService>();
builder.Services.AddSingleton<UserLifecycleService>();
builder.Services.AddHostedService<UserSessionCleanupService>();
builder.Services.AddSingleton<LobbyService>();
builder.Services.AddSingleton<Smart_Training_Server.Services.Operations.LobbyShutdownService>();
builder.Services.AddHostedService<Smart_Training_Server.Services.Operations.LobbyShutdownWorker>();
builder.Services.AddSingleton<Smart_Training_Server.Services.Operations.ServerOperationsRegistry>(services =>
{
    var settings = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<OperationsOptions>>().Value;
    string? path = settings.PersistHistory && builder.Configuration["SMART_TRAINING_SERVER_PROBE"] != "1"
        ? Path.GetFullPath(settings.HistoryPath, builder.Environment.ContentRootPath) : null;
    return new(path, services.GetRequiredService<ILogger<Smart_Training_Server.Services.Operations.ServerOperationsRegistry>>());
});
builder.Services.AddSingleton<Smart_Training_Server.Services.Operations.ServerOperationsService>();
builder.Services.AddHostedService<Smart_Training_Server.Services.Operations.OperationsHistoryWriter>();
builder.Services.AddSingleton<Smart_Training_Server.Services.Operations.IOperationsBrowserOpener, Smart_Training_Server.Services.Operations.OperationsBrowserOpener>();
builder.Services.AddHostedService<Smart_Training_Server.Services.Operations.OperationsBrowserLauncher>();
builder.Services.AddSingleton<LobbyRoomUpdates>();
builder.Services.AddHostedService<LobbyRoomUpdatesService>();
builder.Services.AddSingleton<RoomService>();
builder.Services.AddSingleton<IRoomServerProcessManager>(services => new RoomServerProcessManager(
    services.GetRequiredService<Microsoft.Extensions.Options.IOptions<RoomOptions>>().Value.ServerApiBaseUrl));
builder.Services.AddHostedService<RoomServerMonitorService>();
builder.Services.AddHostedService<RoomReservationCleanupService>();
builder.Services.AddSingleton<ContentService>(services => new ContentService(builder.Configuration["Contents:Directory"], services.GetRequiredService<ILogger<ContentService>>()));
builder.Services.AddSingleton<ILobbyServerProcessManager>(services => new LobbyServerProcessManager(
    services.GetRequiredService<Microsoft.Extensions.Options.IOptions<RoomOptions>>().Value.ServerApiBaseUrl));
builder.Services.AddHostedService<LobbyServerMonitorService>();
builder.Services.AddSingleton<RoomServerConnectionRegistry>();

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "Opaque",
        Description = "로그인 응답의 accessToken만 입력해주세요."
    });
    options.OperationFilter<SwaggerUserAuthenticationFilter>();
});

var app = builder.Build();
app.Services.GetRequiredService<LobbyService>().InitializeCatalogLobbies();
app.UseTrainingApiErrors();

app.MapHub<LobbyUpdatesHub>("/LobbyUpdatesHub");

app.MapHub<RoomHub>("/RoomHub");
app.MapHub<LobbyServerHub>("/LobbyServerHub");

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Lifetime.ApplicationStopping.Register(() =>
{
    try { app.Services.GetRequiredService<Smart_Training_Server.Services.Operations.ServerOperationsRegistry>().CompleteSession(); }
    finally { Smart_Training_Server.Helper.ProcessHelper.KillAll(); }
});
app.Run();


