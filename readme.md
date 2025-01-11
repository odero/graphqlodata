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
        Customers { id title }
        ...
    }

    ...
    "
}
```

That's it!

**Important Note**: Unfortunately it's not yet possible to use Postman's GraphQL client since it expects either a graphql schema or introspection support.

## Introspection

Introspection is not currently supported which means autocompletion and GraphiQL are not supported.

## Queries

Queries are used to support the equivalent standard get requests such as `/Books/` as well as OData functions.

### Simple queries and Functions

```graphql
query
{ 
    Customers { id name email }  # entity set
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
    Books(where: { or : { id_lt : 2, title_contains: "Economy" }}) { ...fullBook }
    Customers { 
        id
        name
        PrimaryEmail: email # property name aliasing
    }
    GetSomeBook(title: "The Capitalist Economy")  { ...simpleBook}
}

fragment simpleBook on Books
{
    id
    title
}

fragment fullBook on Books
{
    id
    title
    author
    price
}
```

## Mutations

Mutations support both CRUD modification operations (i.e. create, update, delete) as well as OData actions.

CRUD modification operations are supported via the `_add`, `_update` and `_delete` suffixes using the following format - `EntitySet_<prefix>`

Example: `Books_add`, `Books_update`, `Books_delete`.

The input values for mutations are passed in via an `input` object for both CRUD modifiers and OData actions.

`update` and `delete` operations expect an `id` field which will be passed as the key segment for the specific entity to be modified.

Example:

```graphql
mutation BookMutations
{
    Books_add(input: { title: "river and the source", author: "mister man" }) { id }

    Books_update(id: 12, input: { title: "The man from mars" }) { id }

    Books_delete(id: 11) { id }
}
```

will be translated into the following:

```http
POST /Books
{ title: "river and the source", author: "mister man" }

PATCH /Books(12)
{ title: "The man from mars" }

DELETE /Books(12)
```

**IMPORTANT NOTE**: GraphQL expects all operations to return an object with at least one property. OData however, only optionally returns data from CRUD modification operations. The HTTP response status code `204` for instance returns `NO CONTENT` which means there's no response payload. You can therefore add anything in the expected GraphQL response properties.

### Actions

```graphql
mutation
{
    AddBook(input: { id: 25, title: "small things", author: "no name" }) { ...fullBook }
}
```

### Aggregations

The following aggregations are supported: `sum`, `min`, `max`, `average`, `countdistinct`, `$count` via the following syntax
`<entity set>_aggregate { ... }`.

`groupby` is also supported by using the `group_by` as shown in the example below.

`$count` is represented by `_count` since graphql will not allow field names starting with `$`.

For the individual properties that you want to aggregate, you should use the following syntax `<field name>_<aggregation>`.

Example:

```graphql
Books_aggregate(group_by: [id, author])
{
    price_sum
    AvgBookPrice: price_average # example using prop name aliasing
    price_max
    price_min
    id_countdistinct
    _count # this is how we represent OData's $count
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
- [x] translate deletes to delete http method (not post)
- [x] using bulk operations for insert/update/delete
- [x] aggregations (sum, min, max, average, countdistinct, $count)
- [x] support property aliases
- [ ] aggregate across nav props and complex types
- [ ] generate graphql schema from OData schema to allow graphql introspection by tools like graphiql
- [ ] casting derived types
- [ ] handling nav props - single/multi/complex type using direct nav syntax e.g. /Customers(1)/Trips(1)/
- [ ] Migrate from newtonsoft to system.text.json
