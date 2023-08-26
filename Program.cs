namespace ChunkMergeTool
{
    using System.Diagnostics;
    using System.Text;

    internal class Program
    {
        static readonly string WorkingDir = @"C:\Users\Fred\Documents\Git\s3unlocked\Levels\LBZ\Chunks";
        static readonly string FileLayoutAct1 = @"..\Layout\1.bin";
        static readonly string FileLayoutAct2 = @"..\Layout\2.bin";

        static readonly string FileChunksAct1 = "Act 1";
        static readonly string FileChunksAct2 = "Act 2";
        static readonly string FileBlocksCommon = @"..\Blocks\Primary";
        static readonly string FileBlocksAct1 = @"..\Blocks\Act 1 Secondary";
        static readonly string FileBlocksAct2 = @"..\Blocks\Act 2 Secondary";

        static void Main()
        {
            var blocksCommon = ReadBlocks(FileBlocksCommon);
            var blocksAct1 = blocksCommon.Concat(ReadBlocks(FileBlocksAct1)).ToList();
            var blocksAct2 = blocksCommon.Concat(ReadBlocks(FileBlocksAct2)).ToList();
            var chunksAct1 = ReadChunks(FileChunksAct1);
            var chunksAct2 = ReadChunks(FileChunksAct2);
            var layoutAct1 = ReadLayout(FileLayoutAct1);
            var layoutAct2 = ReadLayout(FileLayoutAct2);

            MarkUsedChunks(chunksAct1, layoutAct1);
            MarkUsedChunks(chunksAct2, layoutAct2);
            chunksAct2[0xA6].Used = true;
            chunksAct2[0xA7].Used = true;
        }

        static void MarkUsedChunks(IList<ChunkInfo> chunks, LayoutInfo layout)
        {
            foreach (var row in layout.Rows)
                foreach (var chunk in row.Chunks)
                    chunks[chunk].Used = true;
        }

        static IList<BlockInfo> ReadBlocks(string filename)
        {
            var compressed = $"{filename}.bin";
            var uncompressed = $"{filename} unc.bin";
            ProcessKosFile(compressed, uncompressed, moduled: false, extract: true);

            Thread.Sleep(1000);

            var file = File.OpenRead(Path.Combine(WorkingDir, uncompressed));
            var list = new List<BlockInfo>();

            while (file.Position != file.Length)
            {
                var definition = new List<TileRef>(4);
                for (var index = 0; index < 4; index++)
                {
                    var word = ReadWord(file);
                    definition.Add(new TileRef(word));
                }

                list.Add(new BlockInfo(definition));
            }

            return list;
        }

        static IList<ChunkInfo> ReadChunks(string filename)
        {
            var compressed = $"{filename}.bin";
            var uncompressed = $"{filename} unc.bin";
            ProcessKosFile(compressed, uncompressed, moduled: false, extract: true);

            Thread.Sleep(1000);

            var file = File.OpenRead(Path.Combine(WorkingDir, uncompressed));
            var list = new List<ChunkInfo>();

            while (file.Position != file.Length)
            {
                var definition = new List<BlockRef>(0x40);
                for (var index = 0; index < 0x40; index++)
                {
                    var word = ReadWord(file);
                    definition.Add(new BlockRef(word));
                }

                list.Add(new ChunkInfo(definition));
            }

            return list;
        }

        static LayoutInfo ReadLayout(string filename)
        {
            var file = File.OpenRead(Path.Combine(WorkingDir, filename));
            var widthFG = ReadWord(file);
            var widthBG = ReadWord(file);
            var heightFG = ReadWord(file);
            var heightBG = ReadWord(file);
            var ptrsFG = new List<int>(0x20);
            var ptrsBG = new List<int>(0x20);

            for (var index = 0; index < 0x20; index++)
            {
                ptrsFG.Add(ReadWord(file));
                ptrsBG.Add(ReadWord(file));
            }

            var foreground = ReadLayoutRows(file, widthFG, heightFG, ptrsFG);
            var background = ReadLayoutRows(file, widthBG, heightBG, ptrsBG);
            return new LayoutInfo(foreground, background);
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
                rows.Add(new LayoutRow(buffer));
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
        public IList<LayoutRow> Foreground { get; set; }

        public IList<LayoutRow> Background { get; set; }

        public LayoutInfo(IList<LayoutRow> foreground, IList<LayoutRow> background)
        {
            Foreground = foreground;
            Background = background;
        }

        public IEnumerable<LayoutRow> Rows => Foreground.Concat(Background);
    }

    internal class LayoutRow
    {
        public IList<byte> Chunks { get; set; }

        public LayoutRow(IList<byte> chunks)
        {
            Chunks = chunks;
        }
    }

    internal class ChunkInfo
    {
        public IList<BlockRef> Definition { get; set; }

        public bool Used { get; set; }

        public MatchType MatchType { get; set; }

        public byte Match { get; set; }

        public ChunkInfo(IList<BlockRef> definition)
        {
            Definition = definition;
        }

        public IEnumerable<int> Words => Definition.Select(blockRef => blockRef.Word);
    }

    internal enum MatchType : byte
    {
        None,
        Possible,
        Confirmed,
    }

    internal class BlockRef
    {
        public int Id { get; set; }

        public bool XFlip { get; set; }

        public bool YFlip { get; set; }

        public BlockSolidity ForegroundSolid { get; set; }

        public BlockSolidity BackgroundSolid { get; set; }

        public BlockRef(int word)
        {
            Id = word & 0x3FF;
            XFlip = (word & 0x400) != 0;
            YFlip = (word & 0x800) != 0;
            ForegroundSolid = (BlockSolidity)((word & 0x3000) >> 12);
            BackgroundSolid = (BlockSolidity)((word & 0xC000) >> 14);
        }

        public int Word =>
            (int)BackgroundSolid << 14 |
            (int)ForegroundSolid << 12 |
            (YFlip ? 0x800 : 0) |
            (XFlip ? 0x400 : 0) |
            Id;
    }

    internal enum BlockSolidity : byte
    {
        None,
        Top,
        Sides,
        All
    }

    internal class BlockInfo
    {
        IList<TileRef> Definition { get; set; }

        public BlockInfo(IList<TileRef> definition)
        {
            Definition = definition;
        }

        public IEnumerable<int> Words => Definition.Select(tileRef => tileRef.Word);
    }

    internal class TileRef
    {
        public int Id { get; set; }

        public bool XFlip { get; set; }

        public bool YFlip { get; set; }

        public byte Palette { get; set; }

        public bool Priority { get; set; }

        public TileRef(int word)
        {
            Id = word & 0x7FF;
            XFlip = (word & 0x800) != 0;
            YFlip = (word & 0x1000) != 0;
            Palette = (byte)((word & 0x6000) >> 13);
            Priority = (word & 0x8000) != 0;
        }

        public int Word =>
            (Priority ? 0x8000 : 0) |
            (Palette << 13) |
            (YFlip ? 0x800 : 0) |
            (XFlip ? 0x400 : 0) |
            Id;
    }
}