using graphqlodata.Middlewares;
using graphqlodata.Models;
using Microsoft.AspNetCore.OData.Batch;
using Microsoft.AspNetCore.OData.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using System.Linq;
using Microsoft.AspNetCore.OData;

namespace graphqlodata
{
    public class Startup
    {
        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddRouting();
            services.AddControllers().AddOData(
                options => options
                    .AddRouteComponents("odata", GetEdmModel(), new DefaultODataBatchHandler())
                    .Filter().Expand().Select().SetMaxTop(50).OrderBy());
            
            services.AddGraphQLOData();  //TODO: https://services.odata.org/V4/(S(v0jrha4xovrjwocj5redsnrt))/TripPinServiceRW/$metadata
            services.AddRepositories();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseHttpsRedirection();

            app.UseGraphQLOData();
            app.UseODataBatching();
            app.UseRouting();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
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
