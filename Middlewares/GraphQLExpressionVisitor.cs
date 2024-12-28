using GraphQLParser.AST;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.OData.Edm;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace graphqlodata.Middlewares
{
    class BuildParts
    {
        public IList<string> SelectFields { get; set; }
        public IList<string> ExpandFields { get; set; }
        public string QueryArgs { get; set; }
        public string KeySegment { get; set; }
    }

    public class GraphQLExpressionVisitor
    {
        private readonly IEdmModel _model;
        private readonly IDictionary<string, object> _variables;
        private readonly IDictionary<string, GraphQLFragmentDefinition> _fragments;

        public GraphQLExpressionVisitor(IEdmModel model, IDictionary<string, object> variables, IDictionary<string, GraphQLFragmentDefinition> fragments)
        {
            _model = model;
            _variables = variables;
            _fragments = fragments;
        }

        internal RequestNodeInput VisitQuery(GraphQLOperationDefinition gqlQuery, out bool isBatch, IList<string> requestNames)
        {
            //visit nodes
            if (gqlQuery.SelectionSet.Selections.Count > 1)
            {
                var jsonRequest = BuildJsonBatchRequest(gqlQuery.SelectionSet, requestNames);
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
            string nodeName = queryNode.Name.StringValue;
            if (QueryIsFunctionType(_model, nodeName))
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
            else
            {
                var selectedFields = VisitRequestNode(queryNode, _model.EntityContainer.FindEntitySet(nodeName).EntityType());
                var fullString = BuildSelectExpandUrl(selectedFields);

                return new RequestNodeInput
                {
                    Name = queryNode.Name.StringValue,
                    QueryString = fullString,
                    RequestType = GQLRequestType.Query,
                };
            }
        }

        private static string BuildSelectExpandUrl(BuildParts parts, GQLRequestType requestType = GQLRequestType.Query)
        {
            var selectString = BuildSelectFromParts(parts.SelectFields);
            var expandString = parts.ExpandFields.Any() ? "&$expand=" + BuildExpandFromParts(parts.ExpandFields) : "";
            var filterString = requestType == GQLRequestType.Query && !string.IsNullOrEmpty(parts.QueryArgs) ? "&" + parts.QueryArgs : "" ;
            return $"?$select={selectString}{expandString}{filterString}";
        }

        private (string segments, string args) VisitArgs(GraphQLArguments args, GQLRequestType requestType)
        {
            if (args == null)
            {
                return (null, "");
            }

            var argList = new List<string>();
            var kvPairs = new Dictionary<string, object>();
            // Query args simple case is stringKey with primitive values
            var filterArgs = new List<string>();
            var orderByArgs = new List<string>();
            var keywordArgs = new Dictionary<string, string>();
            var mutationBody = default(string);
            var keySegment = default(string);

            foreach (var arg in args)
            {
                object argValue = null;
                if (arg.Value is GraphQLVariable gqlVariable)
                {
                    argValue = _variables[gqlVariable.Name.StringValue].ToString();
                }
                else
                {
                    argValue = arg.Value;
                }

                if (requestType == GQLRequestType.Query)
                {
                    switch (arg.Value.Kind)
                    {
                        // args could be treated as filter/top/orderby
                        case ASTNodeKind.ObjectValue:
                        {
                            if (arg.Name.Value == "filter" || arg.Name.Value == "where")
                            {
                                filterArgs.Add(VisitFilterObject(arg.Value));
                            }

                            break;
                        }
                        case ASTNodeKind.ListValue:
                        {
                            if (arg.Name.Value == "orderBy")
                            {
                                orderByArgs.Add(VisitOrderByObject(arg.Value));
                            }

                            break;
                        }
                        default:
                        {
                            var argName = arg.Name.StringValue.ToLowerInvariant();
                            if (QueryOptionMapper.Options.ContainsKey(argName))
                            {
                                var remapped = QueryOptionMapper.Remap(argName, argValue.ToString());
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
                else if (requestType == GQLRequestType.Function)
                {
                    //todo: allow passing object types
                    // args could be treated as func args
                    if (arg.Value.Kind == ASTNodeKind.ObjectValue || arg.Value.Kind == ASTNodeKind.ListValue)
                    {
                        filterArgs.Add($"{arg.Name.Value}={VisitInputObject(argValue as GraphQLValue)}");
                    }
                    else
                    {
                        argValue = argValue is string || (argValue as GraphQLValue)?.Kind == ASTNodeKind.StringValue ? $"'{argValue.ToString().Trim('"')}'" : argValue;
                        filterArgs.Add($"{arg.Name.Value}={argValue}");
                    }
                }
                else if (requestType == GQLRequestType.Mutation)
                {
                    if (arg.Value is GraphQLObjectValue obj)
                    {
                        if (arg.Name.Value == "key")
                        {
                            // TODO: extract key value
                            keySegment = VisitKeySegment(obj);
                        }
                        else
                        {
                            mutationBody = VisitInputObject(obj, singleQuoteStrings: true);
                        }
                    }
                    else if (arg.Value.Kind == ASTNodeKind.Variable)
                    {
                        if (arg.Name.Value == "input")
                        {
                            mutationBody = argValue.ToString();
                        }
                        else
                        {
                            kvPairs[arg.Name.StringValue] = argValue.ToString().Trim('"');
                        }
                    }
                    else
                    {
                        // todo: probably dont want this. just use single input object instead
                        kvPairs[arg.Name.StringValue] = arg.Value.ToString().Trim('"');
                    }
                }
            }
            
            
            if (requestType == GQLRequestType.Query)
            {
                var keywordString = new QueryBuilder(keywordArgs).ToString().Trim('?');
                var filterString = filterArgs.Count > 0 ? "$filter=" + string.Join(" and ", filterArgs) : "";
                var orderByString = orderByArgs.Count > 0 ? "$orderBy=" + string.Join(",", orderByArgs) : "";
                return (null, string.Join("&", new string[] { filterString, keywordString, orderByString }.Where(s => !string.IsNullOrEmpty(s))));
            }
            else if (requestType == GQLRequestType.Function)
            {
                return (null, filterArgs.Count > 0 ? string.Join(",", filterArgs) : "");
            }
            else if (requestType == GQLRequestType.Mutation)
            {
                if (kvPairs.Keys.Count > 0)
                {
                    return (null, JsonConvert.SerializeObject(kvPairs));
                }
                return (keySegment, mutationBody);
            }
            return (null, "");
        }

        private string VisitKeySegment(GraphQLValue value, bool singleQuoteStrings = false)
        {
            if (value is GraphQLObjectValue obj)
            {
                return string.Join(
                    ",",
                    obj.Fields.Select(fld => VisitKeySegment(fld.Value, singleQuoteStrings))
                );
            }
            else if (value is GraphQLVariable gqlVariable)
            {
                var varValue = _variables[gqlVariable.Name.StringValue];
                return singleQuoteStrings && varValue is string ? $"'{varValue}'" : $"{varValue}";
            }
            else if (value.Kind == ASTNodeKind.StringValue && singleQuoteStrings)
            {
                return $"'{value.ToString().Trim('"')}'";
            }
            else
            {
                return value.ToString();
            }
        }

        private string VisitOrderByObject(GraphQLValue value)
        {
            var orderValues = value as GraphQLListValue;
            var items = new List<string>(orderValues.Values.Count);
            foreach (var item in orderValues.Values)
            {
                var field = item.ToString().Trim('"');
                if (field.EndsWith("_desc"))
                {
                    items.Add($"{field.Substring(0, field.LastIndexOf("_desc"))} desc");
                }
                else
                {
                    var sortField = field.LastIndexOf("_asc") == -1 ? field : field.Substring(0, field.LastIndexOf("_asc"));
                    items.Add($"{sortField} asc");
                }
            }
            return string.Join(",", items);
        }

        private string VisitInputObject(GraphQLValue value, bool singleQuoteStrings = false)
        {
            if (value is GraphQLObjectValue obj)
            {
                return string.Concat(
                    "{",
                    string.Join(
                        ",",
                        obj.Fields.Select(fld => $"\"{fld.Name.Value}\": {VisitInputObject(fld.Value, singleQuoteStrings)}")
                    ),
                    "}"
                );
            }
            else if (value is GraphQLListValue listValue)
            {
                return string.Join(
                    ",",
                    listValue.Values.Select(val => VisitInputObject(val, singleQuoteStrings))
                );
            }
            else if (value is GraphQLVariable gqlVariable)
            {
                var varValue = _variables[gqlVariable.Name.StringValue];
                return singleQuoteStrings && varValue is string ? $"'{varValue}'" : $"{varValue}";
            }
            else if (value.Kind == ASTNodeKind.StringValue && singleQuoteStrings)
            {
                return $"'{value.ToString().Trim('"')}'";
            }
            else
            {
                return value.ToString();
            }
        }

        private string VisitFilterObject(GraphQLValue value, string op="AND")
        {
            object argValue;
            var queries = new List<string>();

            if (value is GraphQLVariable gqlVariable)
            {
                argValue = _variables[gqlVariable.Name.StringValue];
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
                    foreach (var field in objValue.Fields)
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
                    foreach (var item in listValue.Values)
                    {
                        parts.Add(VisitFilterObject(item));
                    }
                    query = string.Join($" {op} ", parts);
                    break;
                }
                default:
                    query = argValue is string || (argValue as GraphQLValue)?.Kind == ASTNodeKind.StringValue ? $"'{argValue.ToString().Trim('"')}'" : argValue.ToString();
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
                    fieldValue = _variables[qLVariable.Name.StringValue];
                }
                else
                {
                    fieldValue = field.Value;
                }
                
                if (fieldValue is JArray enumList)
                {
                    return string.Concat(
                        fieldName.Substring(0, fieldName.LastIndexOf("_in")),
                        " in (",
                        string.Join(',', enumList.Select(v => v.Type == JTokenType.String ? $"'{v}'" : v)),
                        ")"
                    );
                }
                else if (field.Value is GraphQLListValue valueList)
                {
                    return string.Concat(
                        fieldName.Substring(0, fieldName.LastIndexOf("_in")),
                        " in (",
                        string.Join(',', valueList.Values.Select(v => VisitInputObject(v, singleQuoteStrings: true))),
                        ")"
                    );
                }
                //var valueList = field.Value as GraphQLListValue;

            }

            var value = field.Value.Kind switch
            {
                ASTNodeKind.StringValue => ((GraphQLStringValue)field.Value).Value.ToString().Trim('"'),
                ASTNodeKind.IntValue => ((GraphQLIntValue)field.Value).Value.ToString(),
                _ => field.Value.ToString()
            };
            
            if (fieldName.EndsWith("_contains"))
            {
                return $"contains({fieldName[..fieldName.LastIndexOf("_contains", StringComparison.Ordinal)]}, '{value}')";
            }
            else if (fieldName.EndsWith("_startswith"))
            {
                return $"startswith({fieldName[..fieldName.LastIndexOf("_startswith", StringComparison.Ordinal)]}, '{value}')";
            }
            else if (fieldName.EndsWith("_endswith"))
            {
                return $"endswith({fieldName[..fieldName.LastIndexOf("_endswith", StringComparison.Ordinal)]}, '{value}')";
            }
            return VisitLogicalFilter(field);
        }

        private string VisitLogicalFilter(GraphQLObjectField field)
        {
            string value;
            if (field.Value is null || field.Name is null) throw new ArgumentNullException(nameof(field));
            
            if (field.Value is GraphQLVariable qLVariable)
            {
                var varValue = _variables[qLVariable.Name.StringValue];
                value = varValue is string ? $"'{varValue.ToString()?.Trim('"')}'" : varValue.ToString();
            }
            else
            {
                value = field.Value.Kind switch
                {
                    ASTNodeKind.StringValue => $"'{field.Value.ToString()?.Trim('"')}'",
                    ASTNodeKind.IntValue => ((GraphQLIntValue)field.Value).Value.ToString(),
                    _ => field.Value.ToString()
                };
            }
            
            var fieldName = field.Name.StringValue;

            if (fieldName.EndsWith("_gt"))
            {
                return $"{fieldName[..fieldName.LastIndexOf("_gt", StringComparison.Ordinal)]} gt {value}";
            }
            else if (fieldName.EndsWith("_gte"))
            {
                return $"{fieldName[..fieldName.LastIndexOf("_gte", StringComparison.Ordinal)]} gte {value}";
            }
            else if (fieldName.EndsWith("_lt"))
            {
                return $"{fieldName[..fieldName.LastIndexOf("_lt", StringComparison.Ordinal)]} lt {value}";
            }
            else if (fieldName.EndsWith("_lte"))
            {
                return $"{fieldName[..fieldName.LastIndexOf("_lte", StringComparison.Ordinal)]} lte {value}";
            }
            return $"{fieldName} eq {value}";
        }

        private BuildParts VisitRequestNode(GraphQLField fieldSelection, IEdmStructuredType structuredType, GQLRequestType requestType = GQLRequestType.Query)
        {
            var nodeFields = new List<string>();
            var expandItems = new List<string>();

            foreach (var node in fieldSelection.SelectionSet.Selections)
            {
                if (node is GraphQLFragmentSpread fragField)
                {
                    var frag = _fragments[fragField.FragmentName.Name.StringValue];

                    if (frag.SelectionSet?.Selections.Any() == true)
                    {
                        var fields = frag.SelectionSet?.Selections.OfType<GraphQLField>().Select(f => f.Name.StringValue);
                        nodeFields.AddRange(fields);
                    }
                }
                else if (node is GraphQLField field)
                {
                    BuildParts buildParts = null;
                    if (field.SelectionSet?.Selections.Any() == true)
                    {
                        // todo: handling different kinds of nav props - single nav/multi nav/complex type
                        if (structuredType?
                            .NavigationProperties()?
                            .FirstOrDefault(p => p.Name == field.Name.Value)?
                            .ToEntityType() is IEdmStructuredType navPropType)
                        {
                            // must be a nav prop which requires expand
                            buildParts = VisitRequestNode(field, navPropType);

                            if (structuredType.TypeKind == EdmTypeKind.Complex)
                            {
                                nodeFields.Add($"{fieldSelection.Name.Value}/{field.Name.Value}");
                                expandItems.Add($"{fieldSelection.Name.Value}/{field.Name.Value}($select={BuildSelectFromParts(buildParts.SelectFields)})");
                            }
                            else
                            {
                                expandItems.Add($"{field.Name.Value}($select={BuildSelectFromParts(buildParts.SelectFields, buildParts.QueryArgs)})");
                            }
                            continue;
                        }
                        else
                        {
                            // must be a complex type/single prop which is accessed by path
                            var propType = structuredType.StructuralProperties()?.FirstOrDefault(p => p?.Name == field.Name.Value)?.Type;

                            if (propType?.IsComplex() == true || propType?.IsCollection() == true)
                            {
                                var structType = propType.ToStructuredType();
                                var parts = VisitRequestNode(field, structType);
                                nodeFields.AddRange(parts.SelectFields);
                                expandItems.AddRange(parts.ExpandFields);
                                continue;
                            }
                        }

                    }
                    var visitedField = VisitNodeFields(node as GraphQLField);
                    if (structuredType?.TypeKind == EdmTypeKind.Complex)
                    {
                        visitedField = $"{fieldSelection.Name.Value}/{visitedField}";
                    }
                    nodeFields.Add(visitedField);
                }
            }

            var (keySegment, argString) = VisitArgs(fieldSelection.Arguments, requestType);

            return new BuildParts
            {
                QueryArgs = argString,
                ExpandFields = expandItems,
                SelectFields = nodeFields,
                KeySegment = keySegment,
            };
        }

        private static string BuildSelectFromParts(IList<string> parts, string argString = null)
        {
            var selectFieldString = string.Join(",", parts);
            var fullSelectString = string.Join(";", new[] { selectFieldString, argString }.Where(s => !string.IsNullOrEmpty(s)));
            return fullSelectString;
        }

        private static string BuildExpandFromParts(IList<string> parts)
        {
            var expandString = string.Join(",", parts.Where(s => !string.IsNullOrEmpty(s)));
            return expandString;
        }

        private static string VisitNodeFields(GraphQLField fieldSelection)
        {
            return fieldSelection.Name.StringValue;
        }

        internal RequestNodeInput VisitMutation(GraphQLOperationDefinition gqlMutation, out bool isBatch, IList<string> requestNames)
        {
            if (gqlMutation.SelectionSet.Selections.Count > 1)
            {
                //todo: add requestNames param here
                var jsonRequest = BuildJsonBatchRequest(gqlMutation.SelectionSet, requestNames);
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
                var mutationNode = gqlMutation.SelectionSet.Selections.Single() as GraphQLField;
                //todo: mutation can return both the method call + select fields. Consider abstracting by interface. Need to return full path + select fields
                var requestInput = VisitRequestNode(mutationNode, null, GQLRequestType.Mutation);
                var nodeName = mutationNode.Name.StringValue;
                requestNames.Add(nodeName);

                var fullString = BuildSelectExpandUrl(requestInput, GQLRequestType.Mutation);

                return new RequestNodeInput
                {
                    Name = requestInput.KeySegment is null ? nodeName : $"{nodeName}({requestInput.KeySegment})",
                    QueryString = fullString,
                    Body = requestInput.QueryArgs,
                    RequestType = GQLRequestType.Mutation,
                };

            }
        }

        private string VisitMutationNode(GraphQLField fieldSelection, string actionName)
        {
            return default;
        }


        private string BuildJsonBatchRequest(GraphQLSelectionSet selectionSet, IList<string> requestNames)
        {
            BatchRequestObject batchRequest = new BatchRequestObject();

            foreach (var qryNode in selectionSet.Selections.OfType<GraphQLField>().Select((value, index) => (index, value)))
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
                    Headers = new Dictionary<string, string>
                    {
                        { "accept", "application/json; odata.metadata=none; odata.streaming=true" },
                        { "odata-version", "4.0" },
                    }
                });
            }
            var res = JsonConvert.SerializeObject(batchRequest);
            return res;
        }

        private bool QueryIsFunctionType(IEdmModel model, string itemName)
        {
            return model.EntityContainer.FindOperationImports(itemName).Any();
        }

    }
}
