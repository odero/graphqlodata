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
        Aggregation,
    }

    public class GraphqlODataMiddleware(RequestDelegate next)
    {
        public async Task InvokeAsync(
            HttpContext context,
            IGraphQLODataRequestHandler requestHandler,
            IGraphQLODataResponseHandler responseHandler,
            IODataGraphQLSchemaConverter converter
            )
        {
            var requestNames = new List<string>();

            var model = await converter.FetchSchema();
            ((RequestHandler)requestHandler).Request = context.Request;
            
            var parsed = await requestHandler.TryParseRequest(requestNames, model);

            if (!parsed)
            {
                await next(context);
                return;
            }

            var originalBody = context.Response.Body;
            context.Response.Body = new MemoryStream();  // set to MemoryStream so that it is seekable later on when reformatting the response

            await next(context);
            //response pipeline
            //TODO: should i pass request.method here for context?? patch returns no content by default
            await responseHandler.UpdateResponseBody(context.Response, originalBody, requestNames);
        }
    }
}
