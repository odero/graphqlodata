using Microsoft.OData.ModelBuilder;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Threading.Tasks;

namespace graphqlodata.Models
{
    public class Customer
    {
        public int Id { get; set; }
        public string Name { get; set; }

        public string Email { get; set; }
        [AutoExpand]
        public List<Book> Books { get; set; }

        public List<Address> Addresses { get; set; }
    }
}
