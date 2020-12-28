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
        private static XName[] SupportedCollectionProps = new XName[]
        {
            Name.GetLastModified,
            Name.InPackages,
            Name.DisplayName,
            Name.ResourceType
        };

        private static XName[] SupportedFileProps = new XName[]
        {
            Name.GetLastModified,
            Name.InPackages,
            Name.DisplayName,
            Name.ResourceType,
            Name.GetContentLength,
            Name.GetContentType
        };

        private static XName[] AllPropProps = new XName[]
        {
            Name.GetContentType,
            Name.GetLastModified,
            Name.GetContentLength,
            Name.ResourceType
        };

        private PathIndex Index { get; }

        public ExtractProvider(PathIndex index)
        {
            Index = index;
        }

        public bool SupportsDescendantDepth => true;

        public Task<IAsyncEnumerable<PropfindResult>?> EnumerateProperties(string path, OperationDepth depth, bool getAllProps, IEnumerable<XName> additionalProps)
        {
            if (path == "") { path = "/"; }
            else if (path != "/") { path = path.TrimEnd('/'); }

            if (!Index.TryGetItem(path, out var rootItem))
            {
                return Task.FromResult<IAsyncEnumerable<PropfindResult>?>(null);
            }
            else return Task.FromResult<IAsyncEnumerable<PropfindResult>?>(EnumerateProperties(path, rootItem, depth, getAllProps, additionalProps));
        }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        async IAsyncEnumerable<PropfindResult> EnumerateProperties(string originalPath, PathIndexItem rootItem, OperationDepth depth, bool getAllProps, IEnumerable<XName> additionalProps) {
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

            if(getAllProps)
            {
                additionalProps = Enumerable.Concat(
                    AllPropProps,
                    additionalProps
                );
            }

            foreach(var item in itemsToList)
            {
                var isCollection = item is CollectionIndexItem;
                var statResult = new PropfindResult(item.Path, isCollection);

                foreach(var propname in additionalProps)
                {
                    if(propname == Name.PropName)
                    {
                        statResult.Add(propname, isCollection ? SupportedCollectionProps : SupportedFileProps);
                    }
                    else if(propname == Name.InPackages)
                    {
                        statResult.Add(propname, GetPackageFragment(item));
                    }
                    else if(propname == Name.GetContentType && !isCollection)
                    {
                        statResult.Add(propname, "application/octet-stream");
                    }
                    else if(propname == Name.DisplayName)
                    {
                        statResult.Add(propname, item.PathSegment);
                    }
                    else if(propname == Name.GetLastModified)
                    {
                        statResult.Add(propname, item.LastModified.ToString("R"));
                    }
                    else if(propname == Name.GetContentLength && !isCollection)
                    {
                        statResult.Add(propname, item.ContentLength);
                    }
                    else if(propname == Name.ResourceType && isCollection)
                    {
                        statResult.Add(propname, Enumerable.Repeat(new XElement(Name.Collection), 1));
                    }
                }
                yield return statResult;
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

        public Task<IContent> GetContent(string path, IList<MediaTypeHeaderValue>? acceptContentType)
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
    }

    class ExtractFileContent : IContent
    {
        public ResultCode Status => ResultCode.Found;
        public MediaTypeHeaderValue ContentType => new MediaTypeHeaderValue("application/octet-stream");
        public DateTimeOffset LastModified => item.LastModified;
        public bool UseCollectionFallback => false;

        public Task<System.IO.Stream> GetBodyStream() => item.GetContentsStream();

        private FileIndexItem item;
        public ExtractFileContent(FileIndexItem fii)
        {
            item = fii;
        }
    }
}
