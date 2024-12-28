using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace graphqlodata.Handlers
{
    public class ResponseHandler : IGraphQLODataResponseHandler
    {
        public async Task UpdateResponseBody(HttpResponse res, Stream existingBody, IList<string> requestNames)
        {
            string newContent;
            res.Body.Position = 0;
            if (res.StatusCode is < 200 or > 207)
            {
                //if status is not 2xx then we need to package the error
                newContent = PackageError(res);
                if (string.IsNullOrEmpty(res.ContentType))
                {
                    //this happens with a 404
                    res.ContentType = "application/json";
                }
                res.StatusCode = 200;
            }
            else
            {
                newContent = ReformatResponse(await new StreamReader(res.Body).ReadToEndAsync(), requestNames);
            }
            res.Body = existingBody; // because this must be type HttpResponseStream that's internal to Kestrel
            await res.WriteAsync(newContent);
        }

        private static string PackageError(HttpResponse res)
        {
            var errorBody = res.Body.Length > 0
                ? JObject.Parse(new StreamReader(res.Body).ReadToEnd()).SelectToken("error")
                : JObject.FromObject(new { message = res.StatusCode });
            var errorRes = JsonConvert.SerializeObject(
                new Dictionary<string, object>
                {
                    { "errors", new List<JToken> { errorBody } },
                }
            );
            res.Body.Position = 0;
            return errorRes;
        }

        private static string ReformatResponse(string currentResponse, IList<string> gqlQueryNames)
        {
            // if key is value; means single query returning entityset
            // if key is responses; means batch query
            // else single object query returning single object/entity
            var parsed = JObject.Parse(currentResponse);
            var obj = new JObject();
            if (parsed.SelectToken("responses") is { } token)
            {
                for (var i = 0; i < token.Count(); i++)
                {
                    var body = token[i]?["body"];
                    obj.Add(gqlQueryNames[i], body?.SelectToken("value") ?? body);
                }
            }
            else
            {
                obj.Add(gqlQueryNames.First(), parsed.SelectToken("value") ?? parsed);
            }
            var finalRes = JObject.FromObject(new { data = obj });
            return finalRes.ToString();
        }
    }
}
