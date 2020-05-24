using System.Collections.Generic;

namespace graphqlodata.Middlewares
{
    class RequestDescriptor
    {
        public string Path { get; set; }
        public string Args { get; set; }
        public GQLRequestType RequestType { get; set; }
        public IDictionary<string, string> Headers { get; set; }
    }
}
