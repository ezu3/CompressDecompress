using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;

namespace GZipTest
{
    public class Compressor : IDisposable
    {
        private static Semaphore semaphore = new Semaphore(1, 1);
        private Stream sourceStream;
        private Stream targetStream;

        public Compressor(string inputStreamPath, string outputStreamPath)
        {
            sourceStream = OpenFile(inputStreamPath);
            targetStream = OpenFile(outputStreamPath, FileMode.Create);
        }

        private Stream OpenFile(string path, FileMode mode = FileMode.Open)
        {
            try
            {
                return File.Open(path, mode);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error occured while accessing the file.\n {ex}");
            }
            return null;
        }

        public bool Compress()
        {
            if (sourceStream == null || targetStream == null)
                return false;
            byte[] buffer = new byte[Constants.CHUNK_SIZE_COMPRESS];

            Task<MemoryStream>[] tasks = new Task<MemoryStream>[GetNumberOfRequiredTasks((double)Constants.CHUNK_SIZE_COMPRESS)];

            int taskCounter = 0;
            int read = 0;

            while (0 != (read = sourceStream.Read(buffer, 0, Constants.CHUNK_SIZE_COMPRESS)))
            {
                tasks[taskCounter] = Task<MemoryStream>.Factory.StartNew(() => EasyCompress(buffer, read, semaphore));
                semaphore.WaitOne();
                taskCounter++;
                buffer = new byte[Constants.CHUNK_SIZE_COMPRESS];
            }

            Task.WaitAll(tasks);

            for (int i = 0; i < tasks.Length; i++)
            {
                var data = tasks[i].Result.ToArray();
                byte[] lengthData = GetBytesToStore(data.Length);
                targetStream.Write(lengthData, 0, lengthData.Length);
                targetStream.Write(data, 0, data.Length);
            }

            targetStream.Flush();
            return true;
        }

        public bool Decompress()
        {
            if (sourceStream == null || targetStream == null)
                return false;
            byte[] buffer = new byte[Constants.CHUNK_SIZE_DECOMPRESS];
            List<byte[]> streamChunks = new List<byte[]>();

            while (0 != sourceStream.Read(buffer, 0, Constants.CHUNK_SIZE_DECOMPRESS))
            {
                int lengthToRead = GetLengthFromBytes(buffer);
                byte[] buffRead = new byte[lengthToRead];
                sourceStream.Read(buffRead, 0, lengthToRead);
                streamChunks.Add(buffRead);
            }

            Task<MemoryStream>[] tasks = new Task<MemoryStream>[streamChunks.Count];

            for (int taskCounter = 0; taskCounter < streamChunks.Count; taskCounter++)
            {
                tasks[taskCounter] = Task<MemoryStream>.Factory.StartNew(() => EasyDecompress(streamChunks[taskCounter], semaphore));
                semaphore.WaitOne();
            }

            Task.WaitAll(tasks);

            for (int i = 0; i < tasks.Length; i++)
            {
                var data = tasks[i].Result.ToArray();
                targetStream.Write(data, 0, data.Length);
            }

            targetStream.Flush();
            return true;
        }

        private int GetNumberOfRequiredTasks(double chunkSize)
        {
            return Convert.ToInt32(Math.Ceiling(((double)sourceStream.Length / chunkSize)));
        }

        private MemoryStream EasyCompress(byte[] buffer, int length, Semaphore semaphore)
        {
            try
            {
                MemoryStream stream = new MemoryStream();
                using (GZipStream gzStream = new GZipStream(stream, CompressionMode.Compress, true))
                {
                    gzStream.Write(buffer, 0, length);
                }
                semaphore.Release();
                return stream;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Compressing failed. See exception for details:\n{ex}");
                semaphore.Release();
                return null;
            }
        }

        private MemoryStream EasyDecompress(byte[] buffer, Semaphore semaphore)
        {
            try
            {
                MemoryStream decompressedStream = new MemoryStream();

                using (MemoryStream stream = new MemoryStream(buffer))
                {
                    using (GZipStream gzStream = new GZipStream(stream, CompressionMode.Decompress, true))
                    {
                        byte[] decompressedBuffer = new byte[buffer.Length];
                        int read = 0;
                        while (0 != (read = gzStream.Read(decompressedBuffer, 0, buffer.Length)))
                        {
                            decompressedStream.Write(decompressedBuffer, 0, read);
                        }
                    }
                }
                semaphore.Release();
                return decompressedStream;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Decompressing failed. See exception for details:\n{ex}");
                semaphore.Release();
                return null;
            }
        }

        //This converting solution was found on codeproject because I was constantly getting 
        //'The archive entry was compressed using an unsupported compression method.' when trying to decompress the stream
        //and didn't know how to deal with it
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