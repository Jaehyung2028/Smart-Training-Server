using Microsoft.AspNetCore.Diagnostics;
using Smart_Training_Server.Responses.Base;

// Controller와 공통 오류 처리를 등록합니다. 처리되지 않은 예외는 로그에 남기고 공통 500 응답을 반환합니다.
namespace Smart_Training_Server.Services.Api
{
    public static class ApiResponseExtensions
    {
        // Controller 기능을 등록하면서 공통 오류 결과 필터도 함께 추가합니다.
        public static IMvcBuilder AddTrainingApiControllers(this IServiceCollection services)
        {
            return services.AddControllers(options => options.Filters.Add<ApiErrorResponseFilter>());
        }

        // 처리되지 않은 예외와 본문 없는 API 오류 상태에 공통 오류 응답을 연결합니다.
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
                {
                    await context.Response.WriteAsJsonAsync(ApiErrorResponse.Create(context.Response.StatusCode, context.TraceIdentifier));
                }
            });
        }
    }
}
