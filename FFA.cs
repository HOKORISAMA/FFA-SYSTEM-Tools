using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using Utility.Compression;

namespace FFA
{
    public class Entry
    {
        public string Name { get; set; }
        public uint Size { get; set; }
        public long Offset { get; set; }

        public bool CheckPlacement(long maxOffset)
        {
            return Offset >= 0 && Size >= 0 && (Offset + Size) <= maxOffset;
        }
    }

    public class FFASYSTEM
    {
        public void Unpack(string filePath, string folderName)
        {
            string lstFilePath = Path.ChangeExtension(filePath, ".lst");
            if (!File.Exists(lstFilePath))
            {
                throw new FileNotFoundException($"Associated list file not found: {lstFilePath}");
            }

            using (FileStream dataFs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            using (FileStream lstFs = new FileStream(lstFilePath, FileMode.Open, FileAccess.Read))
            using (BinaryReader dataBr = new BinaryReader(dataFs))
            using (BinaryReader lstBr = new BinaryReader(lstFs))
            {
                uint fileCount = (uint)lstFs.Length / 0x16;
                if (fileCount > 0xffff || fileCount * 0x16 != lstFs.Length)
                {
                    throw new InvalidDataException("File Count is not Sane!");
                }

                var entries = new List<Entry>((int)fileCount);
                uint indexOffset = 0;
                for (int i = 0; i < fileCount; i++)
                {
                    var entry = new Entry();
                    lstBr.BaseStream.Position = indexOffset;
                    entry.Name = Encoding.ASCII.GetString(lstBr.ReadBytes(14)).TrimEnd('\0');
                    lstBr.BaseStream.Position = indexOffset + 14;
                    entry.Offset = lstBr.ReadUInt32();
                    lstBr.BaseStream.Position = indexOffset + 18;
                    entry.Size = lstBr.ReadUInt32();

                    if (!entry.CheckPlacement(dataFs.Length))
                    {
                        Console.WriteLine($"Warning: Invalid entry placement: {entry.Name}");
                        continue;
                    }

                    entries.Add(entry);
                    indexOffset += 0x16;
                }

                Directory.CreateDirectory(folderName);

                foreach (var entry in entries)
                {
                    string outputPath = Path.Combine(folderName, entry.Name);
                    string extension = Path.GetExtension(entry.Name).ToLowerInvariant();
                    
                    string directory = Path.GetDirectoryName(outputPath);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    dataBr.BaseStream.Position = entry.Offset;
                    byte[] fileData;

                    if ((extension == ".so4" || extension == ".so5") && entry.Size > 8)
                    {
                        try
                        {
                            // Read LZSS header
                            int packedSize = dataBr.ReadInt32();
                            int unpackedSize = dataBr.ReadInt32();

                            // Verify header
                            if (packedSize + 8 == entry.Size && packedSize > 0 && unpackedSize > 0)
                            {
                                // Read compressed data
                                byte[] compressedData = dataBr.ReadBytes(packedSize);
                                fileData = Lzss.Decompress(compressedData);
                                Console.WriteLine($"Decompressed: {entry.Name}");
                            }
                            else
                            {
                                // Invalid header, treat as regular file
                                dataBr.BaseStream.Position = entry.Offset;
                                fileData = dataBr.ReadBytes((int)entry.Size);
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Failed to decompress {entry.Name}: {ex.Message}");
                            continue;
                        }
                    }
                    else
                    {
                        fileData = dataBr.ReadBytes((int)entry.Size);
                    }

                    try
                    {
                        File.WriteAllBytes(outputPath, fileData);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to write {entry.Name}: {ex.Message}");
                    }
                }
            }
        }
        
        public void Pack(string inputFolder, string outputFile)
        {
            if (!Directory.Exists(inputFolder))
            {
                throw new DirectoryNotFoundException($"Input folder not found: {inputFolder}");
            }

            var files = Directory.GetFiles(inputFolder, "*", SearchOption.AllDirectories);
            if (files.Length == 0)
            {
                throw new InvalidOperationException("No files found in input folder");
            }
            if (files.Length > 0xFFFF)
            {
                throw new InvalidOperationException("Too many files (maximum is 65535)");
            }

            var entries = new List<Entry>();
            long currentOffset = 0;

            string lstFile = Path.ChangeExtension(outputFile, ".lst");
            using (FileStream dataFs = new FileStream(outputFile, FileMode.Create, FileAccess.Write))
            using (FileStream lstFs = new FileStream(lstFile, FileMode.Create, FileAccess.Write))
            using (BinaryWriter dataBw = new BinaryWriter(dataFs))
            using (BinaryWriter lstBw = new BinaryWriter(lstFs))
            {
                foreach (string file in files)
                {
                    byte[] fileData = File.ReadAllBytes(file);
                    string relativePath = Path.GetRelativePath(inputFolder, file);
                    string extension = Path.GetExtension(file).ToLowerInvariant();

                    uint finalSize;
                    if (extension == ".so4" || extension == ".so5")
                    {
                        try
                        {
                            // Compress the data first
                            byte[] compressedData = Lzss.Compress(fileData);
                            
                            // Write header: compressed size + uncompressed size
                            dataBw.Write((int)compressedData.Length);  // packedSize
                            dataBw.Write((int)fileData.Length);        // unpackedSize
                            
                            // Write the compressed data
                            dataBw.Write(compressedData);
                            
                            // Total size is compressed data + 8 byte header
                            finalSize = (uint)(compressedData.Length + 8);
                            
                            Console.WriteLine($"Compressed: {relativePath} ({fileData.Length} -> {compressedData.Length} bytes)");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Failed to compress {relativePath}: {ex.Message}");
                            continue;
                        }
                    }
                    else
                    {
                        dataBw.Write(fileData);
                        finalSize = (uint)fileData.Length;
                    }

                    var entry = new Entry
                    {
                        Name = relativePath,
                        Size = finalSize,
                        Offset = currentOffset
                    };

                    if (entry.Name.Length > 14)
                    {
                        Console.WriteLine($"Warning: Filename too long, truncating: {entry.Name}");
                        entry.Name = entry.Name.Substring(0, 14);
                    }

                    entries.Add(entry);
                    currentOffset += finalSize;

                }

                // Write the list file entries
                foreach (var entry in entries)
                {
                    byte[] nameBytes = new byte[14];
                    Encoding.ASCII.GetBytes(entry.Name).CopyTo(nameBytes, 0);
                    lstBw.Write(nameBytes);
                    lstBw.Write((uint)entry.Offset);
                    lstBw.Write(entry.Size);
                }
            }

            Console.WriteLine($"\nPacking completed!");
            Console.WriteLine($"Created: {outputFile}");
            Console.WriteLine($"Created: {lstFile}");
            Console.WriteLine($"Total files packed: {entries.Count}");
        }
    }
}