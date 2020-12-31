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

        private BundleDatabase Index { get; }

        public ExtractProvider(BundleDatabase index)
        {
            Index = index;
        }

        public bool SupportsDescendantDepth => true;

        public Task<IAsyncEnumerable<PropfindResult>?> EnumerateProperties(string path, OperationDepth depth, bool getAllProps, IEnumerable<XName> additionalProps)
        {
            var query = QueryFromPath(path);

            if (!Index.TryGetItem(query, out var rootItem))
            {
                return Task.FromResult<IAsyncEnumerable<PropfindResult>?>(null);
            }
            else return Task.FromResult<IAsyncEnumerable<PropfindResult>?>(EnumerateProperties(rootItem, depth, getAllProps, additionalProps));
        }

        static System.Text.RegularExpressions.Regex pathSplitter =
            new(@"^/?(.*?)(?:\.([^.]+))?(?:\.([^.]+))?/?$");
        private (string, string?, string?) QueryFromPath(string path)
        {
            var m = pathSplitter.Match(path);
            string respath = m.Groups[1].Value;
            if (m.Groups[2].Success && m.Groups[3].Success)
                return (respath, m.Groups[2].Value, m.Groups[3].Value);
            else if (m.Groups[2].Success && !m.Groups[3].Success)
                return (respath, null, m.Groups[2].Value);
            else
                return (respath, null, null);
        }

        private string PathFromItem(BdItem item)
            => "/" + item.Path.ToString()
                + (item.Language != null ? "." + item.Language.ToString() : "")
                + (item.Extension != null ? "." + item.Extension.ToString() : "");

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        async IAsyncEnumerable<PropfindResult> EnumerateProperties(BdItem rootItem, OperationDepth depth, bool getAllProps, IEnumerable<XName> additionalProps) {
            var itemsToList = Enumerable.Empty<BdItem>();
            if(depth.HasFlag(OperationDepth.IncludeSelf))
            {
                itemsToList = itemsToList.Append(rootItem);
            }
            
            if(depth.HasFlag(OperationDepth.IncludeChildren) || !depth.HasFlag(OperationDepth.IncludeDescendants))
            {
                itemsToList = itemsToList.Concat(Index.GetDirectChildren(rootItem));
            }
            else if(depth.HasFlag(OperationDepth.IncludeDescendants))
            {
                itemsToList = itemsToList.Concat(Index.GetAllChildren(rootItem));
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
                var isCollection = item is BdCollection;
                var statResult = new PropfindResult(PathFromItem(item), isCollection);

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
                    else if(propname == Name.GetLastModified)
                    {
                        statResult.Add(propname, item.LastModified.ToString("R"));
                    }
                    else if(propname == Name.GetContentLength && item is BdFile clfile)
                    {
                        statResult.Add(propname, clfile.ContentLength);
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

        private IEnumerable<XElement> GetPackageFragment(BdItem item)
        {
            if(item is BdFile fii)
            {
                return fii.Packages.Select(i => new XElement(Name.Package, i.Package.PackageName.ToString()));
            }
            else
            {
                return Enumerable.Empty<XElement>();
            }
        }

        public Task<IContent> GetContent(string path, IList<MediaTypeHeaderValue>? acceptContentType)
        {
            var q = QueryFromPath(path);

            if (!Index.TryGetItem(q, out var rootItem))
            {
                return Task.FromResult(GenericContent.NotFound as IContent);
            }

            if(rootItem is BdCollection)
            {
                return Task.FromResult<IContent>(new GenericContent(ResultCode.Found, null, rootItem.LastModified, true));
            }
            else if(rootItem is BdFile item)
            {
                return Task.FromResult<IContent>(new ExtractFileContent(Index, item));
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

        public Task<System.IO.Stream> GetBodyStream() => Task.FromResult(db.GetStream(item));

        private readonly BdFile item;
        private readonly BundleDatabase db;
        public ExtractFileContent(BundleDatabase bd, BdFile fii)
        {
            db = bd;
            item = fii;
        }
    }
}
