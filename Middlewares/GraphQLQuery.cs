using System.Collections.Generic;

namespace graphqlodata.Middlewares
{
    class GraphQLQuery {
        public string Query { get; set; }
        public IDictionary<string, object> Variables { get; set; }
    }
}
