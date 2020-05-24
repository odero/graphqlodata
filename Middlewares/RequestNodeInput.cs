namespace graphqlodata.Middlewares
{
    class RequestNodeInput
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public string Body { get; set; }
        public string QueryString { get; set; }
        public GQLRequestType RequestType { get; set; }
    }
}
