using System.Collections.Generic;

namespace graphqlodata.Middlewares
{
    class RequestObject
    {
        public string? id { get; set; }
        public string method { get; set; }
        public string url { get; set; }
        public Dictionary<string, string> headers { get; set; }
    }
}
