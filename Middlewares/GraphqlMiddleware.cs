using graphqlodata.Handlers;
using Microsoft.AspNetCore.Http;
using Microsoft.OData.Edm;
using System;
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

        public GraphqlODataMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(
            HttpContext context,
            IGraphQLODataRequestHandler requestHandler,
            IGraphQLODataResponseHandler responseHandler,
            IODataGraphQLSchemaConverter converter
            )
        {
            var req = context.Request;
            
            if (!req.Path.StartsWithSegments("/odata/$graphql"))
            {
                await _next(context);
                return;
            }

            requestHandler.Request = context.Request;
            var requestNames = new List<string>();

            converter.ODataSchemaPath = $"{req.Scheme}://{req.Host.Value}{req.Path.Value.Substring(0, req.Path.Value.IndexOf("/$graphql"))}/$metadata";

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
            await responseHandler.UpdateResponseBody(context.Response, originalBody, requestNames);
        }
    }
}
