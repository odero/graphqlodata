# GraphQLOData Middleware

A middleware that translates GraphQL to OData

## Usage

Best to use a tool like Postman or Insomnia

Once your run the project (F5 or dotnet run), go to `https://localhost:<port>/odata/$graphql` using one of the above tools.

Set `Content-Type` to `application/json` and then using raw json, send a request as shown below

```json
{
    "query": 
    "
    query SampleQuery
    {
        customers { id title }
        ...
    }

    ...
    "
}
```

That's it!

**Important Note**: Unfortunately it's not yet possible to use Postman's graphql client since it expects either a graphql schema or introspection support.

## Introspection

Introspection is not currently supported which means autocompletion and graphiql are not supported.

## Queries

Queries are used to support the equivalent standard get requests such as `/books/` as well as OData functions.

### Simple queries and Functions

```graphql
query
{ 
    customers { id name email }  // entity set
    GetSomeBook(title: "The Capitalist Economy")  { id title }
}
```

### Complex queries

#### Filtering and Ordering

Filtering is supported via the `where` clause along with various comparison operators added as suffix extensions to the field names.

Supported filtering suffixes include:

- logical operators:`lt`, `lte`, `gt`, `gte`
- string operators: `startswith`, `endswith`, `contains`

You can use `or` and `and` in the filters to support more complex filtering.

Ordering is also supported via the `order_by` operator.

You can you also use `first` and `last` keywords to get limit the number of records returned.

```graphql
query BooksAndCustomers
{ 
    books(where: { or : { id_lt : 2, title_contains: "Economy" }}) { id title author }
    customers(first: 2, order_by: [name_asc, id_desc]) { id name email }
    GetSomeBook(title: "The Capitalist Economy")  { id title author }
}

```

## Fragments

Fragments area also supported

```graphql
query BooksAndCustomers
{ 
    books(where: { or : { id_lt : 2, title_contains: "Economy" }}) { ...fullBook }
    customers { id name email }
    GetSomeBook(title: "The Capitalist Economy")  { ...simpleBook}
}

fragment simpleBook on books {
    id
    title
}
fragment fullBook on books {
    id
    title
    author
    price
}
```

## Mutations

For now mutations only support OData actions.

### Actions

```graphql
mutation AddBook($id: int, $title: String) {
    AddBook(id: 10, title: "river and the source", author: "moses") { id title author }
}
```

## Next steps

- [x] handling entity set queries
- [x] handling functions and actions
- [x] handling variables
- [x] translate to $expand, query fields that have selection sets
- [x] translate updates to patch http method (not post)
- [x] translating fragments
- [x] support custom filtering options per field e.g. for playground and Hasura
- [x] support order by
- [x] response pipeline to reformat OData response to graphql response
- [x] filter on nav props
- [ ] translate deletes to delete http method (not post)
- [ ] generate graphql schema from OData schema to allow graphql introspection by tools like graphiql
- [ ] handling nav props - single/multi/complex type using direct nav syntax e.g. /Customers(1)/Trips(1)/
- [ ] casting derived types
- [ ] aggregations (count, sum)
- [ ] using bulk operations for insert/update/delete
