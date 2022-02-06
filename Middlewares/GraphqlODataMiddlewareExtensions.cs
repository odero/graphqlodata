using Microsoft.AspNetCore.Builder;

namespace graphqlodata.Middlewares
{
    public static class GraphqlODataMiddlewareExtensions
    {
        public static IApplicationBuilder UseGraphQLOData(this IApplicationBuilder builder, string odataRoutePrefix = "odata")
        {
            return builder.UseWhen(
                context => context.Request.Path == $"/{odataRoutePrefix}/$graphql",
                app => app.UseMiddleware<GraphqlODataMiddleware>()
            );
        }
    }
}
