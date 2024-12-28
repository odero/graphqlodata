using graphqlodata.Models;
using graphqlodata.Repositories;
using Microsoft.AspNetCore.Mvc;
using System.Linq;
using Microsoft.AspNetCore.OData.Deltas;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;

namespace graphqlodata.Controllers
{
    [EnableQuery]
    public class BooksController(IBooksRepository booksRepository) : ODataController
    {
        public IQueryable<Book> Get()
        {
            return booksRepository.GetBooks();
        }

        public Book Get(int key)
        {
            return booksRepository.GetBook(key);
        }

        [EnableQuery]
        public IActionResult Post([FromBody]Book book)
        {
            if (book == null) return BadRequest();
            var newBook =booksRepository.AddBook(
                new Book { Author = book.Author, Title = book.Title, Price = book.Price, ISBN = book.ISBN }
            );
            return Created(newBook);
        }

        public IActionResult Patch([FromODataUri] int key, [FromBody]Delta<Book> book)
        {
            if (book == null) return BadRequest();
            var original = booksRepository.GetBook(key);
            if (original is null) return NotFound();
            book.Patch(original);
            return Updated(original);
        }


        //todo: middleware to convert mutation params to body params and use post
        [HttpPost]
        [Route("AddBook")]
        public IActionResult AddBook(ODataActionParameters parameters)
        {
            var book = new Book { Id = 14, Author = parameters["author"].ToString(), Title = parameters["title"].ToString() };
            return Ok(book);
        }
        [HttpGet("GetSomeBook(title={mytitle})")]
        public IActionResult GetSomeBook([FromODataUri] string mytitle)
        {
            var res = booksRepository.GetBooks().FirstOrDefault(b => b.Title.Equals(mytitle));
            return res == null ? (IActionResult)NotFound() : Ok(res);
        }
        
        [HttpGet("GetSomeComplexBook(title={myAddress})")]
        public IActionResult GetSomeComplexBook([FromODataUri] Address myAddress)
        {
            var res = booksRepository.GetBooks().FirstOrDefault(b => b.Title.Equals(myAddress));
            return res == null ? (IActionResult)NotFound() : Ok(res);
        }
    }
}
