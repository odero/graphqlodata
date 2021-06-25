using Microsoft.AspNetCore.Http;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace graphqlodata.Handlers
{
    public interface IGraphQLODataResponseHandler
    {
        Task UpdateResponseBody(HttpResponse res, Stream existingBody, IList<string> requestNames);
    }
}