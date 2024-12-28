using graphqlodata.Models;
using graphqlodata.Repositories;
using Microsoft.AspNetCore.Mvc;
using System.Linq;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;

namespace graphqlodata.Controllers
{
    [EnableQuery]
    public class CustomersController(ICustomerRepository customerRepository) : ODataController
    {
        public IQueryable<Customer> Get()
        {
            return customerRepository.GetCustomers();
        }

        public IActionResult Get([FromODataUri]int key)
        {
            return Ok(customerRepository.GetCustomer(key));
        }
    }
}
