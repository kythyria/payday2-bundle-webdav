using Microsoft.Net.Http.Headers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace PD2BundleDavServer.WebDAV
{

    public class BundleTransformerProvider : IReadableFilesystem
    {
        private readonly IReadableFilesystem backend;

        private static (string orig, string replacement)[] extensionChanges = new (string orig, string replacement)[]
        {
            (".texture", ".dds"),
            (".movie", ".bik")
        };

        public BundleTransformerProvider(IReadableFilesystem backingStore)
        {
            backend = backingStore;
        }

        public bool SupportsDescendantDepth => backend.SupportsDescendantDepth;

        public async Task<IAsyncEnumerable<PropfindResult>?> EnumerateProperties(string path, OperationDepth depth, bool getAllProps, IEnumerable<XName> additionalProps)
        {
            foreach (var (orig, newext) in extensionChanges)
            {
                if (path.EndsWith(newext, StringComparison.InvariantCultureIgnoreCase))
                {
                    path = path.Substring(0, path.Length - newext.Length) + orig;
                    break;
                }
            }

            var backenditems = await backend.EnumerateProperties(path, depth, getAllProps, additionalProps);
            if (backenditems == null) return null;

            return EnumeratePropertiesInternal(backenditems, depth, getAllProps, additionalProps);
        }

        public async IAsyncEnumerable<PropfindResult> EnumeratePropertiesInternal(IAsyncEnumerable<PropfindResult> backenditems, OperationDepth depth, bool getAllProps, IEnumerable<XName> additionalProps)
        {
            await foreach(var item in backenditems)
            {
                foreach(var (orig, newext) in extensionChanges)
                {
                    if(item.Path.EndsWith(orig, StringComparison.InvariantCultureIgnoreCase))
                    {
                        item.Path = item.Path.Substring(0, item.Path.Length - orig.Length) + newext;
                        break;
                    }
                }
                yield return item;
            }
        }

        public Task<IContent> GetContent(string path, IList<MediaTypeHeaderValue>? acceptContentType)
        {
            foreach(var (orig, newext) in extensionChanges)
            {
                if(path.EndsWith(newext, StringComparison.InvariantCultureIgnoreCase))
                {
                    path = path.Substring(0, path.Length - newext.Length) + orig;
                    break;
                }
            }

            return backend.GetContent(path, acceptContentType);
        }
    }
}
