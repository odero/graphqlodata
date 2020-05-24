using System.Collections.Generic;

namespace graphqlodata.Middlewares
{
    class BatchRequestObject
    {
        public List<RequestObject> Requests { get; set; } = new List<RequestObject>();
    }
}
