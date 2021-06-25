using graphqlodata.Models;
using graphqlodata.Repositories;
using Microsoft.AspNet.OData;
using Microsoft.AspNet.OData.Routing;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

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

        public Book Get(int key)
        {
            return _booksRepository.GetBook(key);
        }

        [EnableQuery]
        public IActionResult Post([FromBody]Book book)
        {
            if (book == null) return BadRequest();
            var newBook =_booksRepository.AddBook(
                new Book { Author = book.Author, Title = book.Title, Price = book.Price, ISBN = book.ISBN }
            );
            return Created(newBook);
        }

        public IActionResult Patch([FromODataUri] int key, [FromBody]Delta<Book> book)
        {
            if (book == null) return BadRequest();
            var original = _booksRepository.GetBook(key);
            book.Patch(original);
            return Updated(original);
        }


        //todo: middleware to convert mutation params to body params and use post
        [HttpPost]
        [ODataRoute("AddBook")]
        public IActionResult AddBook(ODataActionParameters parameters)
        {
            var book = new Book { Id = 14, Author = parameters["author"].ToString(), Title = parameters["title"].ToString() };
            return Ok(book);
        }
        [ODataRoute("GetSomeBook(title={mytitle})")]
        public IActionResult GetSomeBook([FromODataUri] string mytitle)
        {
            var res = _booksRepository.GetBooks().Where(b => b.Title.Equals(mytitle)).FirstOrDefault();
            return res == null ? (IActionResult)NotFound() : Ok(res);
        }

        [HttpGet]
        [ODataRoute("GetSomeComplexBook(title={myAddress})")]
        public IActionResult GetSomeComplexBook([FromODataUri] Address myAddress)
        {
            var res = _booksRepository.GetBooks().Where(b => b.Title.Equals(myAddress)).FirstOrDefault();
            return res == null ? (IActionResult)NotFound() : Ok(res);
        }
    }
}
