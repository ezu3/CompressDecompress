using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GZipTest
{
    public class Compressor : IDisposable
    {
        static AutoResetEvent autoResetEvent = new AutoResetEvent(true);
        private Stream sourceStream;
        private Stream targetStream;

        private int noOfChunks;

        public Compressor(string inputStreamPath, string outputStreamPath)
        {
            sourceStream = OpenFile(inputStreamPath);
            targetStream = OpenFile(outputStreamPath, FileMode.Create);
            noOfChunks = GetNumberOfChunks((double) Constants.CHUNK_SIZE_COMPRESS);
        }

        private Stream OpenFile(string path, FileMode mode = FileMode.Open)
        {
            try
            {
                return File.Open(path, mode);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error occurred while accessing the file.\n {ex}");
            }
            return null;
        }

        public bool Compress()
        {
            if (sourceStream == null || targetStream == null)
                return false;
            Thread[] workingThreads = new Thread[noOfChunks];
            byte[] buffer = new byte[Constants.CHUNK_SIZE_COMPRESS];
            List<MemoryStream> results = new List<MemoryStream>();

            try
            {
                var index = 0;
                while (0 != (sourceStream.Read(buffer, 0, Constants.CHUNK_SIZE_COMPRESS)))
                {
                    workingThreads[index] = new Thread(() =>
                    {
                        Debug.Print(Thread.CurrentThread.ManagedThreadId + " starts");
                        autoResetEvent.WaitOne();
                        results.Add(EasyCompress(buffer));
                    });
                    workingThreads[index].Start();
                    index++;
                    buffer = new byte[Constants.CHUNK_SIZE_COMPRESS];
                }

                for (int i = 0; i < workingThreads.Length; i++)
                {
                    workingThreads[i]?.Join();
                }

                for (int i = 0; i < results.Count; i++)
                {
                    var data = results[i].ToArray();
                    byte[] lengthData = GetBytesToStore(data.Length);
                    targetStream.Write(lengthData, 0, lengthData.Length);
                    targetStream.Write(data, 0, data.Length);
                }

                targetStream.Flush();
            }
            catch(Exception e)
            {
                Console.WriteLine($"Operation failed. See exception for details:\n{e}");
                return false;
            }

            return true;
        }

        public bool Decompress()
        {
            if (sourceStream == null || targetStream == null)
                return false;
            byte[] buffer = new byte[Constants.CHUNK_SIZE_DECOMPRESS];
            Thread[] workingThreads = new Thread[GetNumberOfChunks((double)Constants.CHUNK_SIZE_DECOMPRESS)];
            List<MemoryStream> results = new List<MemoryStream>();
            try
            {
                var index = 0;
                while (0 != sourceStream.Read(buffer, 0, Constants.CHUNK_SIZE_DECOMPRESS))
                {
                    int lengthToRead = GetLengthFromBytes(buffer);
                    byte[] buffRead = new byte[lengthToRead];
                    sourceStream.Read(buffRead, 0, lengthToRead);
                    Debug.Print(Thread.CurrentThread.ManagedThreadId + " starts");
                    workingThreads[index] = new Thread(() => { results.Add(EasyDecompress(buffRead)); });
                }

                for (int i = 0; i < workingThreads.Length; i++)
                {
                    workingThreads[i]?.Join();
                }


                for (int i = 0; i < results.Count; i++)
                {
                    var data = results[i].ToArray();
                    targetStream.Write(data, 0, data.Length);
                }

                targetStream.Flush();
            }
            catch (Exception e)
            {
                Console.WriteLine($"Operation failed. See exception for details:\n{e}");
                return false;
            }

            return true;
        }

        private int GetNumberOfChunks(double chunkSize)
        {
            return Convert.ToInt32(Math.Ceiling(((double)sourceStream.Length / chunkSize)));
        }

        private MemoryStream EasyCompress(byte[] data)
        {
            autoResetEvent.Set();
            Debug.Print(Thread.CurrentThread.ManagedThreadId + " released the lock");
            MemoryStream stream = new MemoryStream();
                using (GZipStream gzStream = new GZipStream(stream, CompressionMode.Compress, true))
                {
                    try
                    {
                        gzStream.Write(data, 0, data.Length);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Compressing failed. See exception for details:\n{ex}");
                        return null;
                    }
                }
                return stream;
        }

        private MemoryStream EasyDecompress(byte[] data)
        {
            try
            {
                autoResetEvent.Set();
                MemoryStream decompressedStream = new MemoryStream();

                using (MemoryStream stream = new MemoryStream(data))
                {
                    using (GZipStream gzStream = new GZipStream(stream, CompressionMode.Decompress, true))
                    {
                        byte[] decompressedBuffer = new byte[data.Length];
                        int read = 0;
                        while (0 != (read = gzStream.Read(decompressedBuffer, 0, data.Length)))
                        {
                            decompressedStream.Write(decompressedBuffer, 0, read);
                        }
                    }
                }
                return decompressedStream;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Decompressing failed. See exception for details:\n{ex}");
                return null;
            }
        }
        
        private static byte[] GetBytesToStore(int length)
        {
            int lengthToStore = System.Net.IPAddress.HostToNetworkOrder(length);
            byte[] lengthInBytes = BitConverter.GetBytes(lengthToStore);
            string base64Enc = Convert.ToBase64String(lengthInBytes);
            byte[] finalStore = System.Text.Encoding.ASCII.GetBytes(base64Enc);

            return finalStore;
        }

        private static int GetLengthFromBytes(byte[] intToParse)
        {
            string base64Enc = System.Text.Encoding.ASCII.GetString(intToParse);
            byte[] normStr = Convert.FromBase64String(base64Enc);
            int length = BitConverter.ToInt32(normStr, 0);

            return System.Net.IPAddress.NetworkToHostOrder(length);
        }

        public void Dispose()
        {
            sourceStream?.Close();
            targetStream?.Close();
        }
    }
}