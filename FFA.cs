using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Collections.Generic;
using System.Linq;
using Compression;

namespace FFA
{
    public class ArchiveEntry
    {
        public string Name { get; set; }
        public uint Size { get; set; }
        public long Offset { get; set; }
        public int Order { get; set; }  
    }

    public class ArchiveMetadata
    {
        public List<string> FileOrder { get; set; }
    }

    public class FFASYSTEM
    {
        public void Unpack(string filePath, string folderName)
        {
            string lstFilePath = Path.ChangeExtension(filePath, ".lst");
            string metadataPath = Path.ChangeExtension(filePath, ".json");
            
            if (!File.Exists(lstFilePath))
            {
                throw new FileNotFoundException($"Associated list file not found: {lstFilePath}");
            }

            var fileOrder = new List<string>();

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

                var entries = new List<ArchiveEntry>((int)fileCount);
                uint indexOffset = 0;
                for (int i = 0; i < fileCount; i++)
                {
                    var entry = new ArchiveEntry();
                    lstBr.BaseStream.Position = indexOffset;
                    entry.Name = Encoding.ASCII.GetString(lstBr.ReadBytes(14)).TrimEnd('\0');
                    lstBr.BaseStream.Position = indexOffset + 14;
                    entry.Offset = lstBr.ReadUInt32();
                    lstBr.BaseStream.Position = indexOffset + 18;
                    entry.Size = lstBr.ReadUInt32();
                    entry.Order = i;  // Save original order

                    fileOrder.Add(entry.Name);  // Record file order

                    if (!CheckPlacement(entry.Offset, entry.Size, dataFs.Length))
                    {
                        Console.WriteLine($"Warning: Invalid entry placement: {entry.Name}");
                        continue;
                    }

                    entries.Add(entry);
                    indexOffset += 0x16;
                }

                // Save file order to JSON
                var metadata = new ArchiveMetadata { FileOrder = fileOrder };
                string jsonMetadata = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(metadataPath, jsonMetadata);

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
                            int packedSize = dataBr.ReadInt32();
                            int unpackedSize = dataBr.ReadInt32();

                            if (packedSize + 8 == entry.Size && packedSize > 0 && unpackedSize > 0)
                            {
                                byte[] compressedData = dataBr.ReadBytes(packedSize);
                                fileData = Lzss.Decompress(compressedData);
                                Console.WriteLine($"Decompressed: {entry.Name}");
                            }
                            else
                            {
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

            string metadataPath = Path.ChangeExtension(outputFile, ".json");
            if (!File.Exists(metadataPath))
            {
                throw new FileNotFoundException($"Order metadata file not found: {metadataPath}");
            }

            // Read file order from JSON
            var metadata = JsonSerializer.Deserialize<ArchiveMetadata>(File.ReadAllText(metadataPath));
            if (metadata?.FileOrder == null || metadata.FileOrder.Count == 0)
            {
                throw new InvalidDataException("Invalid or empty file order metadata");
            }

            var entries = new List<ArchiveEntry>();
            long currentOffset = 0;

            string lstFile = Path.ChangeExtension(outputFile, ".lst");
            using (FileStream dataFs = new FileStream(outputFile, FileMode.Create, FileAccess.Write))
            using (FileStream lstFs = new FileStream(lstFile, FileMode.Create, FileAccess.Write))
            using (BinaryWriter dataBw = new BinaryWriter(dataFs))
            using (BinaryWriter lstBw = new BinaryWriter(lstFs))
            {
                // Process files in the order specified by metadata
                foreach (string fileName in metadata.FileOrder)
                {
                    string filePath = Path.Combine(inputFolder, fileName);
                    if (!File.Exists(filePath))
                    {
                        Console.WriteLine($"Warning: File not found: {fileName}");
                        continue;
                    }

                    byte[] fileData = File.ReadAllBytes(filePath);
                    string extension = Path.GetExtension(fileName).ToLowerInvariant();

                    uint finalSize;
                    if (extension == ".so4" || extension == ".so5")
                    {
                        try
                        {
                            byte[] compressedData = Lzss.Compress(fileData);
                            dataBw.Write(compressedData.Length);
                            dataBw.Write(fileData.Length);
                            dataBw.Write(compressedData);
                            finalSize = (uint)(compressedData.Length + 8);
                            Console.WriteLine($"Compressed: {fileName} ({fileData.Length} -> {compressedData.Length} bytes)");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Failed to compress {fileName}: {ex.Message}");
                            continue;
                        }
                    }
                    else
                    {
                        dataBw.Write(fileData);
                        finalSize = (uint)fileData.Length;
                    }

                    var entry = new ArchiveEntry
                    {
                        Name = fileName,
                        Size = finalSize,
                        Offset = currentOffset
                    };

                    entries.Add(entry);
                    currentOffset += finalSize;
                }

                // Write list file entries in the same order
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

        private bool CheckPlacement(long offset, uint size, long maxOffset)
        {
            return offset >= 0 && size >= 0 && (offset + size) <= maxOffset;
        }
    }
}
