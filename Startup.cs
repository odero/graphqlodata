using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using graphqlodata.Middlewares;
using graphqlodata.Models;
using graphqlodata.Repositories;
using Microsoft.AspNet.OData.Batch;
using Microsoft.AspNet.OData.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Mvc;
//using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;

namespace graphqlodata
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddOData();
            services.AddScoped<IBooksRepository, BooksRepository>();
            services.AddScoped<ICustomerRepository, CustomerRepository>();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseHttpsRedirection();

            app.UseGraphqlOData();
            app.UseODataBatching();
            app.UseRouting();

            //app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.Filter().Expand().Select().MaxTop(50);
                endpoints.MapODataRoute("odata", "odata", GetEdmModel(), new DefaultODataBatchHandler());
            });
        }

        private IEdmModel GetEdmModel()
        {
            var builder = new ODataConventionModelBuilder();
            builder.EntitySet<Book>("Books");
            var customers = builder.EntitySet<Customer>("Customers");
            var action = builder.Action("AddBook");
            action.ReturnsFromEntitySet<Book>("Books");
            action.Parameter<int>("id");
            action.Parameter<string>("author");
            action.Parameter<string>("title");
            builder.Function("GetSomeBook")
                .ReturnsFromEntitySet<Book>("Books")
                .Parameter<string>("title");
            builder.Function("GetSomeComplexBook")
                .ReturnsFromEntitySet<Book>("Books")
                .Parameter<Address>("title");

            return builder.GetEdmModel();
        }
    }
}
