using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using System.Xml;
using System.Xml.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using StringValues = Microsoft.Extensions.Primitives.StringValues;
using Microsoft.AspNetCore.Http.Extensions;

namespace PD2BundleDavServer
{
    public class BundleDavMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly PathIndex index;

        private static readonly XNamespace DAV = "DAV:";
        private static readonly XName DAV_PROPFIND = DAV + "propfind";
        private static readonly XName DAV_PROP = DAV + "prop";
        private static readonly XName DAV_PROPNAME = DAV + "propname";
        private static readonly XName DAV_INCLUDE = DAV + "include";
        private static readonly XName DAV_ALLPROP = DAV + "allprop";
        private static readonly XName DAV_COLLECTION = DAV + "collection";
        private static readonly XName DAV_GETLASTMODIFIED = DAV + "getlastmodified";
        private static readonly XName DAV_RESOURCETYPE = DAV + "resourcetype";
        private static readonly XName DAV_GETCONTENTLENGTH = DAV + "getcontentlength";
        private static readonly HashSet<XName> DEFAULT_PROPS = new HashSet<XName>()
        {
            DAV_GETLASTMODIFIED,
            DAV_RESOURCETYPE,
            DAV_GETCONTENTLENGTH
        };

        private static readonly HashSet<XName> SUPPORTED_PROPS = DEFAULT_PROPS;

        public BundleDavMiddleware(RequestDelegate next, PathIndex index)
        {
            _next = next;
            this.index = index;
        }

