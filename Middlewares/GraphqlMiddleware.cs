using GraphQLParser;
using GraphQLParser.AST;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.OData.Edm;
using Microsoft.OData.Edm.Csdl;
using Microsoft.OData.Edm.Validation;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Xml;

namespace graphqlodata.Middlewares
{

    enum GQLRequestType
    {
        Query = 1,
        Mutation,
        Subscription, //not supported
        Function,
        Action,
    }

    public class GraphqlODataMiddleware
    {
        private readonly RequestDelegate _next;
        //todo: store _variables inside request context not as a field
        private IDictionary<string, object> _variables = new Dictionary<string, object>();
        private Lazy<IEdmModel> _model;

        public GraphqlODataMiddleware(RequestDelegate next)
        {
            _next = next;
            _model = new Lazy<IEdmModel>(() =>
            {
                return ReadModel();
            });
        }
        public async Task InvokeAsync(HttpContext context)
        {
            // request pipeline
            var req = context.Request;
            req.EnableBuffering();
            
            if (!req.Path.StartsWithSegments("/odata/graphql"))
            {
                await _next(context);
                return;
            }
            var graphQLQuery = req.Query["query"].ToString();
            if (req.Method == "POST")
            {
                graphQLQuery = await ReadRequestBody(req);
            }
            if (!string.IsNullOrWhiteSpace(graphQLQuery))
            {
                await ConvertGraphQLtoODataQuery(req, graphQLQuery);
            }
            
            await _next(context);
            //response pipeline
            Console.WriteLine("We wrote after _next");
        }

        private async Task ConvertGraphQLtoODataQuery(HttpRequest req, string graphQLQuery)
        {
            //Convert graphql syntax
            RequestNodeInput parsedQuery = ParseGraphql(graphQLQuery, out bool isBatch);
            //todo: build req path prefix
            var pathPrefix = "odata";
            if (isBatch)
            {
                req.Path = $"/{pathPrefix}/$batch";
                await WriteRequestBody(req, parsedQuery.Body);
            }
            else
            {
                req.Path = $"/{pathPrefix}/{parsedQuery.Name}";
                req.QueryString = new QueryString(parsedQuery.QueryString);

                if (parsedQuery.RequestType == GQLRequestType.Query || parsedQuery.RequestType == GQLRequestType.Function)
                {
                    req.Method = "GET";
                }
                else if (parsedQuery.RequestType == GQLRequestType.Mutation || parsedQuery.RequestType == GQLRequestType.Action)
                {
                    // TODO: Mutation might also be treated as patch, put or delete
                    req.Method = "POST";

                    if (!string.IsNullOrEmpty(parsedQuery.Body))
                    {
                        await WriteRequestBody(req, parsedQuery.Body);
                    }
                }
            }
        }

        private async Task<string> ReadResponseBody(HttpResponse res)
        {
            return default;
        }

        private async Task<string> ReadRequestBody(HttpRequest req)
        {
            using StreamReader reader = new StreamReader(req.Body, Encoding.UTF8, true, 1024, true);
            var body = await reader.ReadToEndAsync();
            req.Body.Position = 0;
            return body;
        }

        private async Task WriteRequestBody(HttpRequest req, string payload)
        {
            byte[] reqBytes = Encoding.UTF8.GetBytes(payload);
            req.ContentType = "application/json";
            req.Method = "POST";
            req.Body = new MemoryStream();
            await req.Body.WriteAsync(reqBytes, 0, reqBytes.Length);
            req.Body.Position = 0;
        }

