﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using DieselEngineFormats.Bundle;

namespace PD2BundleDavServer
{
    /* There's a better way: store a hash of path -> item index, and a big array of
     * items in breadth-first traversal order. That way all PROPFINDs can work by
     * traversing a slice of the array. But that's tricky to do so we just make
     * a tree and keep it around. */

    public class PathIndex
    {
        Dictionary<string, PathIndexItem> itemsByPath;
        string bundleDirectory;
        PackageDatabase pdb;

        public PathIndexItem this[string path] => itemsByPath[path];
        public IEnumerable<PathIndexItem> DirectChildrenListing(string path)
        {
            var item = this[path];
            
            if(item is CollectionIndexItem ci)
            {
                foreach(var (_, child) in ci.Children)
                {
                    yield return child;
                }
            }
        }

        public bool TryGetItem(string path, out PathIndexItem item) => itemsByPath.TryGetValue(path, out item);

        public IEnumerable<PathIndexItem> AllChildrenListing(string path)
        {
            var tings = new Queue<IEnumerable<PathIndexItem>>();
            var item = this[path] as CollectionIndexItem;
            if (item != null) { tings.Enqueue(item.Children.Values); }

            while(tings.Count > 0)
            {
                var curr = tings.Dequeue();
                foreach(var i in curr)
                {
                    yield return i;
                    if(i is CollectionIndexItem ci)
                    {
                        tings.Enqueue(ci.Children.Values);
                    }
                }
            }
        }

        public string GetBundlePath(PackageFileEntry pfe)
            => Path.Join(bundleDirectory, pfe.Parent.BundleName + ".bundle");

        private PathIndex(string bundleDir) {
            bundleDirectory = bundleDir;
            itemsByPath = new Dictionary<string, PathIndexItem>();
        }
        public static PathIndex FromDirectory(string bundleDir, CancellationToken ct, IProgress<GenericProgress> progress)
            => new PathIndex(bundleDir).LoadFromDirectory(ct, progress);

        private PathIndex LoadFromDirectory(CancellationToken ct, IProgress<GenericProgress> progress)
        {
            var blbpath = Path.Join(bundleDirectory, "bundle_db.blb");

            progress.Report(GenericProgress.Indefinite("Reading BLB file"));

            pdb =  new PackageDatabase();
            pdb.Load(blbpath);

            var pdbdate = new FileInfo(blbpath).LastWriteTimeUtc;

            if (ct.IsCancellationRequested) return null;

            var fileentries = new Dictionary<uint, (DatabaseEntry, string, List<PackageFileEntry>)>(pdb.Entries.Count);
            foreach(var dbe in pdb.GetDatabaseEntries())
            {
                var lang = pdb.LanguageFromID(dbe.Language);
                var path = "/" + dbe.Path + (lang != null ? "." + lang.Name.ToString() : "") + "." + dbe.Extension.ToString();
                fileentries.Add(dbe.ID, (dbe, path, new List<PackageFileEntry>()));
            }

            if (ct.IsCancellationRequested) return null;
            progress.Report(GenericProgress.Indefinite("Reading package headers"));

            var headerNames = Directory.EnumerateFiles(bundleDirectory, "*.bundle").Where(i => !i.EndsWith("_h.bundle")).ToList();
            var bundleDates = new Dictionary<Idstring, DateTime>();

            for (var i = 0; i < headerNames.Count; i++) {
                progress.Report(new GenericProgress("Reading package headers", i, headerNames.Count));
                if (ct.IsCancellationRequested) return null;

                var bundleId = Path.GetFileNameWithoutExtension(headerNames[i]);

                var bundlePath = headerNames[i];
                var bundleHeaderPath = Path.Join(bundleDirectory, bundleId + "_h.bundle");

                var bundleHeaderDate = new FileInfo(bundleHeaderPath).LastWriteTimeUtc;
                var bundleDate = new FileInfo(bundlePath).LastWriteTimeUtc;

                
                PackageHeader bundle = new PackageHeader();
                if (!bundle.Load(bundlePath))
                    continue;

                bundleDates[bundle.Name] = bundleDate > bundleHeaderDate ? bundleDate : bundleHeaderDate;

                foreach(var pfe in bundle.Entries)
                {
                    fileentries[pfe.ID].Item3.Add(pfe);
                }
            }

            if (ct.IsCancellationRequested) return null;
            progress.Report(GenericProgress.Indefinite("Assembling directory tree"));

            itemsByPath["/"] = new CollectionIndexItem(this, "/", DateTime.MinValue);

            foreach(var (fileid, (dbe, path, pfes)) in fileentries)
            {
                var fii = new FileIndexItem(this, path, bundleDates[pfes[0].PackageName], dbe, pfes.ToArray());
                itemsByPath[path] = fii;

                PathIndexItem currentItem = fii;
                while (currentItem.Path != "/")
                {
                    var currentDirectoryPath = GetDirectoryName(currentItem.Path);

                    if (itemsByPath.TryGetValue(currentDirectoryPath, out var pidir))
                    {
                        var cidir = (CollectionIndexItem)pidir;
                        cidir.Children.Add(currentItem.PathSegment, currentItem);
                        break;
                    }
                    else
                    {
                        var cidir = new CollectionIndexItem(this, currentDirectoryPath, DateTime.MinValue);
                        itemsByPath[currentDirectoryPath] = cidir;
                        cidir.Children.Add(currentItem.PathSegment, currentItem);
                        currentItem = cidir;
                        continue;
                    }
                }
            }

            itemsByPath["/"].PostBuild();

            return this;
        }

