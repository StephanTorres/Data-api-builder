// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using System.Text.Json;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Service.Exceptions;
using HotChocolate.Resolvers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;

namespace Azure.DataApiBuilder.Core.Resolvers
{
    /// <summary>
    /// Interface for execution of GraphQL mutations against a database.
    /// </summary>
    public interface IMutationEngine
    {
        /// <summary>
        /// Executes the mutation query and returns result as JSON object asynchronously.
        /// </summary>
        /// <param name="context">Middleware context of the mutation</param>
        /// <param name="parameters">parameters in the mutation query.</param>
        /// <param name="dataSourceName">dataSourceName to execute against.</param>
        /// <returns>JSON object result and a metadata object required to resolve the result</returns>
        public Task<Tuple<JsonDocument?, IMetadata?>> ExecuteAsync(IMiddlewareContext context,
            IDictionary<string, object?> parameters,
            string dataSourceName);

        /// <summary>
        /// Executes the mutation query and returns result as JSON object asynchronously.
        /// </summary>
        /// <param name="context">context of REST mutation request.</param>
        /// <returns>IActionResult</returns>
        public Task<IActionResult?> ExecuteAsync(RestRequestContext context);

        /// <summary>
        /// Executes the stored procedure as a mutation query and returns result as JSON asynchronously.
        /// Execution will be identical regardless of mutation operation, but result returned will differ
        /// </summary>
        public Task<IActionResult?> ExecuteAsync(StoredProcedureRequestContext context, string dataSourceName);

        /// <summary>
        /// Authorization check on mutation fields provided in a GraphQL Mutation request.
        /// </summary>
        /// <param name="clientRole">Client role header value extracted from the middleware context of the mutation</param>
        /// <param name="parameters">parameters in the mutation query.</param>
        /// <param name="entityName">entity name</param>
        /// <param name="mutationOperation">mutation operation</param>
        /// <exception cref="DataApiBuilderException"></exception>
        public void AuthorizeMutationFields(
            string inputArgumentName,
            IMiddlewareContext context,
            string clientRole,
            IDictionary<string, object?> parameters,
            string entityName,
            EntityActionOperation mutationOperation);

        protected static string GetClientRoleFromMiddlewareContext(IMiddlewareContext context)
        {
            string clientRole = string.Empty;
            if (context.ContextData.TryGetValue(key: AuthorizationResolver.CLIENT_ROLE_HEADER, out object? value) && value is StringValues stringVals)
            {
                clientRole = stringVals.ToString();
            }

            if (string.IsNullOrEmpty(clientRole))
            {
                throw new DataApiBuilderException(
                    message: "No ClientRoleHeader available to perform authorization.",
                    statusCode: HttpStatusCode.Unauthorized,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.AuthorizationCheckFailed);
            }

            return clientRole;
        }
    }
}
