using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Resolvers;
using HotChocolate.Language;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Azure.DataGateway.Services
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
            JsonElement jsonElement;
            if (context.Selection.Field.Coordinate.TypeName.Value == "Mutation")
            {
                IDictionary<string, object> parameters = GetParametersFromContext(context);

                context.Result = await _mutationEngine.ExecuteAsync(context.Selection.Field.Name.Value, parameters);
            }
            else if (context.Selection.Field.Coordinate.TypeName.Value == "Query")
            {
                IDictionary<string, object> parameters = GetParametersFromContext(context);
                string queryId = context.Selection.Field.Name.Value;
                bool isContinuationQuery = IsContinuationQuery(parameters);

                if (context.Selection.Type.IsListType())
                {
<<<<<<< HEAD
                    context.Result = await _queryEngine.ExecuteListAsync(context, parameters);
                }
                else
                {
                    context.Result = await _queryEngine.ExecuteAsync(context, parameters, isContinuationQuery);
                }
            }
            else
            {
                JsonDocument result = context.Parent<JsonDocument>();

                JsonElement jsonElement;
                bool hasProperty =
                    result.RootElement.TryGetProperty(context.Selection.Field.Name.Value, out jsonElement);

                if (IsInnerObject(context))
                {

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
                else if (context.Selection.Field.Type.IsListType())
                {
                    if (result != null && hasProperty)
                    {
                        //TODO: System.Text.Json seem to to have very limited capabilities. This will be moved to Newtonsoft
                        IEnumerable<JObject> resultArray = JsonConvert.DeserializeObject<JObject[]>(jsonElement.ToString());
                        List<JsonDocument> resultList = new();
                        IEnumerator<JObject> enumerator = resultArray.GetEnumerator();
                        while (enumerator.MoveNext())
                        {
                            resultList.Add(JsonDocument.Parse(enumerator.Current.ToString()));
                        }

                        context.Result = resultList;
                    }
                    else
                    {
                        context.Result = null;
                    }

                }

                if (context.Selection.Field.Type.IsLeafType())
                {
                    if (result != null && hasProperty)
                    {
                        context.Result = jsonElement.ToString();
                    }
                    else
                    {
                        context.Result = null;
                    }
=======
                    context.Result = await _queryEngine.ExecuteListAsync(context, parameters);
                }
                else
                {
                    context.Result = await _queryEngine.ExecuteAsync(context, parameters);
                }
            }
            else if (context.Selection.Field.Type.IsLeafType())
            {
                // This means this field is a scalar, so we don't need to do
                // anything for it.
                if (TryGetPropertyFromParent(context, out jsonElement))
                {
                    context.Result = jsonElement.ToString();
                }
            }
            else if (IsInnerObject(context))
            {
                // This means it's a field that has another custom type as its
                // type, so there is a full JSON object inside this key. For
                // example such a JSON object could have been created by a
                // One-To-Many join.
                if (TryGetPropertyFromParent(context, out jsonElement))
                {
                    //TODO: Try to avoid additional deserialization/serialization here.
                    context.Result = JsonDocument.Parse(jsonElement.ToString());
                }
            }
            else if (context.Selection.Type.IsListType())
            {
                // This means the field is a list and HotChocolate requires
                // that to be returned as a List of JsonDocuments. For example
                // such a JSON list could have been created by a One-To-Many
                // join.
                if (TryGetPropertyFromParent(context, out jsonElement))
                {
                    //TODO: Try to avoid additional deserialization/serialization here.
                    context.Result = JsonSerializer.Deserialize<List<JsonDocument>>(jsonElement.ToString());
>>>>>>> main
                }
            }

            await _next(context);
        }

<<<<<<< HEAD
        private static bool IsContinuationQuery(IDictionary<string, object> parameters)
        {
            if (parameters.ContainsKey("after"))
            {
                return true;
            }

            return false;
=======
        private static bool TryGetPropertyFromParent(IMiddlewareContext context, out JsonElement jsonElement)
        {
            JsonDocument result = context.Parent<JsonDocument>();
            if (result == null)
            {
                jsonElement = default;
                return false;
            }

            return result.RootElement.TryGetProperty(context.Selection.Field.Name.Value, out jsonElement);
>>>>>>> main
        }

        private static bool IsInnerObject(IMiddlewareContext context)
        {
            return context.Selection.Field.Type.IsObjectType() && context.Parent<JsonDocument>() != default;
        }

        static private object ArgumentValue(IValueNode value)
        {
            if (value.Kind == SyntaxKind.IntValue)
            {
                IntValueNode intValue = (IntValueNode)value;
                return intValue.ToInt64();
            }
            else
            {
                return value.Value;
            }
        }

        private static IDictionary<string, object> GetParametersFromContext(IMiddlewareContext context)
        {
            IDictionary<string, object> parameters = new Dictionary<string, object>();

            // Fill the parameters dictionary with the default argument values
            IFieldCollection<IInputField> availableArguments = context.Selection.Field.Arguments;
            foreach (IInputField argument in availableArguments)
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
            foreach (ArgumentNode argument in passedArguments)
            {
                parameters[argument.Name.Value] = ArgumentValue(argument.Value);
            }

            return parameters;
        }
    }
}
