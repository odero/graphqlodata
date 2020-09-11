using Microsoft.AspNetCore.Http;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace graphqlodata.Middlewares
{
    public class ResponseHandler
    {
        internal async Task<string> UpdateResponseBody(HttpResponse res, Stream existingBody, IList<string> requestNames)
        {
            res.Body.Position = 0;
            var newContent = ReformatResponse(new StreamReader(res.Body).ReadToEnd(), requestNames);
            res.Body = existingBody; // because this must be type HttpResponseStream that's internal to Kestrel
            await res.WriteAsync(newContent);
            return default;
        }

        private string ReformatResponse(string currentResponse, IList<string> gqlQueryNames)
        {
            // if key is value; means single query returning entityset
            // if key is responses; means batch query
            // else single object query returning single object/entity
            var parsed = JObject.Parse(currentResponse);
            var obj = new JObject();
            if (parsed.SelectToken("responses") is JToken token)
            {
                for (var i = 0; i < token.Count(); i++)
                {
                    var body = token[i]["body"];
                    obj.Add(gqlQueryNames[i], body?.SelectToken("value") ?? body);
                }
            }
            else
            {
                obj.Add(gqlQueryNames.First(), parsed.SelectToken("value") ?? parsed);
            }
            JObject finalRes = JObject.FromObject(new { data = obj });
            return finalRes.ToString();
        }
    }
}
