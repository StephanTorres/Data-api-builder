using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config
{
    /// <summary>
    /// Indicates the settings are globally applicable.
    /// </summary>
    public record GlobalSettings
    {
        public const string JSON_PROPERTY_NAME = "runtime";
        public const string GRAPHQL_DEFAULT_PATH = "/graphql";
        public const string REST_DEFAULT_PATH = "/api";

    }

    /// <summary>
    /// Indicates the settings are for the all the APIs.
    /// </summary>
    /// <param name="Enabled">If the API is enabled.</param>
    /// <param name="Path">The URL path at which the API is available.</param>
    public record ApiSettings(
        [property: JsonIgnore]
        bool Enabled = true,
        string Path = ""
        ) : GlobalSettings();

    /// <summary>
    /// Holds the global settings used at runtime for REST Apis.
    /// </summary>
    /// <param name="Path">The URL prefix path at which endpoints
    /// for all entities will be exposed.</param>
    public record RestGlobalSettings(
        string Path = GlobalSettings.REST_DEFAULT_PATH
        ) : ApiSettings(Enabled: true, Path);

    /// <summary>
    /// Holds the global settings used at runtime for GraphQL.
    /// </summary>
    /// <param name="Path">The URL path at which the graphql endpoint will be exposed.</param>
    /// <param name="AllowIntrospection">Defines if the GraphQL introspection file
    /// will be generated by the runtime. If GraphQL is disabled, this will be ignored.</param>
    public record GraphQLGlobalSettings(
        string Path = GlobalSettings.GRAPHQL_DEFAULT_PATH,
        [property: JsonPropertyName("allow-introspection")]
        bool AllowIntrospection = true)
        : ApiSettings(Enabled: true, Path);

    /// <summary>
    /// Global settings related to hosting.
    /// </summary>
    /// <param name="Mode">The mode in which runtime is to be run.</param>
    /// <param name="Cors">Settings related to Cross Origin Resource Sharing.</param>
    /// <param name="Authentication">Authentication configuration properties.</param>
    public record HostGlobalSettings
        (HostModeType Mode = HostModeType.Production,
         [property:JsonPropertyName("authenticate-devmode-requests")]
         bool? IsDevModeDefaultRequestAuthenticated = null,
         Cors? Cors = null,
         AuthenticationConfig? Authentication = null)
        : GlobalSettings();

    /// <summary>
    /// Configuration related to Cross Origin Resource Sharing (CORS).
    /// </summary>
    /// <param name="Origins">List of allowed origins.</param>
    /// <param name="AllowCredentials">
    /// Whether to set Access-Control-Allow-Credentials CORS header.</param>
    public record Cors(
        [property: JsonPropertyName("origins")]
        string[]? Origins,
        [property: JsonPropertyName("allow-credentials")]
        bool AllowCredentials = false);

    /// <summary>
    /// Different global settings types.
    /// </summary>
    public enum GlobalSettingsType
    {
        Rest,
        GraphQL,
        Host
    }

    /// <summary>
    /// Different modes in which the runtime can run.
    /// </summary>
    public enum HostModeType
    {
        Development,
        Production
    }
}
