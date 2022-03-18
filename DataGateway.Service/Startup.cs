using System;
using Azure.DataGateway.Service.AuthenticationHelpers;
using Azure.DataGateway.Service.Authorization;
using Azure.DataGateway.Service.Configurations;
using Azure.DataGateway.Service.Resolvers;
using Azure.DataGateway.Service.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MySqlConnector;
using Npgsql;

namespace Azure.DataGateway.Service
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            DoConfigureServices(services, Configuration);
            services.AddControllers();
        }

        /// <summary>
        /// This method adds services that are used when running this project or the
        /// functions project. Any services that are required should be added here, unless
        /// it is only required for one or the other.
        /// </summary>
        /// <param name="services">The service collection to which services will be added.</param>
        /// <param name="config">The applications configuration.</param>
        public static void DoConfigureServices(IServiceCollection services, IConfiguration config)
        {
            services.Configure<DataGatewayConfig>(config.GetSection(nameof(DataGatewayConfig)));
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IPostConfigureOptions<DataGatewayConfig>, DataGatewayConfigPostConfiguration>());
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<DataGatewayConfig>, DataGatewayConfigValidation>());

            // Read configuration and use it locally.
            DataGatewayConfig dataGatewayConfig = new();
            config.Bind(nameof(DataGatewayConfig), dataGatewayConfig);

            switch (dataGatewayConfig.DatabaseType)
            {
                case DatabaseType.Cosmos:
                    services.AddSingleton<CosmosClientProvider, CosmosClientProvider>();
                    services.AddSingleton<IMetadataStoreProvider, FileMetadataStoreProvider>();
                    services.AddSingleton<IQueryEngine, CosmosQueryEngine>();
                    services.AddSingleton<IMutationEngine, CosmosMutationEngine>();
                    services.AddSingleton<IConfigValidator, CosmosConfigValidator>();
                    break;
                case DatabaseType.MsSql:
                    services.AddSingleton<IMetadataStoreProvider, FileMetadataStoreProvider>();
                    services.AddSingleton<IQueryExecutor, QueryExecutor<SqlConnection>>();
                    services.AddSingleton<IQueryBuilder, MsSqlQueryBuilder>();
                    services.AddSingleton<IQueryEngine, SqlQueryEngine>();
                    services.AddSingleton<IMutationEngine, SqlMutationEngine>();
                    services.AddSingleton<IConfigValidator, SqlConfigValidator>();
                    break;
                case DatabaseType.PostgreSql:
                    services.AddSingleton<IMetadataStoreProvider, FileMetadataStoreProvider>();
                    services.AddSingleton<IQueryExecutor, QueryExecutor<NpgsqlConnection>>();
                    services.AddSingleton<IQueryBuilder, PostgresQueryBuilder>();
                    services.AddSingleton<IQueryEngine, SqlQueryEngine>();
                    services.AddSingleton<IMutationEngine, SqlMutationEngine>();
                    services.AddSingleton<IConfigValidator, SqlConfigValidator>();
                    break;
                case DatabaseType.MySql:
                    services.AddSingleton<IMetadataStoreProvider, FileMetadataStoreProvider>();
                    services.AddSingleton<IQueryExecutor, QueryExecutor<MySqlConnection>>();
                    services.AddSingleton<IQueryBuilder, MySqlQueryBuilder>();
                    services.AddSingleton<IQueryEngine, SqlQueryEngine>();
                    services.AddSingleton<IMutationEngine, SqlMutationEngine>();
                    services.AddSingleton<IConfigValidator, SqlConfigValidator>();
                    break;
                default:
                    throw new NotSupportedException(String.Format("The provided DatabaseType value: {0} is currently not supported." +
                        "Please check the configuration file.", dataGatewayConfig.DatabaseType));
            }

            services.AddSingleton<GraphQLService, GraphQLService>();
            services.AddSingleton<RestService, RestService>();

            //Enable accessing HttpContext in RestService to get ClaimsPrincipal.
            services.AddHttpContextAccessor();

            // Setting defaultScheme for AddAuthentication() will ensure the JwtBearerHandler (within AddJwtBearer)
            // is called for every request, and populate the httpContext.User. No defaultScheme requires manually calling
            // JwtBearerHandler.HandleAuthenticateAsync() for usage of JwtBearerDefaults.AuthenticationScheme.
            // Then proceed to register the JwtBearer scheme with AddJwtBearer().
            // Reference: https://docs.microsoft.com/aspnet/core/security/authorization/limitingidentitybyscheme?view=aspnetcore-6.0
            if (dataGatewayConfig.JwtAuth != null)
            {
                services.AddAuthentication(defaultScheme: JwtBearerDefaults.AuthenticationScheme)
                    .AddJwtBearer(options =>
                    {
                        options.Audience = dataGatewayConfig.JwtAuth.Audience;
                        options.Authority = dataGatewayConfig.JwtAuth.Issuer;
                    });
            }

            services.AddAuthorization();
            services.AddSingleton<IAuthorizationHandler, RequestAuthorizationHandler>();
            services.AddControllers();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            // validate the configuration after the services have been built
            // but before the application is built
            app.ApplicationServices.GetService<IConfigValidator>()!.ValidateConfig();

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseHttpsRedirection();

            app.UseRouting();

            app.UseAuthentication();

            // Conditionally add EasyAuth middleware if no JwtAuth configuration supplied.
            // If EasyAuth not present, this will result in all requests being anonymous.
            if (app.ApplicationServices.GetService<DataGatewayConfig>()!.JwtAuth == null)
            {
                app.UseEasyAuth();
            }

            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                endpoints.MapBananaCakePop("/graphql");
            });
        }
    }
}
