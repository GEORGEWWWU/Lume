using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace Lume
{
    public class LumeFileManager
    {
        // 保存数据到 .lume 文件
        public static void SaveLumeFile(string filePath, NoteData data)
        {
            // 临时生成一个 JSON 字符串
            string jsonText = JsonSerializer.Serialize(data);

            // 如果文件已存在，先删除（避免Zip追加写入问题）
            if (File.Exists(filePath)) File.Delete(filePath);

            using (FileStream zipToOpen = new FileStream(filePath, FileMode.Create))
            using (ZipArchive archive = new ZipArchive(zipToOpen, ZipArchiveMode.Create))
            {
                ZipArchiveEntry jsonEntry = archive.CreateEntry("data.json");
                using (StreamWriter writer = new StreamWriter(jsonEntry.Open()))
                {
                    writer.Write(jsonText);
                }
            }
        }

        // 读取 .lume 文件
        public static NoteData OpenLumeFile(string filePath)
        {
            if (!File.Exists(filePath)) return new NoteData();

            try
            {
                using (ZipArchive archive = ZipFile.OpenRead(filePath))
                {
                    var jsonEntry = archive.GetEntry("data.json");
                    if (jsonEntry == null) return new NoteData();

                    using (var stream = jsonEntry.Open())
                    using (var reader = new StreamReader(stream))
                    {
                        string jsonText = reader.ReadToEnd();
                        return JsonSerializer.Deserialize<NoteData>(jsonText) ?? new NoteData();
                    }
                }
            }
            catch
            {
                return new NoteData(); // 文件损坏时返回空笔记
            }
        }
    }
}