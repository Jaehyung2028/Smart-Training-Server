using Smart_Training_Server.Hubs;
using Smart_Training_Server.Options;
using Smart_Training_Server.Services;
using Smart_Training_Server.Services.Auth;
using Smart_Training_Server.Services.Lobby;
using Smart_Training_Server.Services.Room;
using Smart_Training_Server.Services.Api;

var builder = WebApplication.CreateBuilder(args);

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
builder.Services.AddSingleton<RoomService>();
builder.Services.AddSingleton<IRoomServerProcessManager>(services => new RoomServerProcessManager(
    services.GetRequiredService<Microsoft.Extensions.Options.IOptions<RoomOptions>>().Value.ServerApiBaseUrl));
builder.Services.AddHostedService<RoomServerMonitorService>();
builder.Services.AddHostedService<RoomReservationCleanupService>();
builder.Services.AddSingleton<ContentService>();
builder.Services.AddSingleton<ServerConnectionService>();

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
app.UseTrainingApiErrors();

app.MapHub<LobbyHub>("/LobbyHub");

app.MapHub<RoomHub>("/RoomHub");

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// A room process on this machine connects through loopback HTTP. Redirecting its
// WebSocket handshake to the development HTTPS certificate prevents registration.
// Keep HTTPS redirection for other requests and for remote room-server connections.
app.UseWhen(context => !(context.Request.Path.StartsWithSegments("/RoomHub") &&
    context.Connection.RemoteIpAddress is { } address && System.Net.IPAddress.IsLoopback(address)),
    branch => branch.UseHttpsRedirection());

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Lifetime.ApplicationStopping.Register(Smart_Training_Server.Helper.ProcessHelper.KillAll);
app.Run();
