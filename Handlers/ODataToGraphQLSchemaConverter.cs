using Microsoft.AspNetCore.Http;
using Microsoft.OData.Edm;
using Microsoft.OData.Edm.Csdl;
using System;
using System.Threading.Tasks;
using System.Xml;

namespace graphqlodata.Handlers
{
    public class ODataGraphQLSchemaConverter : IODataGraphQLSchemaConverter
    {
        private readonly Lazy<Task<IEdmModel>> _model;
        public readonly string _odataSchemaUri;

        public ODataGraphQLSchemaConverter(string odataSchemaUri, IHttpContextAccessor httpContextAccessor)
        {
            _model = new Lazy<Task<IEdmModel>>(ReadModelAsync);

            if (string.IsNullOrEmpty(odataSchemaUri))
            {
                var req = httpContextAccessor.HttpContext.Request;
                var odataPathPrefix = req.Path.Value.Substring(1, req.Path.Value.IndexOf("/$graphql"));
                _odataSchemaUri = $"{req.Scheme}://{req.Host.Value}/{odataPathPrefix}$metadata";
            }
            else
            {
                _odataSchemaUri = odataSchemaUri;
            }
        }

        public Task<IEdmModel> FetchSchema() => _model.Value;

        Task<IEdmModel> ReadModelAsync() => Task.Run(ReadModel);

        IEdmModel ReadModel()
        {
            using var reader = XmlReader.Create(_odataSchemaUri);
            CsdlReader.TryParse(reader, out var model, out var errors);
            return model;
        }
    }
}
