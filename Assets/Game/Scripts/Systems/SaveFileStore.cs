using System.IO;
namespace BrokenHeart.Systems
{
    public static class SaveFileStore
    {
        public static void WriteAtomic(string path,string json)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary=path+".tmp";
            File.WriteAllText(temporary,json);
            if(File.Exists(path))File.Replace(temporary,path,path+".bak");else File.Move(temporary,path);
        }
        public static string Read(string path)=>File.ReadAllText(path);
    }
}
