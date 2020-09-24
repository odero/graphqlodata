using GraphQLParser.AST;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.OData.Edm;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;

namespace graphqlodata.Middlewares
{
    class BuildParts
    {
        public IList<string> SelectFields { get; set; }
        public IList<string> ExpandFields { get; set; }

        public string QueryArgs { get; set; }
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
                var qryNode = gqlQuery.SelectionSet.Selections.Single() as GraphQLFieldSelection;
                var nodeName = qryNode.Name.Value;
                requestNames.Add(nodeName);

                return BuildQueryOrFunction(qryNode);
            }
        }

        private RequestNodeInput BuildQueryOrFunction(GraphQLFieldSelection queryNode)
        {
            string nodeName = queryNode.Name.Value;
            if (QueryIsFunctionType(_model, nodeName))
            {
                var selectedFields = VisitRequestNode(queryNode, null, GQLRequestType.Function);
                var fullString = BuildSelectExpandURL(selectedFields);

                return new RequestNodeInput
                {
                    Name = $"{nodeName}({selectedFields.QueryArgs})",
                    QueryString = fullString,
                    RequestType = GQLRequestType.Query,
                };

            }
            else
            {
                var selectedFields = VisitRequestNode(queryNode, _model.EntityContainer.FindEntitySet(nodeName).EntityType());
                var fullString = BuildSelectExpandURL(selectedFields);

                return new RequestNodeInput
                {
                    Name = queryNode.Name.Value,
                    QueryString = fullString,
                    RequestType = GQLRequestType.Query,
                };
            }
        }

        private string BuildSelectExpandURL(BuildParts parts)
        {
            var selectString = BuildSelectFromParts(parts.SelectFields);
            var expandString = parts.ExpandFields.Any() ? "$expand=" + BuildExpandFromParts(parts.ExpandFields) : "";
            var filterString = string.IsNullOrEmpty(parts.QueryArgs) ? "" : "&" + parts.QueryArgs;
            return $"?$select={selectString}{expandString}{filterString}";
        }

        private string VisitArgs(List<GraphQLArgument> args, GQLRequestType requestType)
        {
            var argList = new List<string>();
            var kvPairs = new Dictionary<string, object>();
            // Query args simple case is stringKey with primitive values
            var filterArgs = new List<string>();
            var keywordArgs = new Dictionary<string, string>();

            if (args != null)
            {
                foreach (var arg in args)
                {
                    object argValue = null;
                    if (arg.Value is GraphQLVariable gqlVariable)
                    {
                        argValue = _variables[gqlVariable.Name.Value].ToString();
                    }
                    else
                    {
                        ////todo: arg might be a dict
                        if (arg.Value.Kind == ASTNodeKind.StringValue)
                        {
                            if (requestType == GQLRequestType.Query || requestType == GQLRequestType.Function)
                            {
                                argValue = $"'{arg.Value.ToString().Trim('"')}'";
                            }
                            else
                            {
                                argValue = arg.Value.ToString().Trim('"');
                            }
                        }
                        else if (arg.Value.Kind == ASTNodeKind.IntValue)
                        {
                            argValue = arg.Value.ToString();
                        }
                        else if (arg.Value is GraphQLObjectValue obj)
                        {
                            argValue = VisitObject(obj);
                        }
                    }
                    if (requestType == GQLRequestType.Mutation)
                    {
                        kvPairs[arg.Name.Value] = argValue;
                    }
                    else
                    {
                        if (requestType == GQLRequestType.Function)
                        {
                            filterArgs.Add($"{arg.Name.Value}={argValue}");
                        }
                        else
                        {
                            var argName = arg.Name.Value.ToLowerInvariant();
                            if (QueryOptionMapper.Options.Keys.Contains(argName))
                            {
                                var remapped = QueryOptionMapper.Remap(argName, argValue.ToString());
                                remapped.ToList().ForEach(kv => keywordArgs[kv.Key] = kv.Value);
                            }
                            else
                            {
                                if (arg.Name.Value == "filter")
                                {
                                    filterArgs.Add((string)argValue);
                                }
                                else
                                {
                                    filterArgs.Add($"{arg.Name.Value} eq {argValue}");
                                }
                            }
                        }
                    }
                }
            }

            if (requestType == GQLRequestType.Query)
            {
                var keywordString = new QueryBuilder(keywordArgs).ToString().Trim('?');
                var filterString = filterArgs.Count > 0 ? "$filter=" + string.Join(" and ", filterArgs) : "";
                return string.Join("&", new string[] { filterString, keywordString }.Where(s => !string.IsNullOrEmpty(s)));
            }
            else if (requestType == GQLRequestType.Function)
            {
                return filterArgs.Count > 0 ? string.Join(",", filterArgs) : "";
            }
            else if (requestType == GQLRequestType.Mutation)
            {
                return JsonConvert.SerializeObject(kvPairs);
            }
            return "";
        }

        private string VisitObject(GraphQLValue value, string op=" and ")
        {
            string query = "";
            var queries = new List<string>();
            if (value is GraphQLObjectValue objValue)
            {
                foreach (var field in objValue.Fields)
                {
                    switch (field.Name.Value)
                    {
                        case "OR":
                        case "AND":
                            queries.Add(VisitObject(field.Value, field.Name.Value));
                            break;
                        default:
                            queries.Add(VisitStringFilter(field));
                            break;
                    }
                }
                query = string.Join(" and ", queries);
            }
            else if (value is GraphQLListValue listValue)
            {
                var parts = new List<string>();
                foreach (var item in listValue.Values)
                {
                    parts.Add(VisitObject(item));
                }
                query = string.Join($" {op} ", parts);
            }
            return query;
        }

        private string VisitStringFilter(GraphQLObjectField field)
        {
            var fieldName = field.Name.Value;
            if (fieldName.EndsWith("_in"))
            {
                var valueList = field.Value as GraphQLListValue;
                return string.Concat(
                    fieldName.Substring(0, fieldName.LastIndexOf("_in")),
                    " in ",
                    string.Join(',', valueList?.Values).Select(v => $"'{v.ToString().Trim('"')}'")
                );
            }

            var value = field.Value.ToString().Trim('"');
            
            if (fieldName.EndsWith("_contains"))
            {
                return $"contains({fieldName.Substring(0, fieldName.LastIndexOf("_contains"))}, '{value}')";
            }
            else if (fieldName.EndsWith("_startswith"))
            {
                return $"startswith({fieldName.Substring(0, fieldName.LastIndexOf("_startswith"))}, '{value}')";
            }
            else if (fieldName.EndsWith("_endswith"))
            {
                return $"endswith({fieldName.Substring(0, fieldName.LastIndexOf("_endswith"))}, '{value}')";
            }
            return VisitLogicalFilter(field);
        }

        private string VisitLogicalFilter(GraphQLObjectField field)
        {
            var value = field.Value.Kind == ASTNodeKind.IntValue ? field.Value.ToString() : $"'{field.Value.ToString().Trim('"')}'";
            var fieldName = field.Name.Value;

            if (field.Name.Value.EndsWith("_gt"))
            {
                return $"{fieldName.Substring(0, fieldName.LastIndexOf("_gt"))} gt {value}";
            }
            else if (fieldName.EndsWith("_gte"))
            {
                return $"{fieldName.Substring(0, fieldName.LastIndexOf("_gte"))} gte {value}";
            }
            else if (fieldName.EndsWith("_lt"))
            {
                return $"{fieldName.Substring(0, fieldName.LastIndexOf("_lt"))} lt {value}";
            }
            else if (fieldName.EndsWith("_lte"))
            {
                return $"{fieldName.Substring(0, fieldName.LastIndexOf("_lte"))} lte {value}";
            }
            return $"{fieldName} eq {value}";
        }

        private BuildParts VisitRequestNode(GraphQLFieldSelection fieldSelection, IEdmStructuredType structuredType, GQLRequestType requestType = GQLRequestType.Query)
        {
            var nodeFields = new List<string>();
            var expandItems = new List<string>();

            foreach (var node in fieldSelection.SelectionSet.Selections)
            {
                if (node is GraphQLFragmentSpread fragField)
                {
                    var frag = _fragments[fragField.Name.Value];

                    if (frag.SelectionSet?.Selections.Any() == true)
                    {
                        var fields = frag.SelectionSet?.Selections.OfType<GraphQLFieldSelection>().Select(f => f.Name.Value);
                        nodeFields.AddRange(fields);
                    }
                }
                else if (node is GraphQLFieldSelection field)
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
                                expandItems.Add($"{field.Name.Value}($select={BuildSelectFromParts(buildParts.SelectFields)})");
                            }
                            continue;
                        }
                        else
                        {
                            // must be a complex type/single prop which is accessed by path
                            var propType = structuredType.StructuralProperties()?.FirstOrDefault(p => p.Name == field.Name.Value).Type;

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
                    var visitedField = VisitNodeFields(node as GraphQLFieldSelection);
                    if (structuredType?.TypeKind == EdmTypeKind.Complex)
                    {
                        visitedField = $"{fieldSelection.Name.Value}/{visitedField}";
                    }
                    nodeFields.Add(visitedField);
                }
            }

            var argString = VisitArgs(fieldSelection.Arguments, requestType);

            // todo: can possibly combine all these and get rid of the if/else
            if (requestType == GQLRequestType.Mutation)
            {
                return new BuildParts
                {
                    QueryArgs = argString,
                    ExpandFields = expandItems,
                    SelectFields = nodeFields,
                };

            }
            else if (requestType == GQLRequestType.Function)
            {
                return new BuildParts
                {
                    QueryArgs = argString,
                    ExpandFields = expandItems,
                    SelectFields = nodeFields,
                };
            }
            else if (requestType == GQLRequestType.Query)
            {
                return new BuildParts
                {
                    QueryArgs = argString,
                    ExpandFields = expandItems,
                    SelectFields = nodeFields,
                };
            }
            return default;

        }

        private string BuildSelectFromParts(IList<string> parts, string argString = null)
        {
            var selectFieldString = string.Join(",", parts);
            var fullSelectString = string.Join("&", new[] { selectFieldString, argString }.Where(s => !string.IsNullOrEmpty(s)));
            return fullSelectString;
        }

        private string BuildExpandFromParts(IList<string> parts)
        {
            var expandString = string.Join(",", parts.Where(s => !string.IsNullOrEmpty(s)));
            return expandString;
        }

        private string VisitNodeFields(GraphQLFieldSelection fieldSelection)
        {
            return fieldSelection.Name.Value;
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
                var mutationNode = gqlMutation.SelectionSet.Selections.Single() as GraphQLFieldSelection;
                //todo: mutation can return both the method call + select fields. Consider abstracting by interface. Need to return full path + select fields
                var requestInput = VisitRequestNode(mutationNode, null, GQLRequestType.Mutation);
                var nodeName = mutationNode.Name.Value;
                requestNames.Add(nodeName);

                var fullString = BuildSelectExpandURL(requestInput);

                return new RequestNodeInput
                {
                    Name = mutationNode.Name.Value,
                    QueryString = fullString,
                    Body = requestInput.QueryArgs,
                    RequestType = GQLRequestType.Mutation,
                };

            }
        }

        private string VisitMutationNode(GraphQLFieldSelection fieldSelection, string actionName)
        {
            return default;
        }


        private string BuildJsonBatchRequest(GraphQLSelectionSet selectionSet, IList<string> requestNames = null)
        {
            BatchRequestObject batchRequest = new BatchRequestObject();

            foreach (var qryNode in selectionSet.Selections.OfType<GraphQLFieldSelection>().Select((value, index) => (index, value)))
            {
                var nodeName = qryNode.value.Name.Value;
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
