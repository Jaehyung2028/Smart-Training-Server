using Microsoft.AspNetCore.Diagnostics;
using Smart_Training_Server.Responses.Base;

namespace Smart_Training_Server.Services.Api;

public static class ApiResponseExtensions
{
    public static IMvcBuilder AddTrainingApiControllers(this IServiceCollection services) =>
        services.AddControllers(options => options.Filters.Add<ApiErrorResponseFilter>());

    public static void UseTrainingApiErrors(this WebApplication app)
    {
        app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
        {
            var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
            app.Logger.LogError(exception, "요청 실패. TraceId: {TraceId}", context.TraceIdentifier);
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(ApiErrorResponse.Create(500, context.TraceIdentifier));
        }));
        app.UseStatusCodePages(async statusContext =>
        {
            var context = statusContext.HttpContext;
            if (context.Request.Path.StartsWithSegments("/api"))
                await context.Response.WriteAsJsonAsync(ApiErrorResponse.Create(context.Response.StatusCode, context.TraceIdentifier));
        });
    }
}
