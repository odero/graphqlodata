using System.Collections.Generic;

namespace graphqlodata.Middlewares
{
    internal class BatchRequestObject
    {
        public List<RequestObject> Requests { get; set; } = [];
    }
}
