using Microsoft.OData.Edm;
using System;
using System.Threading.Tasks;

namespace graphqlodata.Handlers
{
    public interface IODataGraphQLSchemaConverter
    {
        public string ODataSchemaPath { get; set; }

        Task<IEdmModel> FetchSchema();
    }
}