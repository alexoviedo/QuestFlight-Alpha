using System;
using System.IO;

namespace QuestFlight.Product
{
    public static class ProductLocalSave
    {
        public static void Write(string path,string content)
        {
            string temporary=path+".tmp",backup=path+".bak";
            File.WriteAllText(temporary,content);
            if(File.Exists(path))File.Replace(temporary,path,backup);
            else File.Move(temporary,path);
        }
        public static T Read<T>(string path,Func<string,T> parse,out bool recovered) where T:class
        {
            recovered=false;
            if(!File.Exists(path)&&!File.Exists(path+".bak"))return null;
            try {var data=parse(File.ReadAllText(path));if(data==null)throw new InvalidDataException();return data;}
            catch {var data=parse(File.ReadAllText(path+".bak"));if(data==null)throw new InvalidDataException();recovered=true;return data;}
        }
    }
}
