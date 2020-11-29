using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

using StringValues = Microsoft.Extensions.Primitives.StringValues;

namespace PD2BundleDavServer.WebDAV
{
    // You may need to install the Microsoft.AspNetCore.Http.Abstractions package into your project
    public class DavMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IReadableFilesystem backing;

        public DavMiddleware(RequestDelegate next, IReadableFilesystem backing)
        {
            _next = next;
            this.backing = backing;
        }

        public async Task Invoke(HttpContext httpContext)
        {
            httpContext.Response.Headers.Add("DAV", new StringValues("1"));

            switch (httpContext.Request.Method)
            {
                case "OPTIONS":
                    httpContext.Response.StatusCode = StatusCodes.Status200OK;
                    httpContext.Response.Headers.Add("Allow", new StringValues(new string[] { "GET", "PROPFIND", "OPTIONS" }));
                    httpContext.Response.Headers.Add("MS-Author-Via", new StringValues("DAV"));
                    return;
                case "GET":
                    await InvokeGet(httpContext);
                    return;
                /*case "PROPFIND":
                    await InvokePropfind(httpContext);
                    return;*/
                default:
                    httpContext.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
                    await httpContext.Response.WriteAsync("No");
                    return;
            }
        }

        public async Task InvokeGet(HttpContext ctx)
        {
            var headers = ctx.Request.GetTypedHeaders();
            IContent content;
            if(headers.IfModifiedSince.HasValue)
            {
                content = await backing.GetContentIfModified(ctx.Request.Path, headers.IfModifiedSince.Value);
            }
            else
            {
                content = await backing.GetContent(ctx.Request.Path, null);
            }
        }
    }

    // Extension method used to add the middleware to the HTTP request pipeline.
    public static class DavMiddlewareExtensions
    {
        public static IApplicationBuilder UseDavMiddleware(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<DavMiddleware>();
        }
    }
}
