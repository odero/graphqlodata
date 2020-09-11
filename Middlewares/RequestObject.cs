using System.Collections.Generic;

namespace graphqlodata.Middlewares
{
    class RequestObject
    {
        public string? Id { get; set; }
        public string Method { get; set; }
        public string Url { get; set; }
        public Dictionary<string, string> Headers { get; set; }
    }
}
