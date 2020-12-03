using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace PD2BundleDavServer
{
    public static class Namespace
    {
        public static readonly XNamespace PD2Tools = "https://ns.berigora.net/2020/payday2-tools";
        public static readonly XNamespace Dav = "DAV:";
    }

    public static class Name
    {
        public static readonly XName Propfind = Namespace.Dav + "propfind";
        public static readonly XName AllProp = Namespace.Dav + "allprop";
        public static readonly XName Prop = Namespace.Dav + "prop";
        public static readonly XName Include = Namespace.Dav + "include";
        public static readonly XName PropName = Namespace.Dav + "propname";
        public static readonly XName ResourceType = Namespace.Dav + "resourcetype";
        public static readonly XName GetContentLength = Namespace.Dav + "getcontentlength";
        public static readonly XName GetLastModified = Namespace.Dav + "getlastmodified";
        public static readonly XName GetContentType = Namespace.Dav + "getcontenttype";
        public static readonly XName Collection = Namespace.Dav + "collection";
        public static readonly XName DisplayName = Namespace.Dav + "displayname";

        public static readonly XName InPackages = Namespace.PD2Tools + "in-packages";
        public static readonly XName Package = Namespace.PD2Tools + "package";
    }
}
