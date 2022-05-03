using System.IO;
using System.Reflection;

namespace PMXRenderer.Test
{
    internal class Program
    {
        static void Main(string[] args)
        {
            var path = @"E:\White\Downloads\MMD Models\Tda 粉梦迷蒙 初音 [by莉董勤]\Tda 粉梦迷蒙 初音.pmx";
            var savePath = @"E:\White\Downloads\MMD Models\Tda 粉梦迷蒙 初音 [by莉董勤]\Tda 粉梦迷蒙 初音.bmp";
            string assemblyFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (File.Exists(savePath))
            {
                File.Delete(savePath);
            }

            //new PMXRenderer().GeneratePmxPreviewWindow(path);

            var bitmap = new PMXRenderer().GeneratePmxPreview(path, 1024, 1024);
            bitmap.Save(savePath);
        }
    }
}
