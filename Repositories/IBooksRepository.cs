using graphqlodata.Models;
using System.Collections.Generic;

namespace graphqlodata.Repositories
{
    public interface IBooksRepository
    {
        ICollection<Book> GetBooks();
        Book GetBook(int id);
    }
}