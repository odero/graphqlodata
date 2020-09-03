using graphqlodata.Models;
using System.Collections.Generic;
using System.Linq;

namespace graphqlodata.Repositories
{
    public interface ICustomerRepository
    {
        Customer GetCustomer(int id);
        IQueryable<Customer> GetCustomers();
    }
}