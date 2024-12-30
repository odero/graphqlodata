using System;
using System.Collections.Generic;

namespace graphqlodata.Middlewares
{
    public static class QueryOptionMapper
    {
        public static readonly Dictionary<string, Func<string, Dictionary<string, string>>> Options = new Dictionary<string, Func<string, Dictionary<string, string>>>
        {
            //todo: check if we need to ignore $ prefix
            { "first", v => new Dictionary<string, string> { { "$top", v } } },
            //todo: how to determine which field to order by
            { "last", v => new Dictionary<string, string> { { "$top", v }, { "$orderby", "id desc" } } },
        };

        public static Dictionary<string, string> Remap(string key, string value)
        {
            if (Options.TryGetValue(key, out Func<string, Dictionary<string, string>> res))
            {
                return res(value);
            }
            else
            {
                return new Dictionary<string, string> { { "$filter", $"{key} eq {value}" } };
            }
        }
    }
}