        private string GetDirectoryName(string path)
        {
            var idx = path.LastIndexOf('/');
            if (idx <= 0) return "/";

            return path.Substring(0, idx);
        }
    }

    public abstract class PathIndexItem
    {
        protected PathIndex index;

        public PathIndexItem(PathIndex pi, string path, DateTime lastmodified)
        {
            index = pi;
            Path = path;
            LastModified = lastmodified;
            var lastslash = path.LastIndexOf('/');
            if (lastslash == -1) { PathSegment = path; }
            else
            {
                PathSegment = path.Substring(lastslash+1);
            }
        }

        public string PathSegment { get; private set; }
        public string Path { get; private set; }
        public DateTime LastModified { get; private set; }
        public virtual long ContentLength { get; }

        public abstract void PostBuild();
    }

    public class CollectionIndexItem : PathIndexItem
    {
        public CollectionIndexItem(PathIndex pi, string path, DateTime lastmodified) : base(pi, path, lastmodified)
        {
            Children = new Dictionary<string, PathIndexItem>();
        }
        public override long ContentLength => 0;
        public IDictionary<string, PathIndexItem> Children { get; private set; }

        public override void PostBuild()
        {
            DateTime md = DateTime.MinValue;
            foreach(var child in Children.Values)
            {
                child.PostBuild();
                md = md > child.LastModified ? md : child.LastModified;
            }
        }
    }

    public class FileIndexItem : PathIndexItem
    {
        public FileIndexItem(PathIndex pi, string path, DateTime lastmodified, DatabaseEntry dbe, PackageFileEntry[] pfes) : base(pi, path, lastmodified)
        {
            DatabaseEntry = dbe;
            PackageFileEntries = pfes;
        }

        public DatabaseEntry DatabaseEntry { get; private set; }
        public PackageFileEntry[] PackageFileEntries { get; private set; }

        public override long ContentLength => PackageFileEntries[0].Length;

        public Task<Stream> GetContentsStream()
        {
            // We sorted this earlier when doing the post-build pass.
            var entry = PackageFileEntries[0];

            var packagestream = new FileStream(index.GetBundlePath(entry), FileMode.Open, FileAccess.Read, FileShare.Read);
            var filestream = new SliceStream(packagestream, entry.Address, entry.Length);
            return Task.FromResult(filestream as Stream);
        }

        public override void PostBuild()
        {
            Array.Sort(PackageFileEntries, (x, y) => y.Length - x.Length);
        }
    }

}
