using graphqlodata.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace graphqlodata.Repositories
{
    public class CustomerRepository : ICustomerRepository
    {

        public ICollection<Customer> GetCustomers()
        {
            var book = new Book { Id = 1, Author = "Adam Smith", ISBN = "123", Price = 10.0M, Title = "The Capitalist Economy" };
            return new List<Customer>
            {
                new Customer { Id = 10, Name = "Mister Biggz", Books = new List<Book>{ book } },
                new Customer { Id = 11, Name = "Pops"},
                new Customer { Id = 12, Name = "Martin Kibaba"},
            };
        }

        public Customer GetCustomer(int id)
        {
            return GetCustomers().Where(c => c.Id == id).FirstOrDefault();
        }
    }
}
