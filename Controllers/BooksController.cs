using graphqlodata.Models;
using graphqlodata.Repositories;
using Microsoft.AspNet.OData;
using Microsoft.AspNet.OData.Routing;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Linq;

namespace graphqlodata.Controllers
{
    [EnableQuery]
    public class BooksController : ODataController
    {
        private readonly IBooksRepository _booksRepository;

        public BooksController(IBooksRepository booksRepository)
        {
            _booksRepository = booksRepository;
        }

        public IQueryable<Book> Get()
        {
            
            return _booksRepository.GetBooks();
        }

        public Book Post(Book book)
        {
            return new Book { Id = 14, Author = "Marcus Garvey", Title = "Sexual Healing" };
        }

        //todo: middleware to convert mutation params to body params and use post
        [HttpPost]
        [ODataRoute("AddBook")]
        public Book AddBook(ODataActionParameters parameters)
        {
            var book = new Book { Id = 14, Author = parameters["author"].ToString(), Title = parameters["title"].ToString() };
            return book;
        }
        [ODataRoute("GetSomeBook(title={mytitle})")]
        public Book GetSomeBook([FromODataUri] string mytitle)
        {
            return _booksRepository.GetBooks().Where(b => b.Title.Equals(mytitle)).FirstOrDefault();
        }
    }
}
