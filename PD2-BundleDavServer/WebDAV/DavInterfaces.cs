using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Net.Http.Headers;

using Stream = System.IO.Stream;

namespace PD2BundleDavServer.WebDAV
{
    /// <summary>
    /// Represents possible values of a Depth header: which items the client is interested in.
    /// </summary>
    [Flags]
    public enum OperationDepth
    {
        /// <summary>
        /// The named item.
        /// </summary>
        IncludeSelf = 1,
        /// <summary>
        /// Immediate children of the named item.
        /// </summary>
        IncludeChildren = 2,
        /// <summary>
        /// Certainly grandchildren and deeper, optionally immediate children of the named item.
        /// </summary>
        IncludeDescendants = 4,
        /// <summary>
        /// The named item.
        /// </summary>
        Zero = 1,
        /// <summary>
        /// The named item and its immediate children.
        /// </summary>
        One = IncludeSelf | IncludeChildren,
        /// <summary>
        /// The named item and all descendants thereof.
        /// </summary>
        Infinity = IncludeSelf | IncludeChildren | IncludeDescendants,
        /// <summary>
        /// Immediate children of the named item, but not the item itself.
        /// </summary>
        OneNoRoot = IncludeChildren,
        /// <summary>
        /// All descendants of the named item, but not the item itself.
        /// </summary>
        InfinityNoRoot = IncludeChildren | IncludeDescendants
    }

    public enum PropResultCode
    {
        Found = 200,
        NotFound = 404,
        AccessDenied = 403
    }

    public interface IStat
    {
        string Path { get; }
        bool IsCollection { get; }
        long? ContentLength { get; }
        DateTime LastModified { get; }

        /// <summary>
        /// Properties of the item that are not already members of this interface.
        /// </summary>
        IReadOnlyDictionary<XName, (PropResultCode status, object value)> Properties { get; }
    }

    public interface IContent
    {
        Stream GetBodyStream();
        MediaTypeHeaderValue ContentType { get; }
        DateTime LastModified { get; }

    }

    public interface IReadableFilesystem
    {
        /// <summary>
        /// True if <see cref="OperationDepth.IncludeDescendants"/> is valid in calls to <see cref="EnumerateProperties(string, OperationDepth, ISet{XName})"/>
        /// </summary>
        bool SupportsDescendantDepth { get; }

        /// <summary>
        /// Additional properties that should be implied by allprop requests.
        /// </summary>
        IEnumerable<XName> AllProperties { get; }

        /// <summary>
        /// Get properties for a given path and depth. Also services the &lt;D:propnames/&gt; request,
        /// by using that as a property name.
        /// </summary>
        /// <param name="path"></param>
        /// <param name="depth"></param>
        /// <param name="requestedProps"></param>
        /// <returns>Null if the path was not found, otherwise the specified properties.</returns>
        Task<IAsyncEnumerable<IStat>> EnumerateProperties(string path, OperationDepth depth, IEnumerable<XName> requestedProps);
        IContent GetContent(string path);
        IContent GetContent(string path, IList<MediaTypeHeaderValue> acceptContentType);
        IContent GetContentIfModified(string path, DateTime when);
        IContent GetContentIfModified(string path, IList<MediaTypeHeaderValue> acceptContentType, DateTime when);
    }

    public class SimpleStat : IStat
    {
        public SimpleStat(string path, bool isCollection, long? contentLength, DateTime lastModified, IReadOnlyDictionary<XName, (PropResultCode status, object value)> properties)
        {
            Path = path;
            IsCollection = isCollection;
            ContentLength = contentLength;
            LastModified = lastModified;
            Properties = properties;
        }
        public string Path { get; }
        public bool IsCollection { get; }
        public long? ContentLength { get; }
        public DateTime LastModified { get; }

        public IReadOnlyDictionary<XName, (PropResultCode status, object value)> Properties { get; }
    }
}