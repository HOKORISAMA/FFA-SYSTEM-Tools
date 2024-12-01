using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using Utility.Compression;

namespace FFA
{
    public class Program
    {
        public static void ShowUsage()
        {
            Console.WriteLine("FFA File Packer/Unpacker");
            Console.WriteLine("=======================");
            Console.WriteLine("Usage:");
            Console.WriteLine("  Pack:   FFA -p <input_folder> [output_file.dat]");
            Console.WriteLine("  Unpack: FFA -u <input_file.dat> [output_folder]");
            Console.WriteLine("\nExamples:");
            Console.WriteLine("  FFA -p inputfolder             # Packs inputfolder into inputfolder.dat");
            Console.WriteLine("  FFA -p inputfolder output.dat  # Packs inputfolder into output.dat");
            Console.WriteLine("  FFA -u input.dat            # Unpacks input.dat to input folder");
            Console.WriteLine("  FFA -u input.dat output     # Unpacks input.dat to output folder");
        }

        public static void Main(string[] args)
        {
            if (args.Length < 2)
            {
                ShowUsage();
                return;
            }

            try
            {
                var ffa = new FFASYSTEM();
                string mode = args[0].ToLower();
                string inputPath = args[1].Trim('"');

                switch (mode)
                {
                    case "-p":
                    case "--pack":
                        {
                            string outputFile = args.Length >= 3 
                                ? args[2].Trim('"') 
                                : Path.GetFileName(Path.GetFullPath(inputPath)) + ".dat";

                            Console.WriteLine("Packing Mode");
                            Console.WriteLine($"Input folder: {inputPath}");
                            Console.WriteLine($"Output file: {outputFile}");
                            Console.WriteLine("Starting packing process...\n");

                            ffa.Pack(inputPath, outputFile);
                            break;
                        }

                    case "-u":
                    case "--unpack":
                        {
                            string outputFolder = args.Length >= 3 
                                ? args[2].Trim('"') 
                                : Path.GetFileNameWithoutExtension(inputPath);

                            Console.WriteLine("Unpacking Mode");
                            Console.WriteLine($"Input file: {inputPath}");
                            Console.WriteLine($"Output folder: {outputFolder}");
                            Console.WriteLine("Starting unpacking process...\n");

                            ffa.Unpack(inputPath, outputFolder);
                            break;
                        }

                    default:
                        Console.WriteLine($"Invalid mode: {mode}");
                        ShowUsage();
                        return;
                }

                Console.WriteLine("\nOperation completed successfully!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nError: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                Environment.Exit(1);
            }

            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }
    }
}