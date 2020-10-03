# GraphQLOData Middleware

A middleware translates graphql to odata

## Next steps:
- [x] handling entityset queries
- [x] handling functions and actions
- [x] handling variables
- [x] translate to $expand, query fields that have selection sets
- [ ] translate updates and deletes to patch and delete (not post)
- [x] translating fragments
- [x] generate custom filtering options per field e.g. for playground and hasura
- [x] response pipeline to reformat odata response to graphql response
- [ ] generate graphql schema from odata schema to allow graphql introspection by tools like graphiql
- [ ] handling nav props - single/multi/complex type using direct nav syntax e.g. /Customers(1)/Trips(1)/
- [x] order by
- [ ] casting derived types
- [ ] aggregations (count, sum)
