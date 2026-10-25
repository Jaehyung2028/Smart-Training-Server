using Smart_Training_Server.Options;
using Smart_Training_Server.Services.Operations;
using Smart_Training_Server.Hubs;
using Smart_Training_Server.Services;
using Smart_Training_Server.Services.Api;
using Smart_Training_Server.Services.Auth;
using Smart_Training_Server.Services.Lobby;
using Smart_Training_Server.Services.Room;

// 실행 순서: 설정 읽기 → 공통 서비스 등록 → 요청 경로 연결 → 서버 실행.
// 서버의 시작점입니다. 설정·공유 서비스·백그라운드 작업·인증·HTTP 및 SignalR 경로를 연결하고 서버를 실행합니다.
var builder = WebApplication.CreateBuilder(args);
int apiPort = builder.Configuration.GetValue<int>("Server:Port", 5229);
if (apiPort is < 1 or > 65535)
{
    throw new InvalidOperationException("Server:Port는 1~65535이어야 합니다.");
}

builder.WebHost.ConfigureKestrel(options => options.ListenAnyIP(apiPort));
// 의존성 주입에 사용할 서비스를 등록합니다.
builder.Services.AddTrainingApiControllers();
// appsettings 등의 설정값을 Options 객체에 연결합니다. 서비스는 IOptions<T>로 설정을 전달받습니다.
builder.Services.AddTrainingOptions(builder.Configuration);
// HTTP 요청·응답 외에 연결을 유지하며 변경 알림과 서버 보고를 주고받을 SignalR 기능을 등록합니다.
builder.Services.AddSignalR();
builder.Services.AddAuthentication(UserTokenAuthenticationHandler.SchemeName).AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, UserTokenAuthenticationHandler>(UserTokenAuthenticationHandler.SchemeName, _ =>
    {
    });
builder.Services.AddAuthorization();
// Singleton은 요청마다 새로 만들지 않고 공유합니다. 사용자·로비·방 상태는 현재 API 프로세스의 메모리에 유지됩니다.
builder.Services.AddSingleton<UserService>();
builder.Services.AddSingleton<UserLifecycleService>();
builder.Services.AddHostedService<UserSessionCleanupService>();
builder.Services.AddSingleton<LobbyService>();
builder.Services.AddSingleton<LobbyOperatingModeService>();
builder.Services.AddHostedService<LobbyShutdownWorker>();
builder.Services.AddSingleton<ServerOperationsRegistry>(services =>
    {
    var settings = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<OperationsOptions>>().Value;
    string? path = settings.PersistHistory && builder.Configuration["SMART_TRAINING_SERVER_PROBE"] != "1" ? Path.GetFullPath(settings.HistoryPath, builder.Environment.ContentRootPath) : null;
    return new(path, services.GetRequiredService<ILogger<ServerOperationsRegistry>>());
    });
builder.Services.AddSingleton<ServerOperationsService>();
builder.Services.AddHostedService<OperationsHistoryWriter>();
builder.Services.AddSingleton<IOperationsBrowserOpener, OperationsBrowserOpener>();
builder.Services.AddHostedService<OperationsBrowserLauncher>();
builder.Services.AddSingleton<LobbyRoomUpdates>();
builder.Services.AddHostedService<LobbyRoomUpdatesService>();
builder.Services.AddSingleton<RoomService>();
builder.Services.AddSingleton<IRoomServerProcessManager>(services => new RoomServerProcessManager(services.GetRequiredService<Microsoft.Extensions.Options.IOptions<RoomOptions>>().Value.ServerApiBaseUrl));
// HostedService는 요청과 별개로 실행되며, 종료된 Unity 서버와 만료된 입장 예약을 주기적으로 정리합니다.
builder.Services.AddHostedService<RoomServerMonitorService>();
builder.Services.AddHostedService<RoomReservationCleanupService>();
builder.Services.AddSingleton<ContentService>(services => new ContentService(builder.Configuration["Contents:Directory"], services.GetRequiredService<ILogger<ContentService>>()));
builder.Services.AddSingleton<RoomServerConnectionRegistry>();
// Swagger에서 API 구조를 확인하고 요청을 실행할 수 있도록 설정합니다.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
    {
    options.AddSecurityDefinition(
        "Bearer",
        new Microsoft.OpenApi.Models.OpenApiSecurityScheme {
            Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "Opaque",
            Description = "로그인 응답의 accessToken만 입력해주세요."
        }
    );
    options.OperationFilter<SwaggerUserAuthenticationFilter>();
    });
// 지금까지 등록한 설정과 서비스를 바탕으로 실제 웹 애플리케이션을 만듭니다.
var app = builder.Build();
app.Lifetime.ApplicationStarted.Register(() =>
    {
    var roomOptions = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<RoomOptions>>().Value;
    Smart_Training_Server.Helper.NetworkHelper.WriteConnectionInfo(apiPort, roomOptions.AdvertisedAddress);
    });
// 카탈로그의 고정 로비 목록을 구성합니다. 로비 소속은 ASP.NET이 관리하며 별도 Unity 로비 프로세스는 없습니다.
app.Services.GetRequiredService<LobbyService>().InitializeCatalogLobbies();
app.UseTrainingApiErrors();
app.MapHub<LobbyUpdatesHub>("/LobbyUpdatesHub");
// RoomHub는 Unity 방 서버용 통신 경로입니다. 플레이어의 이동 패킷을 여기로 보내는 구조는 아닙니다.
app.MapHub<RoomHub>("/RoomHub");
// HTTP 요청을 처리할 미들웨어 순서를 구성합니다.
// Swagger는 개발 환경에서만 열어 API 요청을 직접 실행해 볼 수 있도록 합니다.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// 먼저 토큰으로 사용자 신원을 확인한 뒤, 요청에 필요한 접근 권한을 적용합니다.
app.UseAuthentication();
app.UseAuthorization();
// Controller의 Route·HttpGet·HttpPost 특성을 읽어서 HTTP 요청 주소와 처리 함수를 연결합니다.
app.MapControllers();
// API 종료 시 운영 기록을 마무리하고, 이 API가 실행해 관리하던 Unity 프로세스도 정리합니다.
app.Lifetime.ApplicationStopping.Register(() =>
    {
    try
    {
        app.Services.GetRequiredService<ServerOperationsRegistry>().CompleteSession();
    }
    finally
    {
        Smart_Training_Server.Helper.ProcessHelper.KillAll();
    }
    });
app.Run();
