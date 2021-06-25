using graphqlodata.Handlers;
using GraphQLParser;
using GraphQLParser.AST;
using Microsoft.AspNetCore.Http;
using Microsoft.OData.Edm;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace graphqlodata.Middlewares
{
    public class RequestParser
    {
        private readonly IGraphQLODataRequestHandler _requestHandler;
        private readonly IDictionary<string, GraphQLFragmentDefinition> _fragments;
        private readonly GraphQLExpressionVisitor _visitor;

        internal GraphQLQuery Query { get; }

        internal RequestParser(IGraphQLODataRequestHandler requestHandler, IEdmModel model, string graphQLQuery)
        {
            _requestHandler = requestHandler;
            _fragments = new Dictionary<string, GraphQLFragmentDefinition>();
            Query = JsonConvert.DeserializeObject<GraphQLQuery>(graphQLQuery);
            _visitor = new GraphQLExpressionVisitor(model, Query.Variables, _fragments);
        }

        internal async Task ConvertGraphQLtoODataQuery(HttpRequest req, GraphQLQuery graphQLQuery, IList<string> requestNames)
        {
            //Convert graphql syntax
            RequestNodeInput parsedQuery = ParseGraphql(graphQLQuery, out bool isBatch, requestNames);
            //todo: build req path prefix
            var pathPrefix = "odata";

            if (isBatch)
            {
                req.Path = $"/{pathPrefix}/$batch";
                await _requestHandler.RewriteRequestBody(req, parsedQuery.Body);
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
                    if (parsedQuery.Name.Contains("("))
                    {
                        req.Method = "PATCH";
                    }
                    else
                    {
                        req.Method = "POST";
                    }

                    if (!string.IsNullOrEmpty(parsedQuery.Body))
                    {
                        await _requestHandler.RewriteRequestBody(req, parsedQuery.Body);
                    }
                }
            }

            req.Headers["accept"] = "application/json;odata.metadata=none";
        }

        private RequestNodeInput ParseGraphql(GraphQLQuery query, out bool hasMultipleRequests, IList<string> requestNames)
        {
            //todo: we want to avoid having field scoped variables in middleware
            var lexer = new Lexer();
            var parser = new Parser(lexer);
            
            var ast = parser.Parse(new Source(query.Query));

            // TODO: Consider additional definitions like fragments and enums
            if (ast.Definitions.OfType<GraphQLOperationDefinition>().Count() > 1)
            {
                throw new InvalidOperationException("Multiple operations at root level not allowed");
            }

            foreach (var frag in ast.Definitions.OfType<GraphQLFragmentDefinition>())
            {
                _fragments[frag.Name.Value] = frag;
            }

            var parsedQuery = new RequestNodeInput();
            hasMultipleRequests = false;

            foreach (var definition in ast.Definitions.OfType<GraphQLOperationDefinition>())
            {
                if (definition is GraphQLOperationDefinition gqlOpDef)
                {
                    if (gqlOpDef.Operation == OperationType.Query)
                    {
                        parsedQuery = _visitor.VisitQuery(gqlOpDef, out hasMultipleRequests, requestNames);
                    }
                    else if (gqlOpDef.Operation == OperationType.Mutation)
                    {
                        parsedQuery = _visitor.VisitMutation(gqlOpDef, out hasMultipleRequests, requestNames);
                    }
                }
            }
            if (hasMultipleRequests)
            {
                //todo: move serialize batch request here
            }

            return parsedQuery;
        }
    }
}
