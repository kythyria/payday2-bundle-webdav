using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace PD2BundleDavServer
{
    public static partial class Transformers
    {
        public static async Task<Stream> StringsToJson(Stream input)
        {
            var bytes = new byte[input.Length];
            //var readcount = await input.ReadAsync(bytes, 0, (int)input.Length);
            //if (readcount != input.Length) throw new Exception("Couldn't read entire thing");

            var ims = new MemoryStream(bytes);
            var parsedfile = new DieselEngineFormats.StringsFile(input);
            var oms = new MemoryStream((int)input.Length);
            var jwo = new JsonWriterOptions { Indented = true };
            var jw = new Utf8JsonWriter(oms, jwo);

            jw.WriteStartObject();
            foreach(var entry in parsedfile.LocalizationStrings)
            {
                if (entry.ID.ToString() == "" && entry.Text == "") continue;
                jw.WriteString(entry.ID.ToString(), entry.Text);
            }
            jw.WriteEndObject();
            jw.Flush();
            oms.Seek(0, SeekOrigin.Begin);
            return oms;
        }
    }
}
