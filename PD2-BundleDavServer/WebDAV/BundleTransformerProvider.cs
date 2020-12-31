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

        public BundleTransformerProvider(IReadableFilesystem backingStore)
        {
            backend = backingStore;
        }

        public bool SupportsDescendantDepth => backend.SupportsDescendantDepth;

        public async Task<IAsyncEnumerable<PropfindResult>?> EnumerateProperties(string path, OperationDepth depth, bool getAllProps, IEnumerable<XName> additionalProps)
        {
            var backenditems = await backend.EnumerateProperties(path, depth, getAllProps, additionalProps);
            if (backenditems == null) return null;

            return EnumeratePropertiesInternal(backenditems, depth, getAllProps, additionalProps);
        }

        public async IAsyncEnumerable<PropfindResult> EnumeratePropertiesInternal(IAsyncEnumerable<PropfindResult> backenditems, OperationDepth depth, bool getAllProps, IEnumerable<XName> additionalProps)
        {
            await foreach(var bi in backenditems)
            {
                var result = bi;
                if(bi.Path.EndsWith(".texture", StringComparison.InvariantCultureIgnoreCase))
                {
                    var nr = new PropfindResult(bi.Path[0..^7] + "dds", false);
                    foreach (var adbp in bi.AccessDenied)
                        nr.AddFailure(adbp, ResultCode.AccessDenied);
                    foreach (var (prop, val) in bi.Found)
                        nr.Add(prop, val);
                    result = nr;
                }
                yield return result;
            }
        }

        public Task<IContent> GetContent(string path, IList<MediaTypeHeaderValue>? acceptContentType)
        {
            if (path.EndsWith(".dds", StringComparison.InvariantCultureIgnoreCase))
            {
                path = path[0..^3] + "texture";
                return backend.GetContent(path, acceptContentType);
            }

            return backend.GetContent(path, acceptContentType);
        }
    }
}
