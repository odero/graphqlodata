using graphqlodata.Middlewares;
using Microsoft.AspNetCore.Http;
using Microsoft.OData.Edm;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace graphqlodata.Handlers
{
    public class RequestHandler : IGraphQLODataRequestHandler
    {
        private HttpRequest _req;

        public HttpRequest Request { get => _req; set => _req = value; }

        public async Task<bool> TryParseRequest(IList<string> requestNames, IEdmModel model)
        {
            var queryString = await ReadRequest();
            if (string.IsNullOrWhiteSpace(queryString))
            {
                return false;
            }
            var parser = new RequestParser(this, model, queryString);
            await parser.ConvertGraphQLtoODataQuery(_req, parser.Query, requestNames);
            return true;
        }

        private async Task<string> ReadRequest()
        {
            _req.EnableBuffering();
            var graphQLQuery = _req.Query["query"].ToString();
            if (_req.Method == "POST")
            {
                graphQLQuery = await ReadRequestBody();
            }
            return graphQLQuery;
        }


        private async Task<string> ReadRequestBody()
        {
            using StreamReader reader = new StreamReader(_req.Body, Encoding.UTF8, true, 1024, true);
            var body = await reader.ReadToEndAsync();
            _req.Body.Position = 0;
            return body;
        }

        public async Task RewriteRequestBody(HttpRequest req, string payload)
        {
            byte[] reqBytes = Encoding.UTF8.GetBytes(payload);
            req.Headers["accept"] = "application/json;odata.metadata=none;";
            req.Method = "POST";
            req.Body = new MemoryStream();
            await req.Body.WriteAsync(reqBytes, 0, reqBytes.Length);
            req.Body.Position = 0;
        }

    }
}
