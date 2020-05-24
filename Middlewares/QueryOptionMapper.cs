using System;
using System.Collections.Generic;

namespace graphqlodata.Middlewares
{
    public class QueryOptionMapper
    {
        private static readonly Dictionary<string, Func<string, Dictionary<string, string>>> _options = new Dictionary<string, Func<string, Dictionary<string, string>>>
        {
            //todo: check if we need to ignore $ prefix
            { "first", v => new Dictionary<string, string> { { "$top", v } } },
            //todo: how to determine which field to order by
            { "last", v => new Dictionary<string, string> { { "$top", v }, { "$orderby", "id desc" } } },
        };
        public static Dictionary<string, string> Remap(string key, string value)
        {

            var hasMapping = _options.TryGetValue(key, out Func<string, Dictionary<string, string>> res);
            if (hasMapping)
            {
                return _options[key](value);
            }
            else
            {
                return new Dictionary<string, string> { { "$filter", $"{key} eq {value}" } };
            }
        }
    }
}
