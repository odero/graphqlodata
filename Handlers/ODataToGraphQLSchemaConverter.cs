using graphqlodata.Middlewares;
using Microsoft.OData.Edm;
using Microsoft.OData.Edm.Csdl;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Xml;

namespace graphqlodata.Handlers
{
    public class ODataGraphQLSchemaConverter : IODataGraphQLSchemaConverter
    {
        private string _path = "https://localhost:5001/odata/$metadata";
        private readonly Lazy<Task<IEdmModel>> _model;

        public string ODataSchemaPath { get => _path; set => _path = value; }

        public ODataGraphQLSchemaConverter()
        {
            _model = new Lazy<Task<IEdmModel>>(ReadModelAsync);
        }

        public Task<IEdmModel> FetchSchema()
        {
            return _model.Value;
        }

        Task<IEdmModel> ReadModelAsync()
        {
            return Task.Run(ReadModel);
        }

        IEdmModel ReadModel()
        {
            using var reader = XmlReader.Create(_path);
            CsdlReader.TryParse(reader, out var model, out var errors);
            return model;
        }
    }
}
