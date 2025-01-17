using GraphQLParser.AST;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.OData.Edm;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace graphqlodata.Middlewares
{
    internal class BuildParts
    {
        public IList<string> SelectFields { get; init; }
        public IList<string> ExpandFields { get; init; }
        public IList<string> AliasFields { get; init; }
        public string QueryArgs { get; init; }
        public string KeySegment { get; init; }
    }

    public class GraphQLExpressionVisitor(
        IEdmModel model,
        IDictionary<string, object> variables,
        IDictionary<string, GraphQLFragmentDefinition> fragments)
    {
        internal RequestNodeInput VisitQuery(GraphQLOperationDefinition gqlQuery, out bool isBatch,
            IList<string> requestNames)
        {
            //visit nodes
            if (gqlQuery.SelectionSet.Selections.Count > 1)
            {
                var jsonRequest = BuildJsonBatchRequest(gqlQuery, requestNames);
                isBatch = true;

                return new RequestNodeInput
                {
                    Name = "$batch",
                    Body = jsonRequest,
                };
            }
            else
            {
                isBatch = false;
                var qryNode = (GraphQLField)gqlQuery.SelectionSet.Selections.Single();
                var nodeName = qryNode.Name.StringValue;
                requestNames.Add(nodeName);

                return BuildQueryOrFunction(qryNode);
            }
        }

        private RequestNodeInput BuildQueryOrFunction(GraphQLField queryNode)
        {
            var nodeName = queryNode.Name.StringValue;
            if (QueryIsFunctionType(model, nodeName))
            {
                var selectedFields = VisitRequestNode(queryNode, null, GQLRequestType.Function);
                var fullString = BuildSelectExpandUrl(selectedFields, GQLRequestType.Function);

                return new RequestNodeInput
                {
                    Name = $"{nodeName}({selectedFields.QueryArgs})",
                    QueryString = fullString,
                    RequestType = GQLRequestType.Function,
                };
            }

            if (QueryIsAggregation(nodeName))
            {
                var entitySetName = nodeName[..nodeName.LastIndexOf("_aggregate", StringComparison.Ordinal)];
                var entityType = model.EntityContainer.FindEntitySet(entitySetName).EntityType;
                var selectedFields = VisitRequestNode(queryNode, entityType, GQLRequestType.Aggregation);
                var fullString = BuildAggregationUrl(selectedFields);

                return new RequestNodeInput
                {
                    Name = entitySetName,
                    QueryString = fullString,
                    RequestType = GQLRequestType.Aggregation,
                };
            }
            else
            {
                var selectedFields =
                    VisitRequestNode(queryNode, model.EntityContainer.FindEntitySet(nodeName).EntityType);
                var fullString = BuildSelectExpandUrl(selectedFields);

                return new RequestNodeInput
                {
                    Name = queryNode.Name.StringValue,
                    QueryString = fullString,
                    RequestType = GQLRequestType.Query,
                };
            }
        }

        private string BuildAggregationUrl(BuildParts parts)
        {
            var aggregations = new List<string>();

            foreach (var field in parts.SelectFields)
            {
                var cleanedFieldName = GetAggregatorFieldWithAlias(parts, field);
                if (field == "_count")
                {
                    aggregations.Add($"$count as {cleanedFieldName}");
                    continue;
                }

                var (fieldName, aggregationType) = SplitAggregatedFieldName(field);

                aggregations.Add($"{fieldName} with {aggregationType} as {cleanedFieldName}");
            }

            var groupBy = parts.QueryArgs ?? string.Empty;
            var orderBy = string.Empty;

            if (groupBy.Contains(';'))
            {
                var queryParts = parts.QueryArgs.Split(';'); // format [group_by, order_by]
                groupBy = queryParts[0];
                orderBy = queryParts[1];
            }
            var aggregation = aggregations.Count != 0 ? $"aggregate({string.Join(",", aggregations)})" : string.Empty;

            if (string.IsNullOrEmpty(groupBy) && aggregations.Count == 0) return string.Empty;
            if (!string.IsNullOrEmpty(groupBy))
            {
                aggregation = $"groupby({string.Join(",", groupBy, aggregation)})";
            }

            if (!string.IsNullOrEmpty(orderBy))
            {
                aggregation += $"&{orderBy}";
            }

            var aggregationUrl = $"?$apply={aggregation}";
            return aggregationUrl;
        }

        private static string GetAggregatorFieldWithAlias(BuildParts parts, string field)
        {
            var aliasPrefix = $"{field} as";
            var aliasString = parts.AliasFields.FirstOrDefault(f =>
                f.StartsWith(aliasPrefix));

            return aliasString != null ? aliasString[(aliasPrefix.Length + 1)..] : field;
        }

        private readonly List<string> supportedAggregations =
            ["sum", "max", "min", "average", "countdistinct", "_count"];

        private (string, string) SplitAggregatedFieldName(string field)
        {
            var aggregation =
                supportedAggregations.FirstOrDefault(agg => field.EndsWith($"_{agg}"));
            var fieldName = field[..field.LastIndexOf($"_{aggregation}", StringComparison.Ordinal)];
            return (fieldName, aggregation);
        }

        private static bool QueryIsAggregation(string nodeName)
        {
            return nodeName.EndsWith("_aggregate");
        }

        private static string BuildSelectExpandUrl(BuildParts parts, GQLRequestType requestType = GQLRequestType.Query)
        {
            var selectString = BuildSelectFromParts(parts.SelectFields);
            var aliasString = BuildAliasFromParts(parts.AliasFields);
            aliasString = string.IsNullOrEmpty(aliasString) ? aliasString : $"&{aliasString}";
            var expandString = BuildExpandFromParts(parts.ExpandFields);
            var filterString = requestType == GQLRequestType.Query && !string.IsNullOrEmpty(parts.QueryArgs)
                ? "&" + parts.QueryArgs
                : "";
            return $"?$select={selectString}{aliasString}{expandString}{filterString}";
        }

        private static string BuildAliasFromParts(IList<string> aliases)
        {
            return aliases.Count == 0 ? string.Empty : $"$compute={string.Join(",", aliases)}";
        }

        private (string segments, string args) VisitArgs(GraphQLArguments args, GQLRequestType requestType)
        {
            if (args == null)
            {
                return (null, "");
            }

            var kvPairs = new Dictionary<string, object>();
            // Query args simple case is stringKey with primitive values
            var filterArgs = new List<string>();
            var orderByArgs = new List<string>();
            var groupByArgs = new List<string>();
            var keywordArgs = new Dictionary<string, string>();
            var mutationBody = default(string);
            var keySegment = default(string);

            foreach (var arg in args)
            {
                object argValue;
                if (arg.Value is GraphQLVariable gqlVariable)
                {
                    argValue = variables[gqlVariable.Name.StringValue].ToString();
                }
                else
                {
                    argValue = arg.Value;
                }

                switch (requestType)
                {
                    case GQLRequestType.Query:
                    case GQLRequestType.Aggregation:
                        VisitQueryRequest(arg, filterArgs, orderByArgs, groupByArgs, argValue, keywordArgs);
                        break;
                    case GQLRequestType.Function:
                    {
                        VisitFunctionRequest(arg, filterArgs, argValue);
                        break;
                    }
                    case GQLRequestType.Mutation:
                    {
                        keySegment = VisitMutationRequest(arg, keySegment, argValue, kvPairs, ref mutationBody);

                        break;
                    }
                    case GQLRequestType.Subscription:
                    case GQLRequestType.Action:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(requestType), requestType, null);
                }
            }


            switch (requestType)
            {
                case GQLRequestType.Query:
                {
                    var keywordString = new QueryBuilder(keywordArgs).ToString().Trim('?');
                    var filterString = filterArgs.Count > 0 ? "$filter=" + string.Join(" and ", filterArgs) : "";
                    var orderByString = orderByArgs.Count > 0 ? "$orderBy=" + string.Join(",", orderByArgs) : "";
                    return (null,
                        string.Join("&",
                            new[] { filterString, keywordString, orderByString }.Where(s =>
                                !string.IsNullOrEmpty(s))));
                }
                case GQLRequestType.Function:
                    return (null, filterArgs.Count > 0 ? string.Join(",", filterArgs) : "");
                // case GQLRequestType.Mutation when kvPairs.Keys.Count > 0:
                //     return (null, JsonConvert.SerializeObject(kvPairs));
                case GQLRequestType.Mutation:
                    return (keySegment, mutationBody);
                case GQLRequestType.Aggregation:
                {
                    var orderByString = orderByArgs.Count > 0 ? "$orderBy=" + string.Join(",", orderByArgs) : "";
                    var groupByString = $"({string.Join(",", groupByArgs)})";
                    return (null, string.Join(";", new[] { groupByString, orderByString }));
                }
                case GQLRequestType.Subscription:
                case GQLRequestType.Action:
                default:
                    return (null, "");
            }
        }

        private static void VisitAggregationRequest(GraphQLArgument argument, List<string> groupByArgs)
        {
            var groupByValues = (GraphQLListValue)argument.Value;
            foreach (var value in groupByValues.Values?.Select(val => (GraphQLEnumValue)val) ?? [])
            {
                groupByArgs.Add(value.Name.StringValue);
            }
        }

        private string VisitMutationRequest(GraphQLArgument arg, string keySegment, object argValue,
            Dictionary<string, object> kvPairs,
            ref string mutationBody)
        {
            switch (arg.Value)
            {
                case GraphQLObjectValue obj when arg.Name.Value == "key" || arg.Name.Value == "id":
                    // TODO: extract key value
                    keySegment = VisitKeySegment(obj);
                    break;
                case GraphQLObjectValue obj:
                    mutationBody = VisitInputObject(obj, escapeDoubleQuotes: true);
                    break;
                case GraphQLIntValue intValue when arg.Name.Value == "key" || arg.Name.Value == "id":
                    keySegment = GetValue(intValue).ToString();
                    break;
                case GraphQLStringValue stringValue when arg.Name.Value == "key" || arg.Name.Value == "id":
                {
                    keySegment = stringValue.ToString();
                    // kvPairs[arg.Name.StringValue] = mutationBody ?? GetValue(arg.Value);
                    break;
                }
            }

            if (arg.Value.Kind == ASTNodeKind.Variable && arg.Name.Value == "input")
            {
                mutationBody = argValue.ToString();
            }

            // todo: probably dont want this. just use single input object instead
            if (mutationBody != null)
                kvPairs[arg.Name.StringValue] = JsonConvert.DeserializeObject(mutationBody);

            return keySegment;
        }

        private static object GetValue(object argValue)
        {
            if (argValue is GraphQLValue value)
            {
                return value.Kind switch
                {
                    ASTNodeKind.IntValue => long.Parse(((GraphQLIntValue)value).Value.ToString()),
                    ASTNodeKind.FloatValue => float.Parse(((GraphQLFloatValue)value).Value.ToString()),
                    ASTNodeKind.BooleanValue => ((GraphQLBooleanValue)value).Value,
                    ASTNodeKind.StringValue => ((GraphQLStringValue)value).Value.ToString().Trim('"'),
                    _ => value.ToString(),
                };
            }

            return argValue.ToString()?.Trim('"');
        }

        private void VisitFunctionRequest(GraphQLArgument arg, List<string> filterArgs, object argValue)
        {
            //todo: allow passing object types
            // args could be treated as func args
            if (arg.Value.Kind is ASTNodeKind.ObjectValue or ASTNodeKind.ListValue)
            {
                filterArgs.Add($"{arg.Name.Value}={VisitInputObject(argValue as GraphQLValue)}");
            }
            else
            {
                argValue = argValue is string
                    ? argValue
                    : ((GraphQLValue)argValue).Kind == ASTNodeKind.StringValue
                        ? $"'{((GraphQLStringValue)argValue).Value.ToString().Trim('"')}'"
                        : argValue;
                filterArgs.Add($"{arg.Name.Value}={argValue}");
            }
        }

        private void VisitQueryRequest(GraphQLArgument arg, List<string> filterArgs, List<string> orderByArgs,
            List<string> groupByArgs,
            object argValue,
            Dictionary<string, string> keywordArgs)
        {
            switch (arg.Value.Kind)
            {
                // args could be treated as filter/top/orderby
                case ASTNodeKind.ObjectValue when arg.Name.Value == "filter" || arg.Name.Value == "where":
                {
                    filterArgs.Add(VisitFilterObject(arg.Value));
                    break;
                }
                case ASTNodeKind.ListValue when arg.Name.Value == "order_by":
                {
                    orderByArgs.Add(VisitOrderByObject(arg.Value));
                    break;
                }
                case ASTNodeKind.ListValue when arg.Name.Value == "group_by":
                {
                    VisitAggregationRequest(arg, groupByArgs);
                    break;
                }
                default:
                {
                    var argName = arg.Name.StringValue.ToLowerInvariant();
                    if (QueryOptionMapper.Options.ContainsKey(argName))
                    {
                        var remapped = QueryOptionMapper.Remap(argName, GetValue(argValue).ToString());
                        remapped.ToList().ForEach(kv => keywordArgs[kv.Key] = kv.Value);
                    }
                    else
                    {
                        //VisitFilterObject
                        filterArgs.Add($"{arg.Name.Value} eq {VisitFilterObject(arg.Value)}");
                    }

                    break;
                }
            }
        }

        private string VisitKeySegment(GraphQLValue value, bool singleQuoteStrings = false)
        {
            switch (value)
            {
                case GraphQLObjectValue obj:
                    return string.Join(
                        ",",
                        obj.Fields?.Select(fld => VisitKeySegment(fld.Value, singleQuoteStrings)) ?? []
                    );
                case GraphQLVariable gqlVariable:
                {
                    var varValue = variables[gqlVariable.Name.StringValue];
                    return singleQuoteStrings && varValue is string ? $"'{varValue}'" : $"{varValue}";
                }
                default:
                {
                    if (value.Kind == ASTNodeKind.StringValue && singleQuoteStrings)
                    {
                        return $"'{value.ToString()?.Trim('"')}'";
                    }
                    else
                    {
                        return value.ToString();
                    }
                }
            }
        }

        private static string VisitOrderByObject(GraphQLValue value)
        {
            var orderValues = value as GraphQLListValue;
            if (orderValues?.Values is null) return string.Empty;

            var items = new List<string>(orderValues.Values.Count);
            foreach (var field in orderValues.Values.Select(item => ((GraphQLEnumValue)item).Name.ToString().Trim('"')))
            {
                if (field.EndsWith("_desc"))
                {
                    items.Add($"{field[..^5]} desc");
                }
                else
                {
                    var sortField = field.EndsWith("_asc")
                        ? field[..^4]
                        : field;
                    items.Add($"{sortField} asc");
                }
            }

            return string.Join(",", items);
        }

        private string VisitInputObject(GraphQLValue value, bool singleQuoteStrings = false,
            bool escapeDoubleQuotes = false)
        {
            switch (value)
            {
                case GraphQLObjectValue obj:
                {
                    var res = string.Concat(
                        "{",
                        string.Join(
                            ",",
                            obj.Fields?.Select(fld =>
                                $"\"{fld.Name.Value}\": {VisitInputObject(fld.Value, singleQuoteStrings, escapeDoubleQuotes)}") ??
                            []
                        ),
                        "}"
                    );
                    return res;
                }
                case GraphQLListValue listValue:
                    return string.Join(
                        ",",
                        listValue.Values?.Select(val =>
                            VisitInputObject(val, singleQuoteStrings, escapeDoubleQuotes)) ??
                        []
                    );
                case GraphQLVariable gqlVariable:
                {
                    var varValue = variables[gqlVariable.Name.StringValue];
                    return singleQuoteStrings && varValue is string ? $"'{varValue}'" : $"{varValue}";
                }
                default:
                {
                    return value.Kind switch
                    {
                        ASTNodeKind.StringValue when escapeDoubleQuotes =>
                            $"\"{GetValue(value).ToString()?.Trim('"')}\"",
                        ASTNodeKind.StringValue when singleQuoteStrings => $"'{GetValue(value).ToString()?.Trim('"')}'",
                        _ => GetValue(value).ToString()
                    };
                }
            }
        }

        private string VisitFilterObject(GraphQLValue value, string op = "AND")
        {
            object argValue;
            var queries = new List<string>();

            if (value is GraphQLVariable gqlVariable)
            {
                argValue = variables[gqlVariable.Name.StringValue];
            }
            else
            {
                argValue = value;
            }

            string query;
            switch (argValue)
            {
                case GraphQLObjectValue objValue:
                {
                    foreach (var field in objValue.Fields ?? [])
                    {
                        switch (field.Name.StringValue.ToUpper())
                        {
                            case "OR":
                            case "AND":
                                queries.Add(VisitFilterObject(field.Value, field.Name.StringValue));
                                break;
                            default:
                                queries.Add(VisitStringFilter(field));
                                break;
                        }
                    }

                    query = string.Join(" AND ", queries);
                    break;
                }
                case GraphQLListValue listValue:
                {
                    var parts = new List<string>();
                    foreach (var item in listValue.Values ?? [])
                    {
                        parts.Add(VisitFilterObject(item));
                    }

                    query = string.Join($" {op} ", parts);
                    break;
                }
                default:
                    query = GetValue(argValue).ToString();
                    break;
            }

            return query;
        }

        private string VisitStringFilter(GraphQLObjectField field)
        {
            var fieldName = field.Name.StringValue;
            if (fieldName.EndsWith("_in"))
            {
                object fieldValue;
                if (field.Value is GraphQLVariable qLVariable)
                {
                    fieldValue = variables[qLVariable.Name.StringValue];
                }
                else
                {
                    fieldValue = field.Value;
                }

                if (fieldValue is JArray enumList)
                {
                    return string.Concat(
                        fieldName[..fieldName.LastIndexOf("_in", StringComparison.Ordinal)],
                        " in (",
                        string.Join(',', enumList.Select(v => v.Type == JTokenType.String ? $"'{v}'" : v)),
                        ")"
                    );
                }

                if (field.Value is GraphQLListValue valueList)
                    return string.Concat(
                        fieldName[..^3],
                        " in (",
                        string.Join(',',
                            valueList.Values?.Select(v => VisitInputObject(v, singleQuoteStrings: true)) ?? []),
                        ")"
                    );
            }

            var value = GetValue(field.Value);

            if (fieldName.EndsWith("_contains"))
            {
                return $"contains({fieldName[..^9]}, '{value}')";
            }

            if (fieldName.EndsWith("_startswith"))
            {
                return $"startswith({fieldName[..^11]}, '{value}')";
            }

            if (fieldName.EndsWith("_endswith"))
            {
                return $"endswith({fieldName[..^9]}, '{value}')";
            }

            return VisitLogicalFilter(field);
        }

        private string VisitLogicalFilter(GraphQLObjectField field)
        {
            string value;
            if (field.Value is null || field.Name is null) throw new ArgumentNullException(nameof(field));

            if (field.Value is GraphQLVariable qLVariable)
            {
                var varValue = variables[qLVariable.Name.StringValue];
                value = varValue is string ? $"'{varValue.ToString()?.Trim('"')}'" : varValue.ToString();
            }
            else
            {
                value = GetValue(field.Value).ToString();
            }

            var fieldName = field.Name.StringValue;

            if (fieldName.EndsWith("_gt"))
            {
                return $"{fieldName[..^3]} gt {value}";
            }

            if (fieldName.EndsWith("_gte"))
            {
                return $"{fieldName[..^4]} ge {value}";
            }

            if (fieldName.EndsWith("_lt"))
            {
                return $"{fieldName[..^3]} lt {value}";
            }

            if (fieldName.EndsWith("_lte"))
            {
                return $"{fieldName[..^4]} le {value}";
            }

            return $"{fieldName} eq {value}";
        }

        private BuildParts VisitRequestNode(GraphQLField fieldSelection, IEdmStructuredType structuredType,
            GQLRequestType requestType = GQLRequestType.Query)
        {
            var nodeFields = new List<string>();
            var expandItems = new List<string>();
            var aliasFields = new List<string>();

            foreach (var node in fieldSelection.SelectionSet?.Selections ?? [])
            {
                switch (node)
                {
                    case GraphQLFragmentSpread fragField:
                    {
                        var frag = fragments[fragField.FragmentName.Name.StringValue];
                        var fields = frag.SelectionSet.Selections.OfType<GraphQLField>();
                        foreach (var fld in fields)
                        {
                            VisitField(fieldSelection, structuredType, requestType, fld, nodeFields, expandItems,
                                aliasFields);
                        }

                        break;
                    }
                    case GraphQLField field:
                    {
                        VisitField(fieldSelection, structuredType, requestType, field, nodeFields, expandItems,
                            aliasFields);
                        break;
                    }
                }
            }

            var (keySegment, argString) = VisitArgs(fieldSelection.Arguments, requestType);

            return new BuildParts
            {
                QueryArgs = argString,
                ExpandFields = expandItems,
                SelectFields = nodeFields,
                AliasFields = aliasFields,
                KeySegment = keySegment,
            };
        }

        private void VisitField(GraphQLField fieldSelection, IEdmStructuredType structuredType,
            GQLRequestType requestType,
            GraphQLField field, List<string> nodeFields, List<string> expandItems, List<string> aliasFields)
        {
            if (field.SelectionSet?.Selections.Any() == true)
            {
                // todo: handling different kinds of nav props - single nav/multi nav/complex type
                if (structuredType?
                        .NavigationProperties()?
                        .FirstOrDefault(p => p.Name.Equals(GetValue(field.Name.Value).ToString(),
                            StringComparison.OrdinalIgnoreCase))?
                        .ToEntityType() is IEdmStructuredType navPropType)
                {
                    // must be a nav prop which requires expand
                    var buildParts = VisitRequestNode(field, navPropType);

                    if (structuredType.TypeKind == EdmTypeKind.Complex)
                    {
                        nodeFields.Add($"{fieldSelection.Name.Value}/{field.Name.Value}");
                        expandItems.Add(
                            $"{fieldSelection.Name.Value}/{field.Name.Value}($select={BuildSelectFromParts(buildParts.SelectFields)})");
                    }
                    else
                    {
                        var select = BuildSelectFromParts(buildParts.SelectFields, buildParts.QueryArgs);
                        var aliases = BuildAliasFromParts(buildParts.AliasFields);
                        aliases = string.IsNullOrEmpty(aliases) ? aliases : $";{aliases}";
                        expandItems.Add(
                            $"{field.Name.Value}($select={select}{aliases})");
                    }

                    return;
                }

                // must be a complex type/single prop which is accessed by path
                var propType = structuredType.StructuralProperties()
                    ?.FirstOrDefault(p => p?.Name == GetValue(field.Name.Value).ToString())?.Type;

                if (propType?.IsComplex() == true || propType?.IsCollection() == true)
                {
                    var structType = propType.ToStructuredType();
                    var parts = VisitRequestNode(field, structType);
                    nodeFields.AddRange(parts.SelectFields);
                    expandItems.AddRange(parts.ExpandFields);
                    return;
                }
            }

            var (visitedField, alias) = VisitNodeFields(field);
            if (structuredType?.TypeKind == EdmTypeKind.Complex)
            {
                visitedField = $"{fieldSelection.Name.Value}/{visitedField}";
            }
            else
            {
                if (alias != null)
                {
                    aliasFields.Add($"{visitedField} as {alias}");
                }
            }

            if (requestType == GQLRequestType.Aggregation)
                nodeFields.Add(visitedField);
            else
                nodeFields.Add(alias ?? visitedField);
        }

        private static string BuildSelectFromParts(IList<string> parts, string argString = null)
        {
            var selectFieldString = string.Join(",", parts);
            var fullSelectString = string.Join(";",
                new[] { selectFieldString, argString }.Where(s => !string.IsNullOrEmpty(s)));
            return fullSelectString;
        }

        private static string BuildExpandFromParts(IList<string> parts)
        {
            var expandString = parts.Count == 0
                ? string.Empty
                : $"&$expand={string.Join(",", parts.Where(s => !string.IsNullOrEmpty(s)))}";
            return expandString;
        }

        private static (string name, string alias) VisitNodeFields(GraphQLField fieldSelection)
        {
            return (fieldSelection.Name.StringValue, fieldSelection.Alias?.Name.ToString());
        }

        internal RequestNodeInput VisitMutation(GraphQLOperationDefinition gqlMutation, out bool isBatch,
            IList<string> requestNames)
        {
            if (gqlMutation.SelectionSet.Selections.Count > 1)
            {
                //todo: add requestNames param here
                var jsonRequest = BuildJsonBatchRequest(gqlMutation, requestNames);
                isBatch = true;
                return new RequestNodeInput
                {
                    Name = "/$batch",
                    Body = jsonRequest,
                };
            }
            else
            {
                isBatch = false;
                return BuildMutationRequestContext((GraphQLField)gqlMutation.SelectionSet.Selections.Single(),
                    requestNames);
            }
        }

        private RequestNodeInput BuildMutationRequestContext(GraphQLField mutationNode,
            IList<string> requestNames = null)
        {
            //todo: mutation can return both the method call + select fields. Consider abstracting by interface. Need to return full path + select fields
            var requestInput = VisitRequestNode(mutationNode, null, GQLRequestType.Mutation);
            var (nodeName, method) = ExtractNodeNameAndMethod(mutationNode?.Name.StringValue);
            requestNames?.Add(nodeName);

            var isAction = MutationIsActionType(model, nodeName);
            var fullString = isAction ? BuildSelectExpandUrl(requestInput, GQLRequestType.Mutation) : "";

            var res = new RequestNodeInput
            {
                Name = requestInput.KeySegment is null ? nodeName : $"{nodeName}({requestInput.KeySegment})",
                Method = method,
                QueryString = fullString,
                Body = requestInput.QueryArgs,
                RequestType = isAction ? GQLRequestType.Action : GQLRequestType.Mutation,
            };
            return res;
        }

        private static (string, string) ExtractNodeNameAndMethod(string nodeName)
        {
            if (nodeName.EndsWith("_add")) return (nodeName[..^4], "POST");
            if (nodeName.EndsWith("_update")) return (nodeName[..^7], "PATCH");
            if (nodeName.EndsWith("_delete")) return (nodeName[..^7], "DELETE");
            return (nodeName, "POST");
        }

        private string BuildJsonBatchRequest(GraphQLOperationDefinition graphQlOperationDefinition,
            IList<string> requestNames)
        {
            var selectionSet = graphQlOperationDefinition.SelectionSet;
            var batchRequest = new BatchRequestObject();

            switch (graphQlOperationDefinition.Operation)
            {
                case OperationType.Query:
                {
                    foreach (var qryNode in selectionSet.Selections.OfType<GraphQLField>()
                                 .Select((value, index) => (index, value)))
                    {
                        var nodeName = qryNode.value.Name.StringValue;
                        requestNames.Add(nodeName);

                        //todo: check if query/mutation to determine request method - not possible in graphql to combine query and mutation in same request
                        RequestNodeInput requestInput = BuildQueryOrFunction(qryNode.value);
                        requestInput.QueryString = $"{requestInput.Name}{requestInput.QueryString}";

                        batchRequest.Requests.Add(new RequestObject
                        {
                            Id = $"{qryNode.index + 1}",
                            Method = "GET",
                            Url = requestInput.QueryString,
                            Body = "", // avoid passing null for get requests
                            Headers = new Dictionary<string, string>
                            {
                                { "accept", "application/json; odata.metadata=none; odata.streaming=true" },
                                { "odata-version", "4.0" },
                            }
                        });
                    }

                    break;
                }
                case OperationType.Mutation:
                {
                    foreach (var qryNode in selectionSet.Selections.OfType<GraphQLField>()
                                 .Select((value, index) => (index, value)))
                    {
                        var nodeName = qryNode.value.Alias?.Name.StringValue ?? qryNode.value.Name.StringValue;
                        requestNames.Add(nodeName);

                        //todo: check if query/mutation to determine request method - not possible in graphql to combine query and mutation in same request
                        var requestInput = BuildMutationOrAction(qryNode.value);
                        requestInput.QueryString = $"{requestInput.Name}{requestInput.QueryString}";
                        var batchObject = new RequestObject
                        {
                            Id = $"{qryNode.index + 1}",
                            Method = requestInput.Method,
                            Url = requestInput.QueryString,
                            Body = JsonConvert.DeserializeObject(requestInput.Body ?? "{}"),
                            Headers = new Dictionary<string, string>
                            {
                                { "accept", "application/json; odata.metadata=none; odata.streaming=true" },
                                { "content-type", "application/json" },
                                { "odata-version", "4.0" },
                            }
                        };
                        batchRequest.Requests.Add(batchObject);
                    }

                    break;
                }
            }

            var res = JsonConvert.SerializeObject(batchRequest);
            return res;
        }

        private RequestNodeInput BuildMutationOrAction(GraphQLField queryNode)
        {
            var resp = BuildMutationRequestContext(queryNode);
            return resp;
        }

        private static bool QueryIsFunctionType(IEdmModel model, string itemName)
        {
            return model.EntityContainer.FindOperationImports(itemName).Any();
        }

        private static bool MutationIsActionType(IEdmModel model, string itemName)
        {
            return QueryIsFunctionType(model, itemName);
        }
    }
}