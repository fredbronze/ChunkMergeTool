namespace ChunkMergeTool
{
    using System.Diagnostics;
    using System.Text;
    using static ChunkMergeTool.LayoutInfo;

    internal class Program
    {
        static readonly string WorkingDir = @"C:\Users\Fred\Documents\Git\s3unlocked\Levels\LBZ\Chunks";
        static readonly string FileLayoutAct1 = @"..\Layout\1.bin";
        static readonly string FileLayoutAct2 = @"..\Layout\2.bin";

        static readonly string FileBlocksPrimary = @"..\Blocks\Primary";
        static readonly string FileBlocksAct1 = @"..\Blocks\Act 1 Secondary";
        static readonly string FileBlocksAct2 = @"..\Blocks\Act 2 Secondary";
        static readonly string FileChunksAct1 = "Act 1";
        static readonly string FileChunksAct2 = "Act 2";

        static readonly byte[] ProtectedChunks = new byte[] { 0xA4, 0xA5, 0xA6, 0xA7 };

        static void Main()
        {
            var layoutAct1 = ReadLayout(FileLayoutAct1);
            var layoutAct2 = ReadLayout(FileLayoutAct2);
            var chunksAct1 = ReadChunks(FileChunksAct1);
            var chunksAct2 = ReadChunks(FileChunksAct1);
        }

        static IList<ChunkInfo> ReadChunks(string filename)
        {
            var compressed = $"{filename}.bin";
            var uncompressed = $"{filename} unc.bin";
            ProcessKosFile(compressed, uncompressed, moduled: false, extract: true);

            Thread.Sleep(1000);

            var file = File.OpenRead(Path.Combine(WorkingDir, uncompressed));
            var chunks = new List<ChunkInfo>();
            var buffer = new byte[0x80];

            while (file.Read(buffer) != 0)
            {
                chunks.Add(new ChunkInfo { Definition = buffer });
                buffer = new byte[0x80];
            }

            return chunks;
        }

        static LayoutInfo ReadLayout(string filename)
        {
            var file = File.OpenRead(Path.Combine(WorkingDir, filename));
            var widthFG = ReadWord(file);
            var widthBG = ReadWord(file);
            var heightFG = ReadWord(file);
            var heightBG = ReadWord(file);
            var ptrsFG = new List<int>(32);
            var ptrsBG = new List<int>(32);

            for (var index = 0; index < 32; index++)
            {
                ptrsFG.Add(ReadWord(file));
                ptrsBG.Add(ReadWord(file));
            }

            return new LayoutInfo
            {
                Foreground = ReadLayoutRows(file, widthFG, heightFG, ptrsFG),
                Background = ReadLayoutRows(file, widthBG, heightBG, ptrsBG),
            };
        }

        static IList<LayoutRow> ReadLayoutRows(FileStream file, int bufferSize, int rowCount, IEnumerable<int> rowPtrs)
        {
            var rows = new List<LayoutRow>(rowCount);

            foreach (var ptr in rowPtrs)
            {
                if (rows.Count == rowCount)
                    break;

                var buffer = new byte[bufferSize];
                file.Seek(ptr - 0x8000, SeekOrigin.Begin);
                file.Read(buffer);
                rows.Add(new LayoutRow { Chunks = buffer });
            }

            return rows;
        }

        static int ReadWord(FileStream file)
        {
            return (file.ReadByte() << 8) | file.ReadByte();
        }

        static void ProcessKosFile(string source, string destination, bool moduled, bool extract)
        {
            var args = new StringBuilder();

            if (extract) { args.Append("-x "); }
            if (moduled) { args.Append("-m "); }

            args.Append('"');
            args.Append(source);
            args.Append("\" \"");
            args.Append(destination);
            args.Append('"');

            Process.Start(new ProcessStartInfo("koscmp.exe", args.ToString())
            {
                WorkingDirectory = WorkingDir,
                CreateNoWindow = true
            });
        }
    }

    internal class LayoutInfo
    {
        public required IList<LayoutRow> Foreground { get; set; }
        public required IList<LayoutRow> Background { get; set; }

        internal class LayoutRow
        {
            public required IList<byte> Chunks { get; set; }
        }
    }

    internal class ChunkInfo
    {
        public required IList<byte> Definition { get; set; }

        public byte Match { get; set; }

        public MatchType MatchType { get; set; }
    }

    internal enum MatchType : byte
    {
        Unknown,
        Possible,
        Confirmed,
        NoMatch,
        Unused
    }
}