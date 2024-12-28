using graphqlodata.Middlewares;
using Microsoft.AspNetCore.Http;
using Microsoft.OData.Edm;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace graphqlodata.Handlers
{
    public class RequestHandler : IGraphQLODataRequestHandler
    {
        public HttpRequest Request { get; set; }

        public async Task<bool> TryParseRequest(IList<string> requestNames, IEdmModel model)
        {
            var queryString = await ReadRequest();
            if (string.IsNullOrWhiteSpace(queryString))
            {
                return false;
            }
            var parser = new RequestParser(this, model, queryString);
            await parser.ConvertGraphQLtoODataQuery(Request, parser.Query, requestNames);
            return true;
        }

        private async Task<string> ReadRequest()
        {
            Request.EnableBuffering();
            var graphQLQuery = Request.Query["query"].ToString();
            if (Request.Method == "POST")
            {
                graphQLQuery = await ReadRequestBody();
            }
            return graphQLQuery;
        }


        private async Task<string> ReadRequestBody()
        {
            using var reader = new StreamReader(Request.Body, Encoding.UTF8, true, 1024, true);
            var body = await reader.ReadToEndAsync();
            Request.Body.Position = 0;
            return body;
        }

        public async Task RewriteRequestBody(HttpRequest req, string payload)
        {
            var reqBytes = Encoding.UTF8.GetBytes(payload);
            req.Headers.Accept = "application/json;odata.metadata=none;";
            //req.Method = "POST"; ??
            req.Headers.ContentType = "application/json;odata.metadata=none;";
            req.Body = new MemoryStream();
            await req.Body.WriteAsync(reqBytes);
            req.Body.Position = 0;
        }

    }
}
