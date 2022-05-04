using System.IO;
using System.Reflection;

namespace PMXRenderer.Test
{
    internal class Program
    {
        static void Main(string[] args)
        {
            var path = @"E:\White\Downloads\MMD Models\Sour miku R-18\Sour miku R-18\Sour miku R-18.pmx";
            var savePath = @"E:\White\Downloads\MMD Models\③_by_SignalK__9f076546fa9df2b390a3767c4e5073b8\蝴蝶裙00.bmp";
            string assemblyFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (File.Exists(savePath))
            {
                File.Delete(savePath);
            }

            new PMXRenderer().GeneratePmxPreviewWindow(path);

            //var bitmap = new PMXRenderer().GeneratePmxPreview(path, 1024, 1024);
            //bitmap.Save(savePath);
        }
    }
}
