using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Cosmos.GraphQL.Service.Resolvers;
using HotChocolate.Language;
using HotChocolate.Resolvers;
using HotChocolate.Types;

namespace Cosmos.GraphQL.Services
{
    /// <summary>
    /// The resolver middleware that is used by the schema executor to resolve
    /// the queries and mutations
    /// </summary>
    public class ResolverMiddleware
    {
        private readonly FieldDelegate _next;
        private readonly IQueryEngine _queryEngine;
        private readonly IMutationEngine _mutationEngine;

        public ResolverMiddleware(FieldDelegate next, IQueryEngine queryEngine, IMutationEngine mutationEngine)
        {
            _next = next;
            _queryEngine = queryEngine;
            _mutationEngine = mutationEngine;
        }

        public ResolverMiddleware(FieldDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(IMiddlewareContext context)
        {
            try
            {
                if (context.Selection.Field.Coordinate.TypeName.Value == "Mutation")
                {
                    IDictionary<string, object> parameters = GetParametersFromContext(context);

                    context.Result = await _mutationEngine.ExecuteAsync(context.Selection.Field.Name.Value, parameters);
                }

                if (context.Selection.Field.Coordinate.TypeName.Value == "Query")
                {
                    IDictionary<string, object> parameters = GetParametersFromContext(context);

                    if (context.Selection.Type.IsListType())
                    {
                        context.Result = await _queryEngine.ExecuteListAsync(context.Selection.Field.Name.Value, parameters);
                    }
                    else
                    {
                        context.Result = await _queryEngine.ExecuteAsync(context.Selection.Field.Name.Value, parameters);
                    }
                }

                if (isInnerObject(context))
                {
                    JsonDocument result = context.Parent<JsonDocument>();

                    JsonElement jsonElement;
                    bool hasProperty =
                        result.RootElement.TryGetProperty(context.Selection.Field.Name.Value, out jsonElement);
                    if (result != null && hasProperty)
                    {
                        //TODO: Try to avoid additional deserialization/serialization here.
                        context.Result = JsonDocument.Parse(jsonElement.ToString());
                    }
                    else
                    {
                        context.Result = null;
                    }
                }

                if (context.Selection.Field.Type.IsLeafType())
                {
                    JsonDocument result = context.Parent<JsonDocument>();
                    JsonElement jsonElement;
                    bool hasProperty =
                        result.RootElement.TryGetProperty(context.Selection.Field.Name.Value, out jsonElement);
                    if (result != null && hasProperty)
                    {
                        context.Result = jsonElement.ToString();
                    }
                    else
                    {
                        context.Result = null;
                    }
                }

                await _next(context);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.StackTrace);
                throw;
            }
        }

        private bool isInnerObject(IMiddlewareContext context)
        {
            return context.Selection.Field.Type.IsObjectType() && context.Parent<JsonDocument>() != default;
        }

        static private object ArgumentValue(IValueNode value)
        {
            if (value.Kind == SyntaxKind.IntValue)
            {
                var intValue = (IntValueNode)value;
                return intValue.ToInt64();
            }
            else
            {
                return value.Value;
            }
        }

        private IDictionary<string, object> GetParametersFromContext(IMiddlewareContext context)
        {
            IDictionary<string, object> parameters = new Dictionary<string, object>();

            // Fill the parameters dictionary with the default argument values
            IFieldCollection<IInputField> availableArguments = context.Selection.Field.Arguments;
            foreach (var argument in availableArguments)
            {
                if (argument.DefaultValue == null)
                {
                    parameters.Add(argument.Name.Value, null);
                }
                else
                {
                    parameters.Add(argument.Name.Value, ArgumentValue(argument.DefaultValue));
                }
            }


            // Overwrite the default values with the passed in arguments
            IReadOnlyList<ArgumentNode> passedArguments = context.Selection.SyntaxNode.Arguments;
            foreach (var argument in passedArguments)
            {
                parameters[argument.Name.Value] = ArgumentValue(argument.Value);
            }

            return parameters;
        }
    }
}
