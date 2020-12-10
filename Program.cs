using System;

namespace GZipTest
{
    class Program
    {
       static int Main(string[] args)
        {
            string command = (args.Length > 1) ? args[0] : "compress";
            string source = (args.Length > 1) ? args[1] : "C:\\Test\\backup.sql";
            string target = (args.Length > 1) ? args[2] : "C:\\Test\\split\\backup2.zip";


            using (var compressor = new Compressor(source, target))
            {
                if(command.Equals("compress"))
                {
                    if (compressor.Compress())
                        return 0;
                    return 1;
                }
                else if(command.Equals("decompress"))
                {
                    if (compressor.Decompress())
                        return 0;
                    return 1;
                }
                else
                {
                    Console.WriteLine("Unknown command.");
                    return 1;
                }
            }
        }
    }
}
