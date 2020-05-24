using graphqlodata.Models;
using graphqlodata.Repositories;
using Microsoft.AspNet.OData;
using Microsoft.AspNet.OData.Routing;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;

namespace graphqlodata.Controllers
{
    public class BooksController : ODataController
    {
        private readonly IBooksRepository _booksRepository;

        public BooksController(IBooksRepository booksRepository)
        {
            _booksRepository = booksRepository;
        }

        public ICollection<Book> Get()
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
    }
}
