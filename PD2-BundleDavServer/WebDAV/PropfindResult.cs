using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace PD2BundleDavServer.WebDAV
{
    public class PropfindResult
    {
        private Dictionary<XName, object?> foundProps = new();
        private HashSet<XName> accessDenied = new();

        public PropfindResult(string path, bool isCollection)
        {
            Path = path;
            if (isCollection)
            {
                Add(Name.ResourceType, new XElement(Name.Collection));
            }
        }

        public PropfindResult(PropfindResult src)
        {
            this.accessDenied.UnionWith(src.accessDenied);
            foreach(var (k,v) in src.foundProps)
            {
                this.foundProps.Add(k, v);
            }
            this.Path = src.Path;
        }

        public string Path { get; set; }
        public bool IsCollection
        {
            get
            {
                if (foundProps.TryGetValue(Name.ResourceType, out var oValue) && oValue is IEnumerable<object> eValue)
                {
                    return eValue.Any(i => i is XElement xe && xe.Name == Name.Collection);
                }
                else
                {
                    return false;
                }
            }
        }

        public object? this[XName property]
        {
            get => foundProps[property];
        }

        public object? TryGetProperty(XName property)
        {
            if(foundProps.TryGetValue(property, out var val))
            {
                return val;
            }
            else
            {
                return null;
            }
        }

        public void Add(XName what, object? value)
        {
            accessDenied.Remove(what);
            if (!foundProps.ContainsKey(what))
            {
                foundProps.Add(what, value);
            }
            else
            {
                foundProps[what] = value;
            }
        }

        public void AddIfMissing(XName what, object? value)
        {
            if(foundProps.ContainsKey(what)) { return; }
            Add(what, value);
        }

        public void AddFailure(XName what, ResultCode rc)
        {
            if (rc == ResultCode.AccessDenied) accessDenied.Add(what);
            else if (rc == ResultCode.Found) this.Add(what, null as object);
        }

        public void Remove(XName what)
        {
            foundProps.Remove(what);
            accessDenied.Remove(what);
        }

        public ResultCode ResultCodeFor(XName property)
        {
            if (foundProps.ContainsKey(property)) return ResultCode.Found;
            else if (accessDenied.Contains(property)) return ResultCode.AccessDenied;
            else return ResultCode.NotFound;
        }

        public IEnumerable<KeyValuePair<XName, object?>> Found => foundProps;
        public IEnumerable<XName> AccessDenied => accessDenied;
    }
}
