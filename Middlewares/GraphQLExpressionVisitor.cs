using GraphQLParser.AST;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.OData.Edm;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace graphqlodata.Middlewares
{
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

                if (QueryIsFunctionType(_model, nodeName))
                {
                    var selectedFields = VisitRequestNode(qryNode, GQLRequestType.Function);
                    selectedFields.Name = $"{nodeName}({selectedFields.Path})";
                    selectedFields.QueryString = $"?$select={selectedFields.QueryString}";
                    return selectedFields;
                }
                else
                {
                    var selectedFields = VisitRequestNode(qryNode);
                    selectedFields.Name = nodeName;
                    selectedFields.QueryString = $"?$select={selectedFields.QueryString}";
                    return selectedFields;
                }
            }
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
                                filterArgs.Add($"{arg.Name.Value} eq {argValue}");
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

        private RequestNodeInput VisitRequestNode(GraphQLFieldSelection fieldSelection, GQLRequestType requestType = GQLRequestType.Query)
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
                    RequestNodeInput expandField = null;
                    if (field.SelectionSet?.Selections.Any() == true)
                    {
                        expandField = VisitRequestNode(field);
                        var expandString = $"{field.Name.Value}($select={expandField.QueryString})";
                        expandItems.Add(expandString);
                        continue;
                    }
                    var visitedField = VisitNodeFields(node as GraphQLFieldSelection);
                    nodeFields.Add(visitedField);
                }
            }

            var argString = VisitArgs(fieldSelection.Arguments, requestType);

            if (requestType == GQLRequestType.Mutation)
            {
                //todo: return nodefields as well
                return new RequestNodeInput
                {
                    QueryString = string.Join(",", nodeFields),
                    Body = argString,
                };
            }
            else if (requestType == GQLRequestType.Function)
            {
                return new RequestNodeInput
                {
                    Path = argString,
                    RequestType = requestType,
                    QueryString = string.Join(",", nodeFields),
                };
            }
            else if (requestType == GQLRequestType.Query)
            {
                var selectFieldString = string.Join(",", nodeFields);
                var fullSelectString = string.Join("&", new[] { selectFieldString, argString }.Where(s => !string.IsNullOrEmpty(s)));
                var expandString = string.Join(",", expandItems.Where(s => !string.IsNullOrEmpty(s)));
                expandString = expandItems.Any() ? "$expand=" + expandString : "";
                var queryStringParts = new[]
                {
                    fullSelectString,
                    expandString,
                }
                .Where(s => !string.IsNullOrEmpty(s));

                return new RequestNodeInput
                {
                    QueryString = string.Join("&", queryStringParts),
                    RequestType = requestType,
                };
            }
            return default;

        }

        private string VisitNodeFields(GraphQLFieldSelection fieldSelection)
        {
            return fieldSelection.Name.Value;
        }

        internal RequestNodeInput VisitMutation(GraphQLOperationDefinition gqlMutation, out bool isBatch)
        {
            if (gqlMutation.SelectionSet.Selections.Count > 1)
            {
                //todo: add requestNames param here
                var jsonRequest = BuildJsonBatchRequest(gqlMutation.SelectionSet);
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
                var requestInput = VisitRequestNode(mutationNode, GQLRequestType.Mutation);
                requestInput.Name = mutationNode.Name.Value;
                requestInput.QueryString = $"?$select={requestInput.QueryString}";
                return requestInput;
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

                //todo: check if query/mutation to determine request method - probably not possible in graphql to combine query and mutation in same request
                RequestNodeInput requestInput;

                if (QueryIsFunctionType(_model, nodeName))
                {
                    requestInput = VisitRequestNode(qryNode.value, GQLRequestType.Function);
                    requestInput.QueryString = $"{nodeName}({requestInput.Path})?$select={requestInput.QueryString}";
                }
                else
                {
                    requestInput = VisitRequestNode(qryNode.value);
                    requestInput.QueryString = $"{nodeName}/" + new QueryString($"?$select={requestInput.QueryString}");
                }
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
