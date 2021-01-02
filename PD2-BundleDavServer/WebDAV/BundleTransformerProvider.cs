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

        record Rule(string OriginalExtension, string NewExtension, bool ReplaceOriginal, Func<Stream, Task<Stream>>? Transformer, string? NewMime);

        private static Rule[] rules = new Rule[]
        {
            new Rule(".texture", ".dds", true, null, null),
            new Rule(".movie", ".bik", true, null, null),
            new Rule(".strings", ".strings.json", true, Transformers.StringsToJson, "text/json")
        };

        public BundleTransformerProvider(IReadableFilesystem backingStore)
        {
            backend = backingStore;
        }

        public bool SupportsDescendantDepth => backend.SupportsDescendantDepth;

        public async Task<IAsyncEnumerable<PropfindResult>?> EnumerateProperties(string path, OperationDepth depth, bool getAllProps, IEnumerable<XName> additionalProps)
        {
            foreach (var (orig, newext, _, _, _) in rules)
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
                foreach(var (orig, newext, replace, _, newMime) in rules)
                {
                    if (item.Path.EndsWith(orig, StringComparison.InvariantCultureIgnoreCase))
                    {
                        if (replace)
                        {
                            item.Path = item.Path.Substring(0, item.Path.Length - orig.Length) + newext;
                            if (newMime != null)
                            {
                                item[Name.GetContentType] = newMime;
                            }
                        }
                        else
                        {
                            var ni = new PropfindResult(item);
                            ni.Path = item.Path.Substring(0, item.Path.Length - orig.Length) + newext;
                            if(newMime != null)
                            {
                                ni[Name.GetContentType] = newMime;
                            }
                            yield return ni;
                        }
                        break;
                    }
                }
                yield return item;
            }
        }

        public async Task<IContent> GetContent(string path, IList<MediaTypeHeaderValue>? acceptContentType)
        {
            Rule? rule = null;

            foreach(var r in rules)
            {
                if(path.EndsWith(r.NewExtension, StringComparison.InvariantCultureIgnoreCase))
                {
                    rule = r;
                }
            }

            if(rule == null)
            {
                return await backend.GetContent(path, acceptContentType);
            }
            else if(rule.Transformer == null)
            {
                path = path.Substring(0, path.Length - rule.NewExtension.Length) + rule.OriginalExtension;
                return await backend.GetContent(path, acceptContentType);
            }
            else
            {
                path = path.Substring(0, path.Length - rule.NewExtension.Length) + rule.OriginalExtension;
                var content = await backend.GetContent(path, acceptContentType);
                return new TransformContent(content, rule.Transformer);
            }
        }

        class TransformContent : IContent
        {
            private IContent backing;
            private Func<Stream, Task<Stream>> transformer;

            public TransformContent(IContent backing, Func<Stream, Task<Stream>> transformer)
            {
                this.backing = backing;
                this.transformer = transformer;
            }

            public ResultCode Status => backing.Status;

            public MediaTypeHeaderValue? ContentType => backing.ContentType;

            public DateTimeOffset LastModified => backing.LastModified;

            public bool UseCollectionFallback => backing.UseCollectionFallback;

            public async Task<Stream> GetBodyStream() => await transformer(await backing.GetBodyStream());
        }
    }
}
