﻿using System;
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

        public IActionResult Patch([FromRoute] int key, [FromBody]Delta<Book> delta)
        {
            if (delta == null) return BadRequest();
            var original = booksRepository.GetBook(key);
            if (original is null) return NotFound();
            delta.Patch(original);
            return Ok(original);
        }
        public IActionResult Delete([FromODataUri] int key)
        {
            var deleted = booksRepository.RemoveBook(key);
            return Ok(deleted);
        }


        //todo: middleware to convert mutation params to body params and use post
        // [HttpPost]
        [HttpPost("odata/AddBook")]
        public IActionResult AddBook(ODataActionParameters parameters)
        {
            var book = new Book { Id = (int)parameters["id"], Author = parameters["author"].ToString(), Title = parameters["title"].ToString() };
            booksRepository.AddBook(book);
            return Ok(book);
        }
        
        [HttpGet("odata/GetSomeBook(title={mytitle})")]
        public IActionResult GetSomeBook([FromODataUri] string mytitle)
        {
            var title = Uri.UnescapeDataString(mytitle);
            var res = booksRepository.GetBooks().FirstOrDefault(b => b.Title.Equals(title));
            return res == null ? (IActionResult)NotFound() : Ok(res);
        }
        
        [HttpGet("odata/GetSomeComplexBook(title={myAddress})")]
        public IActionResult GetSomeComplexBook([FromODataUri] Address myAddress)
        {
            var res = booksRepository.GetBooks().FirstOrDefault(b => b.Title.Equals(myAddress));
            return res == null ? (IActionResult)NotFound() : Ok(res);
        }
    }
}
