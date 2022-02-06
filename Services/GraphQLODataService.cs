
using graphqlodata.Handlers;
using Microsoft.AspNetCore.Http;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class GraphQLODataService
    {
        public static IServiceCollection AddGraphQLOData(this IServiceCollection services, string metadataPath = null)
        {
            services.AddHttpContextAccessor();
            services.AddScoped<IGraphQLODataRequestHandler, RequestHandler>();
            services.AddScoped<IGraphQLODataResponseHandler, ResponseHandler>();
            services.AddSingleton<IODataGraphQLSchemaConverter, ODataGraphQLSchemaConverter>(provider =>
                new ODataGraphQLSchemaConverter(metadataPath, provider.GetRequiredService<IHttpContextAccessor>())
            );

            return services;
        }
    }
}
