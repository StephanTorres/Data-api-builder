using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Azure.DataApiBuilder.Service.AuthenticationHelpers
{
    /// <summary>
    /// This middleware validates JWT tokens when JWT Auth is configured
    /// and an Authorization HTTP header is present with a token.
    /// This is required since Asp.Net Core UseAuthentication() does not make
    /// AuthZ decisions nor does it terminate requests.
    /// https://github.com/aspnet/Security/issues/1613#issuecomment-358843214
    /// </summary>
    public class ClientRoleHeaderAuthenticationMiddleware
    {
        private readonly RequestDelegate _nextMiddleware;
        private const string IDENTITY_PROVIDER_DAB = "DAB-VERIFIED";

        public ClientRoleHeaderAuthenticationMiddleware(RequestDelegate next)
        {
            _nextMiddleware = next;
        }

        /// <summary>
        /// Middleware to authenticate requests where the method
        /// AuthenticateAsync() calls HandleAuthenticateAsync() in one of:
        /// - EasyAuthAuthenticationHandler
        /// - JwtBearerHandler (internal Asp.Net Core class)
        /// A successful result contains validated token data that is
        /// used to retrieve the `identity` from within the Principal in the HttpContext for use
        /// in downstream middleware.
        /// Based on the AuthenticateResult, the clientRoleHeader will be
        /// validated or set.
        /// AuthenticateResult: None -> Anonymous
        /// AuthenticateResult: Succeeded -> Authenticated/Honor client role header
        /// </summary>
        /// <param name="httpContext">Request metadata</param>
        public async Task InvokeAsync(HttpContext httpContext)
        {
            // authNResult will be one of:
            // 1. Succeeded - Authenticated
            // 2. Failure - Token issue
            // 3. None - No token provided, no auth result.
            AuthenticateResult authNResult = await httpContext.AuthenticateAsync();

            // Reject and terminate the request when an invalid token is provided
            // Write challenge response metadata (HTTP 401 Unauthorized response code
            // and www-authenticate headers) to the HTTP Context via JwtBearerHandler code
            // https://github.com/dotnet/aspnetcore/blob/3fe12b935c03138f76364dc877a7e069e254b5b2/src/Security/Authentication/JwtBearer/src/JwtBearerHandler.cs#L217
            if (authNResult.Failure is not null)
            {
                await httpContext.ChallengeAsync();
                return;
            }

            string clientDefinedRole = AuthorizationType.Anonymous.ToString();

            // A request can be authenticated in 2 cases:
            // 1. When the request has a valid jwt/easyauth token,
            // 2. When using simulator authentication in development mode.
            bool isAuthenticatedRequest = httpContext.User.Identity?.IsAuthenticated ?? false;

            if (isAuthenticatedRequest)
            {
                clientDefinedRole = AuthorizationType.Authenticated.ToString();
            }

            // Attempt to inject CLIENT_ROLE_HEADER:clientDefinedRole into the httpContext
            // to accommodate client requests that do not include such header.
            // otherwise honor existing CLIENT_ROLE_HEADER:Value
            if (!httpContext.Request.Headers.TryAdd(AuthorizationResolver.CLIENT_ROLE_HEADER, clientDefinedRole))
            {
                // Honor the client role header value already included
                // in an authenticated requests.
                if (isAuthenticatedRequest)
                {
                    clientDefinedRole = httpContext.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER];
                }
                else
                {
                    // Override existing client role header value for anonymous requests.
                    httpContext.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER]
                        = clientDefinedRole;
                }
            }

            // When the user is not in the clientDefinedRole and the client role header
            // is resolved to a system role (anonymous, authenticated), add the matcching system
            // role name as a role claim to the ClaimsIdentity.
            if (!httpContext.User.IsInRole(clientDefinedRole) && IsSystemRole(clientDefinedRole))
            {
                Claim claim = new(ClaimTypes.Role, clientDefinedRole, ClaimValueTypes.String);

                // To set the IsAuthenticated value as true, set the authenticationType.
                ClaimsIdentity identity = new(authenticationType: IDENTITY_PROVIDER_DAB);
                identity.AddClaim(claim);
                httpContext.User.AddIdentity(identity);
            }

            await _nextMiddleware(httpContext);
        }

        /// <summary>
        /// Determines whether the given role name matches one of the reserved system role names:
        /// 1. Anonymous
        /// 2. Authenticated
        /// </summary>
        /// <param name="roleName">Name of role to evaluate</param>
        /// <returns>True if roleName is a system role.</returns>
        public static bool IsSystemRole(string roleName)
        {
            return roleName.Equals(AuthorizationType.Authenticated.ToString(), StringComparison.OrdinalIgnoreCase) ||
                    roleName.Equals(AuthorizationType.Anonymous.ToString(), StringComparison.OrdinalIgnoreCase);
        }
    }

    // Extension method used to add the middleware to the HTTP request pipeline.
    public static class ClientRoleHeaderMiddlewareExtensions
    {
        public static IApplicationBuilder UseClientRoleHeaderAuthenticationMiddleware(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<ClientRoleHeaderAuthenticationMiddleware>();
        }
    }
}
