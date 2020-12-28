using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

using RuntimeInformation = System.Runtime.InteropServices.RuntimeInformation;
using OSPlatform = System.Runtime.InteropServices.OSPlatform;

namespace PD2BundleDavServer.Steam
{
    public static class SteamLocation
    {
        public static bool TryGetSteamDirectory([NotNullWhen(true)] out string? result)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                result = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string;
                return result != null;
            }
            else
            {
                result = null;
                return false;
            }
        }

        public static bool TryGetAppDirectory(string appid, [NotNullWhen(true)] out string? result, ILogger logger)
        {
            if(!TryGetSteamDirectory(out var steampath))
            {
                result = null;
                return false;
            }

            var libvdfpath = Path.Combine(steampath, "steamapps", "libraryfolders.vdf");
            VdfNode libvdf;
            try
            {
                var libvdfstr = File.ReadAllText(libvdfpath);
                libvdf = new VdfReader().LoadString(libvdfpath, libvdfstr);
            }
            catch(Exception e)
            {
                logger.LogWarning(e, "Failed to read library locations");
                result = null;
                return false;
            }

            var libraries = new List<string>();
            libraries.Add(Path.Combine(steampath, "steamapps"));

            if(libvdf.Children.Count == 0)
            {
                logger.LogWarning("libraryfolders.vdf is strangely shaped");
                result = null;
                return false;
            }

            foreach(var l in libvdf.Children[0].Children)
            {
                if(int.TryParse(l.Name, out _) && l.Value != null)
                {
                    libraries.Add(Path.Combine(l.Value, "steamapps"));
                }
            }

            foreach(var lp in libraries)
            {
                if (TryGetAppDirectory(lp, appid, out result, logger))
                {
                    return true;
                }
            }

            System.Diagnostics.Debugger.Break();
            result = null;
            return false;
        }

        public static bool TryGetAppDirectory(string steamappsFolder, string appid, [NotNullWhen(true)] out string? result, ILogger logger)
        {
            var acfpath = Path.Combine(steamappsFolder, $"appmanifest_{appid}.acf");
            VdfNode acf;
            try
            {
                acf = new VdfReader().LoadString(acfpath, File.ReadAllText(acfpath));
            }
            catch(FileNotFoundException e)
            {
                _ = e;
                result = null;
                return false;
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Failed to read appmanifest");
                result = null;
                return false;
            }

            if (acf.Children.Count == 0)
            {
                logger.LogWarning("appmanifest vdf is strangely shaped");
                result = null;
                return false;
            }

            var installdir = acf.Children[0].FirstOrDefault(c => c.Name == "installdir")?.Value;
            if(installdir != null)
            {
                result = Path.Combine(steamappsFolder, "common", installdir);
                return true;
            }
            else
            {
                result = null;
                return false;
            }
        }
    }
}
