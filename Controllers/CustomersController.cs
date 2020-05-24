using graphqlodata.Models;
using graphqlodata.Repositories;
using Microsoft.AspNet.OData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace graphqlodata.Controllers
{
    public class CustomersController : ODataController
    {
        private readonly ICustomerRepository _customerRepository;

        public CustomersController(ICustomerRepository customerRepository)
        {
            _customerRepository = customerRepository;
        }

        public ICollection<Customer> Get()
        {
            return _customerRepository.GetCustomers();
        }

        public Customer Get([FromODataUri]int key)
        {
            return _customerRepository.GetCustomer(key);
        }
    }
}
