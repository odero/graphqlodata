
using graphqlodata.Handlers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class GraphQLODataService
    {
        public static IServiceCollection AddGraphQLOData(this IServiceCollection services)
        {
            services.AddScoped<IGraphQLODataRequestHandler, RequestHandler>();
            services.AddScoped<IGraphQLODataResponseHandler, ResponseHandler>();
            services.AddSingleton<IODataGraphQLSchemaConverter, ODataGraphQLSchemaConverter>();
            return services;
        }
    }
}
