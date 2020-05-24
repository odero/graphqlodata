using graphqlodata.Models;
using System.Collections.Generic;

namespace graphqlodata.Repositories
{
    public interface ICustomerRepository
    {
        Customer GetCustomer(int id);
        ICollection<Customer> GetCustomers();
    }
}