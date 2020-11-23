using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;

namespace PD2BundleDavServer
{
    // You may need to install the Microsoft.AspNetCore.Http.Abstractions package into your project
    public class DavStubMiddleware
    {
        private readonly RequestDelegate _next;

        public DavStubMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public Task Invoke(HttpContext httpContext)
        {
            httpContext.Response.Headers.Add("DAV", new Microsoft.Extensions.Primitives.StringValues("1"));
            switch (httpContext.Request.Path)
            {
                case "":
                case "/":
                    return ProcessRoot(httpContext);
                case "/file.txt":
                    return ProcessFile(httpContext);
                default:
                    httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
                    return httpContext.Response.WriteAsync("No");
            }
        }

        private Task ProcessFile(HttpContext httpContext)
        {
            throw new NotImplementedException();
        }

        private async Task ProcessRoot(HttpContext httpContext)
        {
            switch (httpContext.Request.Method)
            {
                case "OPTIONS":
                    httpContext.Response.StatusCode = StatusCodes.Status200OK;
                    httpContext.Response.Headers.Add("Allow", new Microsoft.Extensions.Primitives.StringValues(new string[] { "GET", "PROPFIND", "OPTIONS" }));
                    return;
                case "GET":
                    httpContext.Response.StatusCode = StatusCodes.Status200OK;
                    httpContext.Response.ContentType = "text/html";
                    await httpContext.Response.WriteAsync("<!DOCTYPE html><html><head><title>Test folder</title></head><body><ul><li><a href=\"file.txt\">file.txt</a></li></ul></body></html>");
                    return;
                case "PROPFIND":
                    httpContext.Response.StatusCode = StatusCodes.Status207MultiStatus;
                    httpContext.Response.ContentType = "application/xml";
                    {
                        using var rbr = new System.IO.StreamReader(httpContext.Request.Body);
                        var rb = await rbr.ReadToEndAsync();
                        using var ss = new StringWriterWithEncoding(System.Text.Encoding.UTF8);
                        using var xw = XmlWriter.Create(ss);
                        xw.WriteStartElement("multistatus", "DAV:");
                        xw.WriteStartElement("response");
                        xw.WriteElementString("href", httpContext.Request.GetEncodedUrl());
                        xw.WriteStartElement("propstat");
                        xw.WriteStartElement("prop");
                        xw.WriteElementString("getlastmodified", DateTime.UtcNow.ToString("R"));
                        xw.WriteStartElement("resourcetype");
                        xw.WriteStartElement("collection");
                        xw.WriteEndElement();
                        xw.WriteEndElement();
                        xw.WriteEndElement();
                        xw.WriteElementString("status", "HTTP/1.1 200 OK");
                        xw.WriteEndElement();
                        xw.WriteEndElement();

                        if (httpContext.Request.Headers["Depth"].Count == 1 && httpContext.Request.Headers["Depth"][0] != "0")
                        {
                            xw.WriteStartElement("response");
                            xw.WriteElementString("href", httpContext.Request.GetEncodedUrl() + "/file.txt");
                            xw.WriteStartElement("propstat");
                            xw.WriteElementString("status", "HTTP/1.1 200 OK");
                            xw.WriteStartElement("prop");
                            xw.WriteElementString("getlastmodified", DateTime.UtcNow.ToString("R"));
                            xw.WriteEndElement();
                            xw.WriteEndElement();
                            xw.WriteEndElement();
                        }
                        xw.WriteEndElement();
                        xw.Flush();
                        await httpContext.Response.WriteAsync(ss.ToString());
                    }
                    return;
                default:
                    httpContext.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
                    await httpContext.Response.WriteAsync("No");
                    return;

            }
        }
    }

    // Extension method used to add the middleware to the HTTP request pipeline.
    public static class DavStubMiddlewareExtensions
    {
        public static IApplicationBuilder UseDavStubMiddleware(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<DavStubMiddleware>();
        }
    }

    public sealed class StringWriterWithEncoding : System.IO.StringWriter
    {
        private readonly System.Text.Encoding encoding;

        public StringWriterWithEncoding(System.Text.Encoding encoding)
        {
            this.encoding = encoding;
        }

        public override System.Text.Encoding Encoding
        {
            get { return encoding; }
        }
    }
}

