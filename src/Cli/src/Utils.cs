using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using System.Text.RegularExpressions;
using Azure.DataApiBuilder.Config;
using Humanizer;
using PermissionOperation = Azure.DataApiBuilder.Config.PermissionOperation;

/// <summary>
/// Contains the methods for transforming objects, serialization options.
/// </summary>
namespace Cli
{
    public class Utils
    {
        public const string WILDCARD = "*";
        public static readonly string SEPARATOR = ":";
        public static readonly string[] SUPPORTED_SOURCE_TYPES = { "table", "view", "stored-procedure" };

        private static readonly Regex regex = new Regex(@"^[-]?\d+$");

        /// <summary>
        /// Creates the rest object which can be either a boolean value
        /// or a RestEntitySettings object containing api route based on the input
        /// </summary>
        public static object? GetRestDetails(string? rest)
        {
            object? rest_detail;
            if (rest is null)
            {
                return rest;
            }

            bool trueOrFalse;
            if (bool.TryParse(rest, out trueOrFalse))
            {
                rest_detail = trueOrFalse;
            }
            else
            {
                RestEntitySettings restEntitySettings = new("/" + rest);
                rest_detail = restEntitySettings;
            }

            return rest_detail;
        }

        /// <summary>
        /// Creates the graphql object which can be either a boolean value
        /// or a GraphQLEntitySettings object containing graphql type {singular, plural} based on the input
        /// </summary>
        public static object? GetGraphQLDetails(string? graphQL)
        {
            object? graphQL_detail;
            if (graphQL is null)
            {
                return graphQL;
            }

            bool trueOrFalse;
            if (bool.TryParse(graphQL, out trueOrFalse))
            {
                graphQL_detail = trueOrFalse;
            }
            else
            {
                string singular, plural;
                if (graphQL.Contains(SEPARATOR))
                {
                    string[] arr = graphQL.Split(SEPARATOR);
                    if (arr.Length != 2)
                    {
                        Console.Error.WriteLine($"Invalid format for --graphql. Accepted values are true/false," +
                                                "a string, or a pair of string in the format <singular>:<plural>");
                        return null;
                    }

                    singular = arr[0];
                    plural = arr[1];
                }
                else
                {
                    singular = graphQL.Singularize(inputIsKnownToBePlural: false);
                    plural = graphQL.Pluralize(inputIsKnownToBeSingular: false);
                }

                SingularPlural singularPlural = new(singular, plural);
                GraphQLEntitySettings graphQLEntitySettings = new(singularPlural);
                graphQL_detail = graphQLEntitySettings;
            }

            return graphQL_detail;
        }

