﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using PD2BundleDavServer.WebDAV;
using StringValues = Microsoft.Extensions.Primitives.StringValues;

namespace PD2BundleDavServer.WebDAV
{
    // You may need to install the Microsoft.AspNetCore.Http.Abstractions package into your project

    /// <summary>
    /// Implements the WebDAV server as an ASP.Net Middleware
    /// </summary>
    /// <remarks>
    /// The browser-friendly listing code requires <see cref="Name.DisplayName"/> to be present on
    /// collections.
    /// </remarks>
    public class DavMiddleware
    {
        private static readonly System.Xml.Linq.XName[] ListingProps = new System.Xml.Linq.XName[]
        {
            Name.ResourceType,
            Name.GetContentLength,
            Name.GetLastModified,
            Name.DisplayName
        };

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
                case "PROPFIND":
                    await InvokePropfind(httpContext);
                    return;
                default:
                    httpContext.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
                    await httpContext.Response.WriteAsync("No");
                    return;
            }
        }

        async Task InvokeGet(HttpContext ctx)
        {
            var reqHeaders = ctx.Request.GetTypedHeaders();
            IContent content = await backing.GetContent(ctx.Request.Path, reqHeaders.Accept);

            if (content.Status == ResultCode.NotFound)
            {
                ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                ctx.Response.ContentType = "text/plain";
                await ctx.Response.WriteAsync("404 Not Found");
                return;
            }

            else if (content.Status == ResultCode.AccessDenied)
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                ctx.Response.ContentType = "text/plain";
                await ctx.Response.WriteAsync("403 Forbidden");
                return;
            }

            ctx.Response.Headers.Add("Last-Modified", new StringValues(content.LastModified.ToString("R")));

            var dateSelector = reqHeaders.IfModifiedSince;
            if (dateSelector.HasValue && dateSelector <= content.LastModified)
            {
                ctx.Response.StatusCode = StatusCodes.Status304NotModified;
                return;
            }

            ctx.Response.StatusCode = StatusCodes.Status200OK;

            if (content.UseCollectionFallback)
            {
                ctx.Response.ContentType = "text/html; encoding=UTF-8";
                var sb = new System.Text.StringBuilder();
                sb.Append($"<!DOCTYPE html><html><head><title>{ctx.Request.GetDisplayUrl()}</title><body style=\"font-family: monospace\">\n" +
                    $"<table><thead><tr><th>Name</th><th>Size</th><th>Last Modified</th></tr></thead><tbody>\n");
                var childrenasync = await backing.EnumerateProperties(ctx.Request.Path, OperationDepth.IncludeChildren, false, ListingProps);
                if(childrenasync == null)
                {
                    childrenasync = AsyncEnumerable.Empty<PropfindResult>();
                }

                var children = await childrenasync.ToList();

                foreach (var child in children.OrderBy(i => i.IsCollection ? 0 : 1).ThenBy(i => i.Path))
                {
                    var childpath = ctx.Request.PathBase + new PathString(child.Path);
                    var ub = new UriBuilder(ctx.Request.GetEncodedUrl());
                    ub.Path = childpath;

                    var date = child.IsCollection ? "" : child[Name.GetLastModified];
                    var size = child.TryGetProperty(Name.GetContentLength) ?? (child.IsCollection ? "&lt;dir&gt;" : "?");
                    var name = child[Name.DisplayName] + (child.IsCollection ? "/" : "");

                    var row = $"<tr><td><a href=\"{childpath}\">{name}</a></td><td>{size}</td><td>{date}</td></tr>\n";
                    sb.Append(row);
                }
                sb.Append("</tbody></table></body></html>");
                await ctx.Response.WriteAsync(sb.ToString());
            }
            else
            {
                using var stream = await content.GetBodyStream();
                ctx.Response.ContentLength = stream.Length;
                ctx.Response.ContentType = content.ContentType?.ToString() ?? "application/octet-stream";

                await stream.CopyToAsync(ctx.Response.Body);
            }
        }

        async Task InvokePropfind(HttpContext ctx)
        {
            var depthsv = ctx.Request.Headers["Depth"];
            OperationDepth? depthn = depthsv.Count switch
            {
                0 => OperationDepth.Infinity,
                1 => depthsv[0].ToLowerInvariant() switch
                {
                    "0" => OperationDepth.Zero,
                    "1" => OperationDepth.One,
                    "infinity" => OperationDepth.Infinity,
                    "1,noroot" => OperationDepth.OneNoRoot,
                    "infinity,noroot" => OperationDepth.InfinityNoRoot,
                    _ => null
                },
                _ => null
            };

            if (!depthn.HasValue)
            {
                ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
                await ctx.Response.WriteAsync("Depth header must be 0, 1, or infinity.");
                return;
            }

            var depth = depthn.Value;
            PfResult BodyParseResult = PfResult.AllProp;
            HashSet<XName> wantedProps = new();

            if (ctx.Request.ContentLength > 0)
            {
                BodyParseResult = await TryParsePropfind(ctx.Request.Body, wantedProps);
                if (BodyParseResult == PfResult.Error)
                {
                    ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await ctx.Response.WriteAsync("PROPFIND request body was not valid or well-formed");
                    return;
                }
            }

            var itemsToList = await backing.EnumerateProperties(ctx.Request.Path, depth, BodyParseResult == PfResult.AllProp, wantedProps);

            if(itemsToList == null)
            {
                ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                ctx.Response.ContentType = "text/plain";
                await ctx.Response.WriteAsync("404 Not Found");
                return;
            }

            ctx.Response.StatusCode = StatusCodes.Status207MultiStatus;
            ctx.Response.ContentType = "application/xml; encoding=UTF-8";


            var xw = XmlWriter.Create(ctx.Response.Body, new XmlWriterSettings() { Async = true, Encoding = new System.Text.UTF8Encoding(false) });
            await xw.WriteStartElementAsync("", "multistatus", "DAV:");

            await foreach(var curr in itemsToList)
            {
                await xw.WriteStartElementAsync("", "response", "DAV:");

                var itempath = ctx.Request.PathBase + new PathString(curr.Path);
                var ub = new UriBuilder(ctx.Request.GetEncodedUrl());
                ub.Path = itempath;

                await xw.WriteElementStringAsync("", "href", "DAV:", ub.ToString());

                if (BodyParseResult == PfResult.PropNames)
                {
                    await xw.WriteStartElementAsync("", "propstat", "DAV:");
                    await xw.WriteStartElementAsync("", "prop", "DAV:");

                    var namelist = curr[Name.PropName] as IEnumerable<XName>;
                    if(namelist == null)
                    {
                        throw new NotImplementedException("BUG: Backend does not respond to propname requests.");
                    } 

                    foreach (var pn in namelist)
                    {
                        await xw.WriteStartElementAsync("", pn.LocalName, pn.NamespaceName);
                        await xw.WriteEndElementAsync();
                    }

                    await xw.WriteEndElementAsync();
                    await xw.WriteElementStringAsync("", "status", "DAV:", "HTTP/1.1 200 OK");
                    await xw.WriteEndElementAsync();
                }
                else
                {
                    await xw.WriteStartElementAsync("", "propstat", "DAV:");
                    await xw.WriteStartElementAsync("", "prop", "DAV:");

                    foreach(var (name, value) in curr.Found)
                    {
                        await xw.WriteStartElementAsync(xw.LookupPrefix(name.NamespaceName), name.LocalName, name.NamespaceName);
                        await WritePropertyBody(xw, value);
                        await xw.WriteEndElementAsync();
                    }

                    await xw.WriteEndElementAsync();
                    await xw.WriteElementStringAsync("", "status", "DAV:", "HTTP/1.1 200 OK");
                    await xw.WriteEndElementAsync(); // DAV:propstat

                    if(curr.AccessDenied.Count() > 0)
                    {
                        await xw.WriteStartElementAsync("", "propstat", "DAV:");
                        await xw.WriteStartElementAsync("", "prop", "DAV:");

                        foreach(var name in curr.AccessDenied)
                        {
                            await xw.WriteStartElementAsync(xw.LookupPrefix(name.NamespaceName), name.LocalName, name.NamespaceName);
                            await xw.WriteEndElementAsync();
                        }

                        await xw.WriteEndElementAsync();
                        await xw.WriteElementStringAsync("", "status", "DAV:", "HTTP/1.1 403 Forbidden");
                        await xw.WriteEndElementAsync(); // DAV:propstat
                    }

                    var notfound = new HashSet<XName>(wantedProps);
                    notfound.ExceptWith(curr.AccessDenied);
                    notfound.ExceptWith(curr.Found.Select(i => i.Key));

                    if(notfound.Count > 0)
                    {
                        await xw.WriteStartElementAsync("", "propstat", "DAV:");
                        await xw.WriteStartElementAsync("", "prop", "DAV:");

                        foreach (var name in notfound)
                        {
                            await xw.WriteStartElementAsync(xw.LookupPrefix(name.NamespaceName), name.LocalName, name.NamespaceName);
                            await xw.WriteEndElementAsync();
                        }

                        await xw.WriteEndElementAsync();
                        await xw.WriteElementStringAsync("", "status", "DAV:", "HTTP/1.1 404 Not Found");
                        await xw.WriteEndElementAsync(); // DAV:propstat
                    }
                }

                await xw.WriteEndElementAsync(); // DAV: response
            }

            await xw.WriteEndDocumentAsync();
            await xw.FlushAsync();
        }

        async Task WritePropertyBody(XmlWriter xw, object? value)
        {
            if (value == null) return;
            else if (value is XNode xnv) await xnv.WriteToAsync(xw, System.Threading.CancellationToken.None);
            else if (value is IEnumerable<object?> ieov)
                foreach (var i in ieov)
                    await WritePropertyBody(xw, i);
            else await xw.WriteStringAsync(value.ToString());
        }

        enum PfResult
        {
            Error,
            PropNames,
            AllProp,
            SpecificProp
        }

        static async Task<PfResult> TryParsePropfind(System.IO.Stream input, HashSet<XName> result)
        {
            XElement root;

            try
            {
                root = await XElement.LoadAsync(input, LoadOptions.None, new System.Threading.CancellationToken());
            }
            catch
            {
                return PfResult.Error;
            }

            if (root.Name != Name.Propfind) { return PfResult.Error; }

            // valid children:
            // [ <allprop/>, <include/> ]
            // [ <allprop/> ]
            // [ <propname/> ]
            // [ <prop/> ]
            // Note that [] isn't valid. Send nothing in that case.

            var childs = root.Elements().ToList();
            if (childs.Count == 0 || childs.Count > 2) { return PfResult.Error; }

            if(childs.Count == 2 && childs[0].Name == Name.AllProp && childs[1].Name == Name.Include)
            {
                result.UnionWith(childs[1].Elements().Select(i => i.Name));
                return PfResult.AllProp;
            }
            else if(childs.Count == 1 && childs[0].Name == Name.AllProp)
            {
                return PfResult.AllProp;
            }
            else if(childs.Count == 1 && childs[0].Name == Name.PropName)
            {
                result.Add(Name.PropName);
                return PfResult.PropNames;
            }
            else if(childs.Count == 1 && childs[0].Name == Name.Prop)
            {
                result.UnionWith(childs[0].Elements().Select(i => i.Name));
                return PfResult.SpecificProp;
            }
            else
            {
                return PfResult.Error;
            }
        }
    }
}

namespace PD2BundleDavServer {
    // Extension method used to add the middleware to the HTTP request pipeline.
    public static class DavMiddlewareExtensions
    {
        public static IApplicationBuilder UseDavMiddleware(this IApplicationBuilder builder, IReadableFilesystem backing)
        {
            return builder.UseMiddleware<DavMiddleware>(backing);
        }
    }
}
