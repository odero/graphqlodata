using GraphQLParser.AST;
using Microsoft.AspNetCore.Http;
using Microsoft.OData.Edm;
using Microsoft.OData.Edm.Csdl;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Xml;

namespace graphqlodata.Middlewares
{
    [Obsolete("Functionality moved to ODataGraphQLSchemaConverter")]
    public class Helper
    {
        private static string _path = "https://localhost:5001/odata/$metadata";
        public static string ODataSchemaPath { get => _path; set => _path = value; }

        internal static IEdmModel ReadModel()
        {
            using var reader = XmlReader.Create(_path);
            CsdlReader.TryParse(reader, out var model, out var errors);
            return model;
        }
    }
}
