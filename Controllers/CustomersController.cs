using graphqlodata.Models;
using graphqlodata.Repositories;
using Microsoft.AspNet.OData;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace graphqlodata.Controllers
{
    [EnableQuery]
    public class CustomersController : ODataController
    {
        private readonly ICustomerRepository _customerRepository;

        public CustomersController(ICustomerRepository customerRepository)
        {
            _customerRepository = customerRepository;
        }

        public IQueryable<Customer> Get()
        {
            return _customerRepository.GetCustomers();
        }

        public IActionResult Get([FromODataUri]int key)
        {
            return Ok(_customerRepository.GetCustomer(key));
        }
    }
}
