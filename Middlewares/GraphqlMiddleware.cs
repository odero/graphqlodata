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
        private readonly Lazy<IEdmModel> _model;

        public GraphqlODataMiddleware(RequestDelegate next)
        {
            _next = next;
            _model = new Lazy<IEdmModel>(Helper.ReadModel);
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var req = context.Request;

            if (!req.Path.StartsWithSegments("/odata/graphql"))
            {
                await _next(context);
                return;
            }

            var requestHandler = new RequestHandler(req);
            var responseHandler = new ResponseHandler();
            var requestNames = new List<string>();

            Helper.ODataSchemaPath = $"{req.Scheme}://{req.Host.Value}{req.Path.Value.Substring(0, req.Path.Value.IndexOf("/graphql"))}/$metadata";
            var parsed = await requestHandler.TryParseRequest(requestNames, _model.Value);

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
