using graphqlodata.Handlers;
using Microsoft.AspNetCore.Http;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace graphqlodata.Middlewares
{
    enum GQLRequestType
    {
        Query = 1,
        Mutation,
        Subscription, //not supported
        Function,
        Action,
    }

    public class GraphqlODataMiddleware
    {
        private readonly RequestDelegate _next;

        public GraphqlODataMiddleware(RequestDelegate next) => _next = next;

        public async Task InvokeAsync(
            HttpContext context,
            IGraphQLODataRequestHandler requestHandler,
            IGraphQLODataResponseHandler responseHandler,
            IODataGraphQLSchemaConverter converter
            )
        {
            var requestNames = new List<string>();

            var _model = await converter.FetchSchema();
            var parsed = await requestHandler.TryParseRequest(requestNames, _model);

            if (!parsed)
            {
                await _next(context);
                return;
            }

            var originalBody = context.Response.Body;
            context.Response.Body = new MemoryStream();  // set to MemoryStream so that it is seekable later on when reformatting the response

            await _next(context);
            //response pipeline
            //TODO: should i pass request.method here for context?? patch returns no content by default
            await responseHandler.UpdateResponseBody(context.Response, originalBody, requestNames);
        }
    }
}
