using System.Collections.Generic;

namespace graphqlodata.Models
{
    public class Customer
    {
        public int Id { get; set; }
        public string Name { get; set; }

        public string Email { get; set; }

        public List<Book> Books { get; set; }

        public List<Address> Addresses { get; set; }
    }
}
