using Microsoft.AspNetCore.Http;
using Microsoft.OData.Edm;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace graphqlodata.Handlers
{
    public interface IGraphQLODataRequestHandler
    {
        Task RewriteRequestBody(HttpRequest req, string payload);
        Task<bool> TryParseRequest(IList<string> requestNames, IEdmModel model);
    }
}