        /// <summary>
        /// Try convert operation string to Operation Enum.
        /// </summary>
        /// <param name="operationName">operation string.</param>
        /// <param name="operation">Operation Enum output.</param>
        /// <returns>True if convert is successful. False otherwise.</returns>
        public static bool TryConvertOperationNameToOperation(string operationName, out Operation operation)
        {
            if (!Enum.TryParse(operationName, ignoreCase: true, out operation))
            {
                if (operationName.Equals(WILDCARD, StringComparison.OrdinalIgnoreCase))
                {
                    operation = Operation.All;
                }
                else
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Creates an array of Operation element which contains one of the CRUD operation and
        /// fields to which this operation is allowed as permission setting based on the given input.
        /// </summary>
        public static object[] CreateOperations(string operations, Policy? policy, Field? fields)
        {
            object[] operation_items;
            if (policy is null && fields is null)
            {
                return operations.Split(",");
            }

            if (operations is WILDCARD)
            {
                operation_items = new object[] { new PermissionOperation(Operation.All, policy, fields) };
            }
            else
            {
                string[]? operation_elements = operations.Split(",");
                if (policy is not null || fields is not null)
                {
                    List<object>? operation_list = new();
                    foreach (string? operation_element in operation_elements)
                    {
                        if (TryConvertOperationNameToOperation(operation_element, out Operation op))
                        {
                            PermissionOperation? operation_item = new(op, policy, fields);
                            operation_list.Add(operation_item);
                        }
                    }

                    operation_items = operation_list.ToArray();
                }
                else
                {
                    operation_items = operation_elements;
                }
            }

            return operation_items;
        }

        /// <summary>
        /// Given an array of operations, which is a type of JsonElement, convert it to a dictionary
        /// key: Valid operation (wild card operation will be expanded)
        /// value: Operation object
        /// </summary>
        /// <param name="operations">Array of operations which is of type JsonElement.</param>
        /// <returns>Dictionary of operations</returns>
        public static IDictionary<Operation, PermissionOperation> ConvertOperationArrayToIEnumerable(object[] operations)
        {
            Dictionary<Operation, PermissionOperation> result = new();
            foreach (object operation in operations)
            {
                JsonElement operationJson = (JsonElement)operation;
                if (operationJson.ValueKind is JsonValueKind.String)
                {
                    if (TryConvertOperationNameToOperation(operationJson.GetString(), out Operation op))
                    {
                        if (op is Operation.All)
                        {
                            // Expand wildcard to all valid operations
                            foreach (Operation validOp in PermissionOperation.ValidPermissionOperations)
                            {
                                result.Add(validOp, new PermissionOperation(validOp, null, null));
                            }
                        }
                        else
                        {
                            result.Add(op, new PermissionOperation(op, null, null));
                        }
                    }
                }
                else
                {
                    PermissionOperation ac = operationJson.Deserialize<PermissionOperation>(GetSerializationOptions())!;

                    if (ac.Name is Operation.All)
                    {
                        // Expand wildcard to all valid operations.
                        foreach (Operation validOp in PermissionOperation.ValidPermissionOperations)
                        {
                            result.Add(
                                validOp,
                                new PermissionOperation(validOp, Policy: ac.Policy, Fields: ac.Fields));
                        }
                    }
                    else
                    {
                        result.Add(ac.Name, ac);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Creates a single PermissionSetting Object based on role, operations, fieldsToInclude, and fieldsToExclude.
        /// </summary>
        public static PermissionSetting CreatePermissions(string role, string operations, Policy? policy, Field? fields)
        {
            return new PermissionSetting(role, CreateOperations(operations, policy, fields));
        }

        /// <summary>
        /// JsonNamingPolicy to convert all the keys in Json as lower case string.
        /// </summary>
        public class LowerCaseNamingPolicy : JsonNamingPolicy
        {
            public override string ConvertName(string name) => name.ToLower();

            public static string ConvertName(Enum name) => name.ToString().ToLower();
        }

        /// <summary>
        /// Returns the Serialization option used to convert objects into JSON.
        /// Ignoring properties with null values.
        /// Keeping all the keys in lowercase.
        /// </summary>
        public static JsonSerializerOptions GetSerializationOptions()
        {
            JsonSerializerOptions? options = new()
            {
                Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                PropertyNamingPolicy = new LowerCaseNamingPolicy()
            };

            options.Converters.Add(new JsonStringEnumConverter(namingPolicy: new LowerCaseNamingPolicy()));
            return options;
        }

        /// <summary>
        /// Returns true on successful parsing of mappings Dictionary from IEnumerable list.
        /// Returns false in case the format of the input is not correct.
        /// </summary>
        /// <param name="mappingList">List of ':' separated values indicating exposed and backend names.</param>
        /// <param name="mappings">Output a Dictionary containing mapping from backend name to exposed name.</param>
        /// <returns> Returns true when successful else on failure, returns false. Else updated PermissionSettings array will be returned.</returns>
        public static bool TryParseMappingDictionary(IEnumerable<string> mappingList, out Dictionary<string, string> mappings)
        {
            mappings = new();
            foreach (string item in mappingList)
            {
                string[] map = item.Split(SEPARATOR);
                if (map.Length != 2)
                {
                    Console.Error.WriteLine("Invalid format for --map");
                    Console.WriteLine("It should be in this format --map \"backendName1:exposedName1,backendName2:exposedName2,...\".");
                    return false;
                }

                mappings.Add(map[0], map[1]);
            }

            return true;
        }

        /// <summary>
        /// Returns the default global settings.
        /// </summary>
        public static Dictionary<GlobalSettingsType, object> GetDefaultGlobalSettings(HostModeType hostMode,
                                                                                      IEnumerable<string>? corsOrigin,
                                                                                      bool? devModeDefaultAuth)
        {
            Dictionary<GlobalSettingsType, object> defaultGlobalSettings = new();
            defaultGlobalSettings.Add(GlobalSettingsType.Rest, new RestGlobalSettings());
            defaultGlobalSettings.Add(GlobalSettingsType.GraphQL, new GraphQLGlobalSettings());
            defaultGlobalSettings.Add(
                GlobalSettingsType.Host,
                GetDefaultHostGlobalSettings(hostMode, corsOrigin, devModeDefaultAuth));
            return defaultGlobalSettings;
        }

        /// <summary>
        /// Returns the default host Global Settings
        /// If the user doesn't specify host mode. Default value to be used is Production.
        /// Sample:
        // "host": {
        //     "mode": "production",
        //     "cors": {
        //         "origins": [],
        //         "allow-credentials": true
        //     },
        //     "authentication": {
        //         "provider": "StaticWebApps"
        //     }
        // }
        /// </summary>
        public static HostGlobalSettings GetDefaultHostGlobalSettings(
            HostModeType hostMode,
            IEnumerable<string>? corsOrigin,
            bool? devModeDefaultAuth)
        {
            string[]? corsOriginArray = corsOrigin is null ? new string[] { } : corsOrigin.ToArray();
            Cors cors = new(Origins: corsOriginArray);
            AuthenticationConfig authenticationConfig = new(Provider: EasyAuthType.StaticWebApps.ToString());
            return new HostGlobalSettings(
                Mode: hostMode,
                IsDevModeDefaultRequestAuthenticated: devModeDefaultAuth,
                Cors: cors,
                Authentication: authenticationConfig);
        }

        /// <summary>
        /// Returns an object of type Policy
        /// If policyRequest or policyDatabase is provided. Otherwise, returns null.
        /// </summary>
        public static Policy? GetPolicyForOperation(string? policyRequest, string? policyDatabase)
        {
            if (policyRequest is not null || policyDatabase is not null)
            {
                return new Policy(policyRequest, policyDatabase);
            }

            return null;
        }

        /// <summary>
        /// Returns an object of type Field
        /// If fieldsToInclude or fieldsToExclude is provided. Otherwise, returns null.
        /// </summary>
        public static Field? GetFieldsForOperation(IEnumerable<string>? fieldsToInclude, IEnumerable<string>? fieldsToExclude)
        {
            if (fieldsToInclude is not null && fieldsToInclude.Any() || fieldsToExclude is not null && fieldsToExclude.Any())
            {
                HashSet<string>? fieldsToIncludeSet = fieldsToInclude is not null && fieldsToInclude.Any() ? new HashSet<string>(fieldsToInclude) : null;
                HashSet<string>? fieldsToExcludeSet = fieldsToExclude is not null && fieldsToExclude.Any() ? new HashSet<string>(fieldsToExclude) : null;
                return new Field(fieldsToIncludeSet, fieldsToExcludeSet);
            }

            return null;
        }

        /// <summary>
        /// Try to read and deserialize runtime config from a file.
        /// </summary>
        /// <param name="file">File path.</param>
        /// <param name="runtimeConfigJson">Runtime config output. On failure, this will be null.</param>
        /// <returns>True on success. On failure, return false and runtimeConfig will be set to null.</returns>
        public static bool TryReadRuntimeConfig(string file, out string runtimeConfigJson)
        {
            runtimeConfigJson = string.Empty;

            if (!File.Exists(file))
            {
                Console.WriteLine($"ERROR: Couldn't find config  file: {file}.");
                Console.WriteLine($"Please run: dab init <options> to create a new config file.");
                return false;
            }

            // Read existing config file content.
            //
            runtimeConfigJson = File.ReadAllText(file);
            return true;
        }

        /// <summary>
        /// Verifies whether the operation provided by the user is valid or not
        /// Example:
        /// *, create -> Invalid
        /// create, create, read -> Invalid
        /// * -> Valid
        /// fetch, read -> Invalid
        /// read, delete -> Valid
        /// </summary>
        /// <param name="operations">array of string containing operations for permissions</param>
        /// <returns>True if no invalid operation is found.</returns>
        public static bool VerifyOperations(string[] operations)
        {
            // Check if there are any duplicate operations
            // Ex: read,read,create
            HashSet<string> uniqueOperations = operations.ToHashSet();
            if (uniqueOperations.Count() != operations.Length)
            {
                Console.Error.WriteLine("Duplicate action found in --permissions");
                return false;
            }

            bool containsWildcardOperation = false;
            foreach (string operation in uniqueOperations)
            {
                if (TryConvertOperationNameToOperation(operation, out Operation op))
                {
                    if (op is Operation.All)
                    {
                        containsWildcardOperation = true;
                    }
                    else if (!PermissionOperation.ValidPermissionOperations.Contains(op))
                    {
                        Console.Error.WriteLine("Invalid actions found in --permissions");
                        return false;
                    }
                }
                else
                {
                    // Check for invalid operation.
                    Console.Error.WriteLine("Invalid actions found in --permissions");
                    return false;
                }
            }

            // Check for WILDCARD operation with CRUD operations.
            if (containsWildcardOperation && uniqueOperations.Count() > 1)
            {
                Console.Error.WriteLine(" WILDCARD(*) along with other CRUD operations in a single operation is not allowed.");
                return false;
            }

            return true;
        }

        /// <summary>
        /// This method will parse role and operation from permission string.
        /// A valid permission string will be of the form "<<role>>:<<actions>>"
        /// It will return true if parsing is successful and add the parsed value
        /// to the out params role and operations.
        /// </summary>
        public static bool TryGetRoleAndOperationFromPermission(IEnumerable<string> permissions, out string? role, out string? operations)
        {
            // Split permission to role and operations.
            role = null;
            operations = null;
            if (permissions.Count() != 2)
            {
                Console.WriteLine("Please add permission in the following format. --permissions \"<<role>>:<<actions>>\"");
                return false;
            }

            role = permissions.ElementAt(0);
            operations = permissions.ElementAt(1);
            return true;
        }

        /// <summary>
        /// This method will try to find the config file based on the precedence.
        /// If the config file is provided by user, it will return that.
        /// Else it will check the DAB_ENVIRONMENT variable.
        /// In case the environment variable is not set it will check for default config.
        /// If none of the files exists it will return false. Else true with output in runtimeConfigFile.
        /// In case of false, the runtimeConfigFile will be set to string.Empty.
        /// </summary>
        public static bool TryGetConfigFileBasedOnCliPrecedence(
            string? userProvidedConfigFile,
            out string runtimeConfigFile)
        {
            if (!string.IsNullOrEmpty(userProvidedConfigFile))
            {
                /// The existence of user provided config file is not checked here.
                Console.WriteLine($"Using config file: {userProvidedConfigFile}");
                RuntimeConfigPath.CheckPrecedenceForConfigInEngine = false;
                runtimeConfigFile = userProvidedConfigFile;
                return true;
            }
            else
            {
                Console.WriteLine("Config not provided. Trying to get default config based on DAB_ENVIRONMENT...");
                /// Need to reset to true explicitly so any that any re-invocations of this function
                /// get simulated as being called for the first time specifically useful for tests.
                RuntimeConfigPath.CheckPrecedenceForConfigInEngine = true;
                runtimeConfigFile = RuntimeConfigPath.GetFileNameForEnvironment(
                        hostingEnvironmentName: null,
                        considerOverrides: false);

                /// So that the check doesn't run again when starting engine
                RuntimeConfigPath.CheckPrecedenceForConfigInEngine = false;
            }

            return !string.IsNullOrEmpty(runtimeConfigFile);
        }

        /// <summary>
        /// Returns true on successful parsing of source object from name, type,
        /// parameters, and key Fields.
        /// Returns false otherwise.
        /// </summary>
        public static bool TryGetSourceObject(
            string name,
            string? type,
            Dictionary<string, object>? parameters,
            string[]? keyFields,
            out object? sourceObject
        )
        {
            type = (string.Empty).Equals(type) ? null : type;
            sourceObject = null;

            if (!VerifySourceObjectFields(name, type, parameters, keyFields))
            {
                Console.Error.WriteLine("Invalid Fields for Source.");
                return false;
            }

            if (type is null && parameters is null && keyFields is null)
            {
                sourceObject = name;
                return true;
            }

            // Verify the given source type is valid.
            if (type is not null && !SUPPORTED_SOURCE_TYPES.Contains(type.ToLowerInvariant()))
            {
                Console.Error.WriteLine("Source type must be one of:" + SUPPORTED_SOURCE_TYPES.ToString());
                return false;
            }

            sourceObject = new DatabaseObjectSource(
                Type: type is null ? "table" : type,    //If sourceType is not explicitly specified, we assume it is a Table
                Name: name,
                Parameters: parameters,
                KeyFields: keyFields
            );

            return true;
        }

        /// <summary>
        /// Verifies whether a valid source object can be created from the given source fields.
        /// </summary>
        public static bool VerifySourceObjectFields(
            string? name,
            string? type,
            Dictionary<string, object>? parameters,
            string[]? keyFields
        )
        {
            if (name is null)
            {
                return false;
            }

            SourceType? objectType;
            if (type is not null)
            {
                try
                {
                    objectType = Entity.ConvertSourceType(type);
                }
                catch (Exception e)
                {
                    // Invalid SourceType provided.
                    Console.Error.WriteLine(e.Message);
                    return false;
                }
            }
            else
            {
                // If sourceType is not explicitly specified, we assume it is a Table
                objectType = SourceType.Table;
            }

            if ((SourceType.StoredProcedure).Equals(objectType))
            {
                if (keyFields is not null)
                {
                    Console.Error.WriteLine("KeyFields is only supported for Table and Views.");
                    return false;
                }
            }
            else
            {
                if (parameters is not null)
                {
                    Console.Error.WriteLine("parameters are only supported for Stored Procedure.");
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Returns true on successful parsing of source parameters Dictionary from IEnumerable list.
        /// Returns false in case the format of the input is not correct.
        /// </summary>
        /// <param name="parametersList">List of ':' separated values indicating key and value.</param>
        /// <param name="mappings">Output a Dictionary of parameters and their values.</param>
        /// <returns> Returns true when successful else on failure, returns false.</returns>
        public static bool TryParseSourceParameterDictionary(
            IEnumerable<string>? parametersList,
            out Dictionary<string, object>? sourceParameters)
        {
            sourceParameters = null;
            if (parametersList is null)
            {
                return true;
            }

            sourceParameters = new(StringComparer.InvariantCultureIgnoreCase);
            foreach (string param in parametersList)
            {
                string[] items = param.Split(SEPARATOR);
                if (items.Length != 2)
                {
                    sourceParameters = null;
                    Console.Error.WriteLine("Invalid format for --source.params");
                    Console.WriteLine("It should be in this format --source.params \"key1:value1,key2:value2,...\".");
                    return false;
                }
                string paramKey = items[0];
                object paramValue = ParseParamValue(items[1]);

                sourceParameters.Add(paramKey, paramValue);
            }

            if (!sourceParameters.Any())
            {
                sourceParameters = null;
            }

            return true;
        }

        private static object ParseParamValue(string stringValue)
        {
            object paramValue;
            if (regex.IsMatch(stringValue))
            {
                paramValue = int.Parse(stringValue);
            }
            else if (Boolean.TryParse(stringValue, out bool booleanValue))
            {
                paramValue = booleanValue;
            }
            else
            {
                paramValue = stringValue;
            }

            return paramValue;
        }

        /// <summary>
        /// This method will write all the json string in the given file.
        /// </summary>
        public static bool WriteJsonContentToFile(string file, string jsonContent)
        {
            try
            {
                File.WriteAllText(file, jsonContent);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"Failed to generate the config file, operation failed with exception:{e}.");
                return false;
            }

            return true;
        }
    }
}
