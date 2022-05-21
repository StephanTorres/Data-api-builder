using System.Net;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Azure.DataGateway.Service.Authorization
{
    /// <summary>
    /// This middleware to be executed prior to reaching Controllers
    /// Evaluates request and User(token) claims against developer config permissions.
    /// Authorization should do little to no request validation as that is handled
    /// in later middleware.
    /// </summary>
    public class AuthorizationEngineMiddleware
    {
        private readonly RequestDelegate _next;

        public AuthorizationEngineMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext httpContext, IAuthorizationService authorizationService)
        {
            AuthorizationResult authorizationResult = await authorizationService.AuthorizeAsync(
                user: httpContext.User,
                resource: null,
                requirements: new[] { new RoleContextPermissionsRequirement() }
            );

            if (!authorizationResult.Succeeded)
            {
                //Handle authz failure
                throw new DataGatewayException(
                    message: "authZ failed",
                    statusCode: HttpStatusCode.Forbidden,
                    subStatusCode: DataGatewayException.SubStatusCodes.AuthorizationCheckFailed);
            }

            await _next(httpContext);
        }
    }

    // Extension method used to add the middleware to the HTTP request pipeline.
    public static class AuthorizationEngineMiddlewareExtensions
    {
        public static IApplicationBuilder UseAuthorizationEngineMiddleware(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<AuthorizationEngineMiddleware>();
        }
    }
}