        private RequestNodeInput ParseGraphql(string queryString, out bool hasMultipleRequests)
        {
            var query = JsonConvert.DeserializeObject<GraphQLQuery>(queryString);
            //todo: we want to avoid having field scoped variables in middleware
            _variables = query.Variables;
            var lexer = new Lexer();
            var parser = new Parser(lexer);
            var ast = parser.Parse(new Source(query.Query));

            // TODO: Consider additional definitions like fragments and enums
            if (ast.Definitions.OfType<GraphQLOperationDefinition>().Count() > 1)
            {
                throw new InvalidOperationException("Multiple operations at root level not allowed");
            }

            var parsedQuery = new RequestNodeInput();
            hasMultipleRequests = false;

            foreach (var definition in ast.Definitions.Take(1))
            {
                if (definition is GraphQLOperationDefinition gqlOpDef)
                {
                    if (gqlOpDef.Operation == OperationType.Query)
                    {
                        parsedQuery = VisitQuery(gqlOpDef, out hasMultipleRequests);
                    }
                    else if (gqlOpDef.Operation == OperationType.Mutation)
                    {
                        parsedQuery = VisitMutation(gqlOpDef, out hasMultipleRequests);
                    }
                }
            }
            if (hasMultipleRequests)
            {
                //todo: move serialize batch request here
            }
            
            return parsedQuery;
        }


        private string BuildJsonBatchRequest(GraphQLSelectionSet selectionSet)
        {
            BatchRequestObject batchRequest = new BatchRequestObject();

            foreach (var qryNode in selectionSet.Selections.OfType<GraphQLFieldSelection>().Select((value, index) => (index, value) ))
            {
                var nodeName = qryNode.value.Name.Value;
                //todo: check if query/mutation to determine request method - probably not possible in graphql to combine query and mutation in same request
                RequestNodeInput requestInput;

                if (QueryIsFunctionType(nodeName))
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
                    id = $"{qryNode.index + 1}",
                    method = "GET",
                    url = requestInput.QueryString,
                    headers = new Dictionary<string, string>
                    {
                        { "content-type", "application/json; odata.metadata=none; odata.streaming=true" },
                        { "odata-version", "4.0" },
                    }
                });
            }
            var res = JsonConvert.SerializeObject(batchRequest);
            return res;
        }

        private RequestNodeInput VisitQuery(GraphQLOperationDefinition gqlQuery, out bool isBatch)
        {
            //visit nodes
            if (gqlQuery.SelectionSet.Selections.Count > 1)
            {
                var jsonRequest = BuildJsonBatchRequest(gqlQuery.SelectionSet);
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

                if (QueryIsFunctionType(nodeName))
                {
                    var selectedFields = VisitRequestNode(qryNode, GQLRequestType.Function);
                    selectedFields.Name = $"{nodeName}({selectedFields.QueryString})";
                    selectedFields.QueryString = null;
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

        private bool QueryIsFunctionType(string itemName)
        {
            return _model.Value.EntityContainer.FindOperationImports(itemName).Any();
        }

        private IEdmModel ReadModel()
        {
            using var reader = XmlReader.Create("https://localhost:5001/odata/$metadata");
            CsdlReader.TryParse(reader, out var model, out var errors);
            return model;
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
                            //argList.Add(new QueryBuilder(QueryOptionMapper.Remap(arg.Name.Value.ToLowerInvariant(), argValue.ToString())).ToString().Trim('?'));
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

            foreach (var field in fieldSelection.SelectionSet.Selections.OfType<GraphQLFieldSelection>())
            {
                RequestNodeInput expandField = null;
                if (field.SelectionSet?.Selections.Any() == true)
                {
                    expandField = VisitRequestNode(field);
                    var expandString = $"{field.Name.Value}($select={expandField.QueryString})";
                    expandItems.Add(expandString);
                    continue;
                }
                var visitedField = VisitNodeFields(field as GraphQLFieldSelection);
                nodeFields.Add(visitedField);
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
            //could be a join field
            //if a join field then we need to visit query node to create an Expand
            return fieldSelection.Name.Value;
        }

        private RequestNodeInput VisitMutation(GraphQLOperationDefinition gqlMutation, out bool isBatch)
        {
            if (gqlMutation.SelectionSet.Selections.Count > 1)
            {
                var jsonRequest = BuildJsonBatchRequest(gqlMutation.SelectionSet);
                isBatch = true;
                return new RequestNodeInput
                {
                    Name = "/$batch",
                    Body = jsonRequest,
                };
                //return new KeyValuePair<string, string>("/$batch", jsonRequest);
            }
            else
            {
                isBatch = false;
                var mutationNode = gqlMutation.SelectionSet.Selections.Single() as GraphQLFieldSelection;
                var actionName = mutationNode.Name.Value;
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
    }
}
