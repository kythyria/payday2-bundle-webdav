using System;
using System.Collections.Generic;
using System.Linq;

using IO = System.IO;
using DB = DieselEngineFormats.Bundle;

using Idstring = DieselEngineFormats.Bundle.Idstring;
using System.Diagnostics.CodeAnalysis;

namespace PD2BundleDavServer.Bundles
{
    interface BdPostLoadable
    {
        void PostLoad();
    }

    public class BdPackage : BdPostLoadable
    {
        public  string Filename { get; }
        public  Idstring PackageName { get; }
        public  DateTimeOffset LastModified { get; }
        public  List<FileRef> Files { get; } = new();

        public BdPackage(string filename, Idstring packagename, DateTimeOffset lastmodified)
        {
            Filename = filename;
            PackageName = packagename;
            LastModified = lastmodified;
        }

        void BdPostLoadable.PostLoad()
        {
            Files.TrimExcess();
        }
    }

    public readonly struct PackageRef
    {
        public BdPackage Package { get; }
        public uint Offset { get; }
        public uint Length { get; }

        public PackageRef(BdPackage package, uint offset, uint length)
        {
            Package = package;
            Offset = offset;
            Length = length;
        }
    }

    public readonly struct FileRef
    {
        public BdFile File { get; }
        public uint Offset { get; }
        public uint Length { get; }

        public FileRef(BdFile file, uint offset, uint length)
        {
            File = file;
            Offset = offset;
            Length = length;
        }
    }

    public abstract class BdItem : BdPostLoadable
    {
        public Idstring Path { get; }
        public Idstring? Language { get; set; }
        public Idstring? Extension { get; set; }
        
        public DateTimeOffset LastModified { get; protected set; }

        void BdPostLoadable.PostLoad() { }

        public BdItem(Idstring path, Idstring? language, Idstring? extension)
        {
            Path = path;
            Language = language;
            Extension = extension;
        }
    }

    public class BdCollection : BdItem, BdPostLoadable
    {
        public List<BdItem> Children { get; set; } = new();

        void BdPostLoadable.PostLoad()
        {
            foreach (var c in Children)
            {
                ((BdPostLoadable)c).PostLoad();
                if (LastModified < c.LastModified)
                {
                    LastModified = c.LastModified;
                }
            }
            Children.TrimExcess();
        }

        public BdCollection(Idstring path) : base(path, null, null) { }
    }

    public class BdFile : BdItem, BdPostLoadable
    {
        public List<PackageRef> Packages { get; set; } = new();
        public uint ContentLength => Packages[0].Length;

        void BdPostLoadable.PostLoad()
        {
            LastModified = Packages.Aggregate(DateTimeOffset.MinValue, (a, p) => a < p.Package.LastModified ? p.Package.LastModified : a);
            Packages.TrimExcess();
        }
        public BdFile(Idstring path, Idstring language, Idstring extension) : base(path, language, extension) { }
    }

    public class BundleDatabase
    {
        Dictionary<(Idstring path, Idstring? language, Idstring? extension), BdItem> items;
        Dictionary<Idstring, BdPackage> packages;

        public string BasePath { get; private set; }

        public BundleDatabase(string basepath)
        {
            BasePath = basepath;

            var blbpath = IO.Path.Join(BasePath, "bundle_db.blb");
            var pdbdate = new IO.FileInfo(blbpath).LastWriteTimeUtc;

            var pdb = new DB.PackageDatabase(blbpath);
            var pdbentries = pdb.GetDatabaseEntries();
            var filesById = new Dictionary<uint, BdFile>(pdbentries.Count);
            items = new(pdbentries.Count);

            foreach(var entry in pdbentries)
            {
                var lang = pdb.Languages[entry.Language].Name;
                var item = new BdFile(entry.Path, lang, entry.Extension);
                filesById.Add(entry.ID, item);
                items.Add((entry.Path, lang, entry.Extension), item);
            }

            var headerNames = IO.Directory.EnumerateFiles(BasePath, "*.bundle").Where(i => !i.EndsWith("_h.bundle")).ToList();

            packages = new(headerNames.Count);
            for (var i = 0; i < headerNames.Count; i++)
            {
                var bundleId = IO.Path.GetFileNameWithoutExtension(headerNames[i]);
                var bundlePath = headerNames[i];
                var bundleHeaderPath = IO.Path.Join(BasePath, bundleId + "_h.bundle");

                var bundleHeaderDate = new IO.FileInfo(bundleHeaderPath).LastWriteTimeUtc;
                var bundleDate = new IO.FileInfo(bundlePath).LastWriteTimeUtc;

                var bundle = new DB.PackageHeader();

                if (!bundle.Load(bundlePath))
                    continue;

                var package = new BdPackage(bundlePath, bundle.Name, bundleDate > bundleHeaderDate ? bundleDate : bundleHeaderDate);
                packages.Add(bundle.Name, package);

                bundle.SortEntriesAddress();
                foreach(var entry in bundle.Entries)
                {
                    var file = filesById[entry.ID];
                    package.Files.Add(new FileRef(file, entry.Address, (uint)entry.Length));
                    file.Packages.Add(new PackageRef(package, entry.Address, (uint)entry.Length));
                }
            }
            Idstring emptyIdstring = GetHashProperly("");

            //root directory
            var root = new BdCollection(emptyIdstring);
            items.Add((emptyIdstring, null, null), root);

            foreach(var file in filesById.Values)
            {
                EnsureParentsExist(file);
            }

            (root as BdPostLoadable).PostLoad();
            foreach(BdPostLoadable i in packages.Values)
            {
                i.PostLoad();
            }
        }

        void EnsureParentsExist(BdItem item)
        {
            var path = item.Path.ToString();
            var lastSlashIdx = path.LastIndexOf('/');
            var parentPath = lastSlashIdx == -1 ? "" : path.Substring(0, lastSlashIdx);

            var parentIdstr = GetHashProperly(parentPath);
            if(TryGetItem((parentIdstr, null, null), out var parent))
            {
                var parentcol = (BdCollection)parent;
                parentcol.Children.Add(item);
            }
            else
            {
                var newParent = new BdCollection(parentIdstr);
                items.Add((parentIdstr, null, null), newParent);
                EnsureParentsExist(newParent);
                newParent.Children.Add(item);
            }
        }

        Idstring GetHashProperly(string str)
        {
            var hash = DieselEngineFormats.Utils.Hash64.HashString(str);
            if(DB.HashIndex.HasHash(hash))
            {
                return DB.HashIndex.Get(hash);
            }
            else
            {
                return DB.HashIndex.Get(str);
            }
        }

        bool TryGetItem((Idstring path, Idstring? language, Idstring? extension) what, [NotNullWhen(true)] out BdItem? item) => throw new NotImplementedException();
        BdPackage? TryGetPackage(Idstring what) => throw new NotImplementedException();
        IO.Stream GetStream(BdFile file) => throw new NotImplementedException();
        IEnumerable<BdItem> GetDirectChildren(BdItem item) => throw new NotImplementedException();
        IEnumerable<BdItem> GetAllChildren(BdItem item) => throw new NotImplementedException();
    }
}
