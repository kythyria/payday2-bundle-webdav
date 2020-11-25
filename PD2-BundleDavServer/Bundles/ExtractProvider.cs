using Microsoft.Net.Http.Headers;
using PD2BundleDavServer.WebDAV;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace PD2BundleDavServer.Bundles
{
    public class ExtractProvider : IReadableFilesystem
    {
        private PathIndex Index { get; }

        public ExtractProvider(PathIndex index)
        {
            Index = index;
        }

        public bool SupportsDescendantDepth => true;

        public Task<IAsyncEnumerable<IStat>> EnumerateProperties(string path, OperationDepth depth, IEnumerable<XName> requestedProps)
        {
            if (path == "") { path = "/"; }
            else if (path != "/") { path = path.TrimEnd('/'); }

            if (!Index.TryGetItem(path, out var rootItem))
            {
                return null;
            }
            else return Task.FromResult(EnumerateProperties(path, rootItem, depth, requestedProps));
        }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        async IAsyncEnumerable<IStat> EnumerateProperties(string originalPath, PathIndexItem rootItem, OperationDepth depth, IEnumerable<XName> requestedProps) {
            var itemsToList = Enumerable.Empty<PathIndexItem>();
            if(depth.HasFlag(OperationDepth.IncludeSelf))
            {
                itemsToList = itemsToList.Append(rootItem);
            }
            
            if(depth.HasFlag(OperationDepth.IncludeChildren) || !depth.HasFlag(OperationDepth.IncludeDescendants))
            {
                itemsToList = itemsToList.Concat(Index.DirectChildrenListing(rootItem.Path));
            }
            else if(depth.HasFlag(OperationDepth.IncludeDescendants))
            {
                itemsToList = itemsToList.Concat(Index.AllChildrenListing(rootItem.Path));
            }

            foreach(var item in itemsToList)
            {
                var isCollection = item is CollectionIndexItem;
                var props = new Dictionary<XName, (PropResultCode, object)>();
                foreach(var propname in requestedProps)
                {
                    if(propname == Name.PropName)
                    {
                        props.Add(propname, (PropResultCode.Found, Enumerable.Repeat(Name.InPackages, 1)));
                    }
                    else if(propname == Name.InPackages)
                    {
                        props.Add(propname, (PropResultCode.Found, GetPackageFragment(item)));
                    }
                    else if(propname == Name.GetContentType)
                    {
                        props.Add(propname, (PropResultCode.Found, "application/octet-stream"))
                    }
                }
                yield return new SimpleStat(originalPath, isCollection, item.ContentLength, item.LastModified, props);
            }
        }
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously

        private IEnumerable<XElement> GetPackageFragment(PathIndexItem item)
        {
            if(item is FileIndexItem fii)
            {
                return fii.PackageFileEntries.Select(i => new XElement(Name.Package, i.PackageName));
            }
            else
            {
                return Enumerable.Empty<XElement>();
            }
        }

        public IContent GetContent(string path)
        {
            throw new NotImplementedException();
        }

        public IContent GetContent(string path, IList<MediaTypeHeaderValue> acceptContentType)
        {
            throw new NotImplementedException();
        }

        public IContent GetContentIfModified(string path, DateTime when)
        {
            throw new NotImplementedException();
        }

        public IContent GetContentIfModified(string path, IList<MediaTypeHeaderValue> acceptContentType, DateTime when)
        {
            throw new NotImplementedException();
        }
    }
}
