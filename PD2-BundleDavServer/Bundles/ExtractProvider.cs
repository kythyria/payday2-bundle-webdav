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

        public IEnumerable<XName> AllProperties { get; } = Enumerable.Empty<XName>();

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
                var props = new Dictionary<XName, (ResultCode, object)>();
                foreach(var propname in requestedProps)
                {
                    if(propname == Name.PropName)
                    {
                        props.Add(propname, (ResultCode.Found, Enumerable.Repeat(Name.InPackages, 1)));
                    }
                    else if(propname == Name.InPackages)
                    {
                        props.Add(propname, (ResultCode.Found, GetPackageFragment(item)));
                    }
                    else if(propname == Name.GetContentType && !isCollection)
                    {
                        props.Add(propname, (ResultCode.Found, "application/octet-stream"));
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

        public Task<IContent> GetContent(string path)
        {
            if (path == "") { path = "/"; }
            else if (path != "/") { path = path.TrimEnd('/'); }

            if (!Index.TryGetItem(path, out var rootItem))
            {
                return Task.FromResult(GenericContent.NotFound as IContent);
            }

            if(rootItem is CollectionIndexItem)
            {
                return Task.FromResult<IContent>(new GenericContent(ResultCode.Found, null, rootItem.LastModified, true));
            }
            else if(rootItem is FileIndexItem item)
            {
                return Task.FromResult<IContent>(new ExtractFileContent(item));
            }

            throw new NotImplementedException();
        }

        public Task<IContent> GetContent(string path, IList<MediaTypeHeaderValue> acceptContentType) => GetContent(path);
        public Task<IContent> GetContentIfModified(string path, DateTime when) => GetContent(path);
        public Task<IContent> GetContentIfModified(string path, IList<MediaTypeHeaderValue> acceptContentType, DateTime when) => GetContentIfModified(path, when);
    }

    class ExtractFileContent : IContent
    {
        public ResultCode Status => ResultCode.Found;
        public MediaTypeHeaderValue ContentType => new MediaTypeHeaderValue("application/octet-stream");
        public DateTime LastModified => item.LastModified;
        public bool UseCollectionFallback => false;

        public Task<System.IO.Stream> GetBodyStream() => item.GetContentsStream();

        private FileIndexItem item;
        public ExtractFileContent(FileIndexItem fii)
        {
            item = fii;
        }
    }
}
