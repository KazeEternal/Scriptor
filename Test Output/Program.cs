using System;

namespace Test_Ouput
{
    class Program
    {
        static void Main(string[] args)
        {
            string outputPath = @"D:\Encoding\Video\Test";
            int discNumber = 0;
            int fileNumber = 0;
            for (int i = 0; i < 10; i++)
            {
                if(i % 3 == 0)
                {
                    discNumber++;
                }

                FileInfo fileInfo = new FileInfo(Path.Combine(outputPath, String.Format("Test Show From Make {0} {1}-{2}.txt", "Disc", discNumber, fileNumber++)));

                using (StreamWriter writer = fileInfo.CreateText())
                {
                    writer.WriteLine(fileInfo.Name);
                }
            }
        }
    }
}