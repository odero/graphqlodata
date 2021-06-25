using graphqlodata.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace graphqlodata.Repositories
{
    public class BooksRepository : IBooksRepository
    {
        private readonly IList<Book> _books;
        public BooksRepository()
        {
            _books = new List<Book>
            {
                new Book { Id = 1, Author = "Adam Smith", ISBN = "123", Price = 10.0M, Title = "The Capitalist Economy"},
                new Book { Id = 2, Author = "Sam Smith", ISBN = "No one but you", Price = 2.50M, Title = "Random Tunes"},
            };
        }

        public Book AddBook(Book book)
        {
            book.Id = _books.Last().Id + 1;
            _books.Add(book);
            return book;
        }

        public IQueryable<Book> GetBooks()
        {
            return _books.AsQueryable();
        }

        public Book GetBook(int id)
        {
            return GetBooks().Where(e => e.Id == id).FirstOrDefault();
        }
    }
}
