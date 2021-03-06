﻿using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using XIVDBDownloader.Constants;

namespace XIVDBDownloader.Models
{
    [DataContract]
    public class PlacenameData
    {
        [DataMember(Order = 1, Name = "id")]
        public int ID { get; set; }

        [DataMember(Order = 2, Name = "name_de")]
        public string NameDe { get; set; }

        [DataMember(Order = 3, Name = "name_en")]
        public string NameEn { get; set; }

        [DataMember(Order = 4, Name = "name_fr")]
        public string NameFr { get; set; }

        [DataMember(Order = 5, Name = "name_ja")]
        public string NameJa { get; set; }
    }

    public class PlacenameModel :
        XIVDBApiBase<IList<PlacenameData>>
    {
        public override string Uri =>
            @"https://api.xivdb.com/placename?pretty=1&columns=id,name_ja,name_en,name_fr,name_de";

        public void SaveToCSV(
            string file,
            Language language)
        {
            if (this.ResultList == null ||
                this.ResultList.Count < 1)
            {
                return;
            }

            if (!Directory.Exists(Path.GetDirectoryName(file)))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file));
            }

            var buffer = new StringBuilder(5120);

            using (var sw = new StreamWriter(file, false, new UTF8Encoding(false)))
            {
                sw.WriteLine(
                    $"ID,NameEn,Name");

                var orderd =
                    from x in this.ResultList
                    orderby
                    x.ID
                    select
                    x;

                foreach (var data in orderd)
                {
                    var name = data.NameEn;

                    switch (language)
                    {
                        case Language.JA:
                            name = data.NameJa;
                            break;

                        case Language.FR:
                            name = data.NameFr;
                            break;

                        case Language.DE:
                            name = data.NameDe;
                            break;
                    }

                    buffer.AppendLine(
                        $"{data.ID},{data.NameEn},{name}");

                    if (buffer.Length >= 5120)
                    {
                        sw.Write(buffer.ToString());
                        buffer.Clear();
                    }
                }

                if (buffer.Length > 0)
                {
                    sw.Write(buffer.ToString());
                }
            }
        }
    }
}
