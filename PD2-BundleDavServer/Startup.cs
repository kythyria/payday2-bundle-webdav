using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
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

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, ILogger<Startup> logger)
        {
            string? bp;
            Steam.SteamLocation.TryGetAppDirectory("218620", out bp, logger);

            if(bp != null)
            {
                bp = System.IO.Path.Combine(bp, "assets");
            }

            var bundlepath = Configuration["bundles"] ?? bp ?? "";
            logger.LogInformation("Bundle directory: {0}", bundlepath);
            //var Index = PathIndex.FromDirectory(bundlepath ?? "", new System.Threading.CancellationToken(), new Progress<GenericProgress>());
            var Index = new Bundles.BundleDatabase(bundlepath);
            logger.LogInformation("Done reading bundles");
            var extractProvider = new Bundles.ExtractProvider(Index);
            var transformedProvider = new WebDAV.BundleTransformerProvider(extractProvider);
            WebDAV.IReadableFilesystem provider;
            provider = transformedProvider;
            //provider = extractProvider;

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.Map("/diesel-files", iab => { iab.UseDavMiddleware(transformedProvider); });
            app.Map("/diesel-raw", iab => { iab.UseDavMiddleware(extractProvider); });

            GC.Collect(2, GCCollectionMode.Forced, true, true);
        }
    }
}
