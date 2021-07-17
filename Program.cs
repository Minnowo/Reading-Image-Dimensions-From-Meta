using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ImageDimensionReader
{
    class Program
    {
        static void Main(string[] args)
        {
            foreach (string path in Directory.EnumerateFiles("..\\..\\TestImages"))
            {
                Console.WriteLine($"{path} : {ImageDimensionReader.GetDimensions(path)}");
            }
            Console.ReadLine();
        }
    }
}
