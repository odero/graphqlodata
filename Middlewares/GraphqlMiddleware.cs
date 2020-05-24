using GraphQLParser;
using GraphQLParser.AST;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace graphqlodata.Middlewares
{

    enum GQLRequestType
    {
        Query = 1,
        Function,
        Mutation,
        Subscription, //not supported
    }

    public class GraphqlODataMiddleware
    {
        private readonly RequestDelegate _next;
        //todo: store _variables inside request context not as a field
        private IDictionary<string, object> _variables = new Dictionary<string, object>();

        public GraphqlODataMiddleware(RequestDelegate next)
        {
            _next = next;
        }
        public async Task InvokeAsync(HttpContext context)
        {
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
        }

        private async Task ConvertGraphQLtoODataQuery(HttpRequest req, string graphQLQuery)
        {
            //Convert graphql syntax
            //todo: consider returning a type/descriptor with path, queryString, headers, queryType(query/mutation/subscription)
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

                if (parsedQuery.RequestType == GQLRequestType.Query)
                {
                    req.Method = "GET";
                }
                else if (parsedQuery.RequestType == GQLRequestType.Mutation)
                {
                    req.Method = "POST";

                    if (!string.IsNullOrEmpty(parsedQuery.Body))
                    {
                        await WriteRequestBody(req, parsedQuery.Body);
                    }
                }
            }
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

            if (ast.Definitions.Count > 1)
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
                        parsedQuery.RequestType = GQLRequestType.Query;
                    }
                    else if (gqlOpDef.Operation == OperationType.Mutation)
                    {
                        parsedQuery.RequestType = GQLRequestType.Mutation;
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

            foreach (GraphQLFieldSelection qryNode in selectionSet.Selections)
            {
                var nodeName = qryNode.Name.Value;
                //todo: check if query/mutation to determine request method
                var selectedFields = VisitRequestNode(qryNode);
                batchRequest.Requests.Add(new RequestObject
                {
                    method = "GET",
                    url = $"{nodeName}/" + new QueryString($"?$select={selectedFields.QueryString}"),
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
                //return new KeyValuePair<string, string>("/$batch", jsonRequest);
                return new RequestNodeInput
                {
                    Name = "$batch",
                    Body = jsonRequest,
                };
            }
            else
            {
                isBatch = false;
                var qryNode = gqlQuery.SelectionSet.Selections.FirstOrDefault() as GraphQLFieldSelection;
                var nodeName = qryNode.Name.Value;
                var selectedFields = VisitRequestNode(qryNode);

                //return new KeyValuePair<string, string>($"/{nodeName}", $"?$select={selectedFields}");
                return new RequestNodeInput
                {
                    Name = nodeName,
                    QueryString = $"?$select={selectedFields}",
                };
            }
        }

        private string VisitArgs(List<GraphQLArgument> args, GQLRequestType requestType)
        {
            var argList = new List<string>();
            var kvPairs = new Dictionary<string, object>();

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
                        //if (arg.Value.Kind == ASTNodeKind.StringValue)
                        //{
                        //    //todo: differentiate how to deal with mutation body params vs function params (need to be quoted)
                        //    argValue = arg.Value.ToString();
                        //}
                        //else if (arg.Value.Kind == ASTNodeKind.IntValue)
                        //{
                        //    argValue = Convert.ChangeType(arg.Value.ToString(), arg.Value.GetType());
                        //}
                        argValue = arg.Value.ToString().Trim('"');
                    }
                    if (requestType == GQLRequestType.Mutation)
                    {
                        kvPairs[arg.Name.Value] = argValue;
                    }
                    else
                    {
                        argList.Add(new QueryBuilder(QueryOptionMapper.Remap(arg.Name.Value.ToLowerInvariant(), argValue.ToString())).ToString());
                    }
                }
            }

            if (requestType == GQLRequestType.Query)
            {
                return string.Join(" and ", argList);
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
            foreach (var field in fieldSelection.SelectionSet.Selections)
            {
                var visitedField = VisitNodeFields(field as GraphQLFieldSelection);
                nodeFields.Add(visitedField);
            }

            var argString = VisitArgs(fieldSelection.Arguments, requestType);
            
            if (requestType == GQLRequestType.Mutation)
            {
                //todo: return nodefields as well
                return new RequestNodeInput
                {
                    QueryString = string.Join(", ", nodeFields),
                    Body = argString,
                };
                    //argString + ":" + string.Join(", ", nodeFields);
            }
            else
            {
                return new RequestNodeInput
                {
                    QueryString = string.Join(", ", nodeFields) + argString,
                };
                //return string.Join(", ", nodeFields) + argString;
            }
            
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
                var mutationNode = gqlMutation.SelectionSet.Selections.FirstOrDefault() as GraphQLFieldSelection;
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
