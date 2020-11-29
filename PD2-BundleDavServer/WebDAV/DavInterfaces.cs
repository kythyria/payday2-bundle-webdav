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

    public enum ResultCode
    {
        Found = 200,
        NotFound = 404,
        AccessDenied = 403,
        NotModified = 304
    }

    public interface IStat
    {
        string Path { get; }
        bool IsCollection { get; }
        long? ContentLength { get; }
        DateTimeOffset LastModified { get; }

        /// <summary>
        /// Properties of the item that are not already members of this interface.
        /// </summary>
        IReadOnlyDictionary<XName, (ResultCode status, object value)> Properties { get; }
    }

    /// <summary>
    /// Represents a response to a request for an item's content.
    /// </summary>
    /// <remarks>
    public interface IContent
    {
        ResultCode Status { get; }
        Task<Stream> GetBodyStream();
        MediaTypeHeaderValue ContentType { get; }
        DateTimeOffset LastModified { get; }

        /// <summary>
        /// If true, the server should perform its default behaviour for collections.
        /// </summary>
        bool UseCollectionFallback { get; }
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
        Task<IContent> GetContent(string path);
        Task<IContent> GetContent(string path, IList<MediaTypeHeaderValue> acceptContentType);
        Task<IContent> GetContentIfModified(string path, DateTimeOffset when);
        Task<IContent> GetContentIfModified(string path, IList<MediaTypeHeaderValue> acceptContentType, DateTimeOffset when);
    }

    public class SimpleStat : IStat
    {
        public SimpleStat(string path, bool isCollection, long? contentLength, DateTimeOffset lastModified, IReadOnlyDictionary<XName, (ResultCode status, object value)> properties)
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
        public DateTimeOffset LastModified { get; }

        public IReadOnlyDictionary<XName, (ResultCode status, object value)> Properties { get; }
    }

    public class GenericContent : IContent
    {
        public ResultCode Status { get; }

        public MediaTypeHeaderValue ContentType { get; }

        public DateTimeOffset LastModified { get; }

        public bool UseCollectionFallback { get; }

        public Task<Stream> GetBodyStream() => Task.FromResult(Stream.Null);

        public GenericContent(ResultCode rc, MediaTypeHeaderValue ct, DateTimeOffset lm, bool ucf)
        {
            Status = rc;
            ContentType = ct;
            LastModified = lm;
            UseCollectionFallback = ucf;
        }

        public static GenericContent NotFound { get; } = new(ResultCode.NotFound, null, DateTime.MinValue, false);
    }
}