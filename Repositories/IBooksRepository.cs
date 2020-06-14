using graphqlodata.Models;
using System.Collections.Generic;
using System.Linq;

namespace graphqlodata.Repositories
{
    public interface IBooksRepository
    {
        IQueryable<Book> GetBooks();
        Book GetBook(int id);
    }
}