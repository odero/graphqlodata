using graphqlodata.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace graphqlodata.Repositories
{
    public class CustomerRepository : ICustomerRepository
    {

        public IQueryable<Customer> GetCustomers()
        {
            var book = new Book { Id = 1, Author = "Adam Smith", ISBN = "123", Price = 10.0M, Title = "The Capitalist Economy" };
            var address = new Address { City = new City { Name = "Nairobi", Id = 1 }, Street = "Mlolongo Rd" };

            return new List<Customer>
            {
                new Customer { Id = 10, Name = "Mister Biggz", Email = "biggz@contoso.com", Books = new List<Book>{ book }, Addresses = new List<Address> { address }, MainAddress = address, FavoriteBook = book },
                new Customer { Id = 11, Name = "Pops", Email = "pops@example.com", MainAddress = address, FavoriteBook = book },
                new Customer { Id = 12, Name = "Martin Kibaba", Addresses = new List<Address> { address } },
            }.AsQueryable();
        }

        public Customer GetCustomer(int id)
        {
            return GetCustomers().Where(c => c.Id == id).FirstOrDefault();
        }
    }
}
