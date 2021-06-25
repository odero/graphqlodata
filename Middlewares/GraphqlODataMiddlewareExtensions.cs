using Microsoft.AspNetCore.Builder;

namespace graphqlodata.Middlewares
{
    public static class GraphqlODataMiddlewareExtensions
    {
        public static IApplicationBuilder UseGraphQLOData(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<GraphqlODataMiddleware>();
        }
    }
}
