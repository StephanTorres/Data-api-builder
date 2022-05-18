using System.Collections.Generic;
using Microsoft.AspNetCore.Http;

namespace Azure.DataGateway.Service.Authorization
{
    /// <summary>
    /// Interface for authorization decision-making. Each method performs lookups within a
    /// structure representing permissions defined in the runtime config.
    /// </summary>
    public interface IAuthorizationResolver
    {
        /// <summary>
        /// Checks for the existence of the client role header in httpContext.Request.Headers
        /// and evaluates that header against the authenticated (httpContext.User)'s roles
        /// </summary>
        /// <param name="httpContext">Contains request headers and metadata of the authenticated user.</param>
        /// <returns>True, if client role header exists and matches authenticated user's roles.</returns>
        public bool IsValidRoleContext(HttpContext httpContext);

        /// <summary>
        /// Checks if the permissions collection of the requested entity
        /// contains an entry for the role defined in the client role header.
        /// </summary>
        /// <param name="entityName">Entity from request</param>
        /// <param name="roleName">Role defined in client role header</param>
        /// <param name="action">Action type: Create, Read, Update, Delete</param>
        /// <returns>True, if a matching permission entry is found.</returns>
        public bool AreRoleAndActionDefinedForEntity(string entityName, string roleName, string action);

        /// <summary>
        /// Any columns referenced in a request's headers, URL(filter/orderby/routes), and/or body
        /// are compared against the inclued/excluded column permission defined for the entityName->roleName->action
        /// </summary>
        /// <param name="entityName">Entity from request</param>
        /// <param name="roleName">Role defined in client role header</param>
        /// <param name="action">Action type: Create, Read, Update, Delete</param>
        /// <param name="columns">Compiled list of any column referenced in a request</param>
        /// <returns></returns>
        public bool AreColumnsAllowedForAction(string entityName, string roleName, string action, List<string> columns);

        /// <summary>
        /// Retrieves the policy of an action within an entity's role entry
        /// within the permissions section of the runtime config.
        /// </summary>
        /// <param name="entityName">Entity from request</param>
        /// <param name="roleName">Role defined in client role header</param>
        /// <param name="action">Action type: Create, Read, Update, Delete</param>
        /// <param name="httpContext">Contains token claims of the authenticated user used in policy evaluation.</param>
        /// <returns>True, if query predicates are successfully appended to the auto-generated database query.
        /// or if a policy is not defined.</returns>
        public bool DidProcessDBPolicy(string entityName, string roleName, string action, HttpContext httpContext);
    }
}
