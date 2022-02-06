using Microsoft.OData.Edm;
using System.Threading.Tasks;

namespace graphqlodata.Handlers
{
    public interface IODataGraphQLSchemaConverter
    {
        Task<IEdmModel> FetchSchema();
    }
}