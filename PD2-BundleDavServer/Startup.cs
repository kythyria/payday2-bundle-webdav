using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PD2BundleDavServer
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
            services.AddControllers();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, ILogger<Startup> logger)
        {
            var bundlepath = Configuration["bundles"];
            logger.LogInformation("Bundle directory: {0}", bundlepath);
            var Index = PathIndex.FromDirectory(bundlepath, new System.Threading.CancellationToken(), new Progress<GenericProgress>());
            logger.LogInformation("Done reading bundles");
            var extractProvider = new Bundles.ExtractProvider(Index);

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseRouting();

            app.UseAuthorization();
            app.Map("/davstub", iab => { iab.UseDavStubMiddleware(); });
            app.Map("/extract1", iab => { iab.UseBundleDavMiddleware(Index); });
            app.Map("/extract2", iab => { iab.UseDavMiddleware(extractProvider); });
            extractProvider.ToString();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }
    }
}
