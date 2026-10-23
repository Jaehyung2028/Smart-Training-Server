using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

// Swagger 문서에 인증이 필요한 API를 표시합니다. 실제 인증 판단은 인증 미들웨어가 수행합니다.
namespace Smart_Training_Server.Services.Auth
{
    public sealed class SwaggerUserAuthenticationFilter : IOperationFilter
    {
        // Swagger가 API 문서를 만들 때 인증 필요 여부를 읽어 보안 요구사항을 문서에 반영합니다.
        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            var metadata = context.ApiDescription.ActionDescriptor.EndpointMetadata;
            bool refresh = context.MethodInfo.DeclaringType == typeof(Smart_Training_Server._Controllers.AuthController) &&
                context.MethodInfo.Name == "Refresh";
            if (!refresh && (metadata.OfType<IAllowAnonymous>().Any() || !metadata.OfType<IAuthorizeData>().Any()))
            {
                return;
            }

            operation.Security = new List<OpenApiSecurityRequirement>
            {
                new()
                {
                    [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }] = Array.Empty<string>()
                }
            };
        }
    }
}