        public async Task Invoke(HttpContext httpContext)
        {
            httpContext.Response.Headers.Add("DAV", new StringValues("1"));

            switch(httpContext.Request.Method) {
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
            var path = ctx.Request.Path.Value;
            if (path == "") { path = "/"; }
            else if (path != "/") { path = path.TrimEnd('/'); }

            if (!index.TryGetItem(path, out var item))
            {
                ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                await ctx.Response.WriteAsync("File not found");
                return;
            }

            if(item is FileIndexItem fii)
            {
                var stream = await fii.GetContentsStream();

                ctx.Response.StatusCode = StatusCodes.Status200OK;
                ctx.Response.ContentType = "application/octet-stream";
                ctx.Response.ContentLength = stream.Length;
                ctx.Response.Headers.Add("Last-Modified", new StringValues(fii.LastModified.ToString("R")));

                await stream.CopyToAsync(ctx.Response.Body);
                return;
            }
            else if(item is CollectionIndexItem cii)
            {
                ctx.Response.StatusCode = StatusCodes.Status200OK;
                ctx.Response.ContentType = "text/html; encoding=UTF-8";
                var children = cii.Children.Values
                    .OrderBy(i => i.PathSegment)
                    .OrderBy(i => !(i is CollectionIndexItem));
                await ctx.Response.WriteAsync($"<!DOCTYPE html><html><head><title>{ctx.Request.GetDisplayUrl()}</title><body style=\"font-family: monospace\">\n" +
                    $"<table><thead><tr><th>Name</th><th>Size</th><th>Last Modified</th></tr></thead><tbody>\n");
                foreach(var child in children)
                {
                    var childpath = ctx.Request.PathBase + new PathString(child.Path);
                    var ub = new UriBuilder(ctx.Request.GetEncodedUrl());
                    ub.Path = childpath;

                    var date = child.LastModified.ToString("yyyy-MM-dd HH:mm");
                    var size = child is CollectionIndexItem cc ? cc.Children.Count : child.ContentLength;
                    var name = child.PathSegment + (child is CollectionIndexItem ? "/" : "");

                    var row = $"<tr><td><a href=\"{childpath}\">{name}</a></td><td>{size}</td><td>{date}</td></tr>\n";
                    await ctx.Response.WriteAsync(row);
                }
                await ctx.Response.WriteAsync("</tbody></table></body></html>");
            }
        }

        async Task InvokePropfind(HttpContext ctx)
        {
            var path = ctx.Request.Path.Value;
            if(path == "") { path = "/"; }
            else if (path != "/") { path = path.TrimEnd('/'); }


            if (!index.TryGetItem(path, out var item))
            {
                ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                await ctx.Response.WriteAsync("File not found");
                return;
            }

            var depthsv = ctx.Request.Headers["Depth"];
            var depth = Depth.Error;
            if(depthsv.Count == 0) { depth = Depth.Infinity; }
            if(depthsv.Count == 1)
            {
                depth = depthsv[0].ToLowerInvariant() switch
                {
                    "0" => Depth.Zero,
                    "1" => Depth.One,
                    "infinity" => Depth.Infinity,
                    "1,noroot" => Depth.OneNoRoot,
                    "infinity,noroot" => Depth.InfinityNoRoot,
                    _ => Depth.Error
                };
            }

            if(depth == Depth.Error)
            {
                ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
                await ctx.Response.WriteAsync("Depth header must be 0, 1, or infinity.");
                return;
            }

            HashSet<XName> wantedProps = DEFAULT_PROPS;
            if(ctx.Request.ContentLength > 0)
            {
                if(ctx.Request.ContentType.Split(';')[0] != "application/xml")
                {
                    ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await ctx.Response.WriteAsync("PROPFIND request bodies must be XML");
                    return;
                }
                else if (ctx.Request.ContentLength > 8192)
                {
                    ctx.Response.StatusCode = StatusCodes.Status413RequestEntityTooLarge;
                    await ctx.Response.WriteAsync("PROPFIND request body much too big (limit 8KiB)");
                    return;
                }

                wantedProps = await ParsePropfindRequestBody(ctx.Request.Body);
                if(wantedProps == null)
                {
                    ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await ctx.Response.WriteAsync("PROPFIND request bodies must be a valid <D:propfind> element.");
                    return;
                }
            }

            ctx.Response.StatusCode = StatusCodes.Status207MultiStatus;

            var itemsToList = Enumerable.Empty<PathIndexItem>();
            if(depth != Depth.OneNoRoot && depth != Depth.InfinityNoRoot)
            {
                itemsToList = itemsToList.Append(item);
            }
            
            if(depth == Depth.One || depth == Depth.OneNoRoot )
            {
                itemsToList = itemsToList.Concat(index.DirectChildrenListing(path));
            }
            else if(depth == Depth.Infinity || depth == Depth.InfinityNoRoot)
            {
                itemsToList = itemsToList.Concat(index.DirectChildrenListing(path));
            }

            var xw = XmlWriter.Create(ctx.Response.Body, new XmlWriterSettings() { Async = true });
            await xw.WriteStartElementAsync("", "multistatus", "DAV:");

            foreach(var curr in itemsToList)
            {
                await xw.WriteStartElementAsync("", "response", "DAV:");

                var itempath = ctx.Request.PathBase + new PathString(curr.Path);
                var ub = new UriBuilder(ctx.Request.GetEncodedUrl());
                ub.Path = itempath;

                await xw.WriteElementStringAsync("", "href", "DAV:", ub.ToString());
                if(wantedProps.Contains(DAV_PROPNAME))
                {
                    await xw.WriteStartElementAsync("", "propstat", "DAV:");
                    await xw.WriteStartElementAsync("", "prop", "DAV:");
                    foreach (var pn in SUPPORTED_PROPS)
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
                    var existentprops = new HashSet<XName>(SUPPORTED_PROPS);
                    existentprops.IntersectWith(wantedProps);

                    var nonexistentprops = new HashSet<XName>(wantedProps);
                    nonexistentprops.ExceptWith(SUPPORTED_PROPS);

                    if (existentprops.Count > 0)
                    {
                        await xw.WriteStartElementAsync("", "propstat", "DAV:");
                        await xw.WriteStartElementAsync("", "prop", "DAV:");
                        
                        foreach(var pn in existentprops)
                        {
                            await xw.WriteStartElementAsync("", pn.LocalName, pn.NamespaceName);
                            if (pn == DAV_GETLASTMODIFIED)
                            {
                                await xw.WriteStringAsync(curr.LastModified.ToString("R"));
                            }
                            else if (pn == DAV_RESOURCETYPE)
                            {
                                if(curr is CollectionIndexItem)
                                {
                                    await xw.WriteStartElementAsync("", "collection", "DAV:");
                                    await xw.WriteEndElementAsync();
                                }
                            }
                            else if (pn == DAV_GETCONTENTLENGTH)
                            {
                                await xw.WriteStringAsync(curr.ContentLength.ToString());
                            }
                            await xw.WriteEndElementAsync();
                        }

                        await xw.WriteEndElementAsync();
                        await xw.WriteElementStringAsync("", "status", "DAV:", "HTTP/1.1 200 OK");
                        await xw.WriteEndElementAsync();
                    }

                    if(nonexistentprops.Count > 0)
                    {
                        await xw.WriteStartElementAsync("", "propstat", "DAV:");
                        await xw.WriteStartElementAsync("", "prop", "DAV:");

                        foreach (var pn in nonexistentprops)
                        {
                            await xw.WriteStartElementAsync("", pn.LocalName, pn.NamespaceName);
                            await xw.WriteEndElementAsync();
                        }

                        await xw.WriteEndElementAsync();
                        xw.WriteElementString("", "status", "DAV:", "HTTP/1.1 404 Not Found");
                        await xw.WriteEndElementAsync();
                    }
                }
                await xw.WriteEndElementAsync();
            }

            await xw.WriteEndDocumentAsync();
            await xw.FlushAsync();
        }

        enum Depth
        {
            Zero,
            One,
            Infinity,
            OneNoRoot,
            InfinityNoRoot,
            Error
        }

        static async Task<HashSet<XName>> ParsePropfindRequestBody(Stream input)
        {
            var result = new HashSet<XName>();
            XElement root;

            try
            {
                root = await XElement.LoadAsync(input, LoadOptions.None, new System.Threading.CancellationToken());
            }
            catch
            {
                return null;
            }

            if (root.Name != DAV_PROPFIND) return null;

            var childs = root.Elements().ToList();
            if (childs.Count == 0 || childs.Count > 2) return null;

            if(childs[0].Name == DAV_ALLPROP)
            {
                result.UnionWith(DEFAULT_PROPS);
                if(childs.Count == 2 && childs[1].Name == DAV_INCLUDE)
                {
                    result.UnionWith(childs[1].Elements().Select(i => i.Name));
                }
                else if (childs.Count == 2)
                {
                    return null;
                }
            }
            else if (childs[0].Name == DAV_PROP) {
                result.UnionWith(childs[0].Elements().Select(i => i.Name));
            }
            else if(childs[0].Name == DAV_PROPNAME)
            {
                result.Add(DAV_PROPNAME);
            }
            else
            {
                return null;
            }
            if (childs.Count == 2) { return null; }
            return result;
        }
    }

    // Extension method used to add the middleware to the HTTP request pipeline.
    public static class BundleDavMiddlewareExtensions
    {
        public static IApplicationBuilder UseBundleDavMiddleware(this IApplicationBuilder builder, PathIndex index)
        {
            return builder.UseMiddleware<BundleDavMiddleware>(index);
        }
    }
}
