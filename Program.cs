namespace ChunkMergeTool
{
    using System.Diagnostics;
    using System.Text;
    using System.Text.Json;

    internal class Program
    {
        static readonly string WorkingDir = @"C:\Users\Fred\Documents\Git\s3unlocked\Levels\LBZ\Chunks";
        static readonly string FileReport = @"report.txt";
        static readonly string FileLayoutAct1 = @"..\Layout\1.bin";
        static readonly string FileLayoutAct2 = @"..\Layout\2.bin";
        static readonly string FileSolidsAct1 = @"..\Collision\1.bin";
        static readonly string FileSolidsAct2 = @"..\Collision\2.bin";

        static readonly string FileChunksAct1 = "Act 1";
        static readonly string FileChunksAct2 = "Act 2";
        static readonly string FileBlocksCommon = @"..\Blocks\Primary";
        static readonly string FileBlocksAct1 = @"..\Blocks\Act 1 Secondary";
        static readonly string FileBlocksAct2 = @"..\Blocks\Act 2 Secondary";

        static void Main()
        {
            var layoutAct1 = ReadLayout(FileLayoutAct1);
            var layoutAct2 = ReadLayout(FileLayoutAct2);
            var chunksAct1 = ReadChunks(FileChunksAct1);
            var chunksAct2 = ReadChunks(FileChunksAct2);

            var blocksCommon = ReadBlocks(FileBlocksCommon);
            var blocksAct1 = blocksCommon.Concat(ReadBlocks(FileBlocksAct1)).ToList();
            var blocksAct2 = blocksCommon.Concat(ReadBlocks(FileBlocksAct2)).ToList();
            ReadSolids(FileSolidsAct1, blocksAct1);
            ReadSolids(FileSolidsAct1, blocksAct2);

            chunksAct2[0xA6].Used = true;
            chunksAct2[0xA7].Used = true;
            MarkUsedChunks(chunksAct1, layoutAct1);
            MarkUsedChunks(chunksAct2, layoutAct2);

            BlankUnusedChunks(chunksAct1);
            BlankUnusedChunks(chunksAct2);
            var blocksMapping = Analyze(chunksAct1, chunksAct2, blocksCommon.Count);
        }

        static void MarkUsedChunks(IList<ChunkInfo> chunks, LayoutInfo layout)
        {
            foreach (var row in layout.Rows)
                foreach (var chunk in row.Chunks)
                    chunks[chunk].Used = true;
        }

        static void BlankUnusedChunks(IList<ChunkInfo> chunks)
        {
            var blankDefinition = chunks[0].Definition;
            foreach (var chunk in chunks.Where(chunk => !chunk.Used))
                chunk.Definition = blankDefinition;
        }

        static IList<int?> Analyze(IList<ChunkInfo> chunksAct1, IList<ChunkInfo> chunksAct2, int blocksCommonCount)
        {
            var path = Path.Combine(WorkingDir, FileReport);
            var report = JsonSerializer.Deserialize<IList<Report>>(File.Exists(path) ? File.ReadAllText(path) : "[]");
            var blocksMapping = new List<int?>(0x300);

            for (var index = 0; index < blocksCommonCount; index++)
                blocksMapping.Add(index);

            for (var index = blocksMapping.Count; index < 0x300; index++)
                blocksMapping.Add(null);

            for (var chunkIndex1 = 1; chunkIndex1 < chunksAct1.Count; chunkIndex1++)
            {
                var chunk1 = chunksAct1[chunkIndex1];
                if (!chunk1.Used) continue;
                chunk1.Unique = true;

                for (int chunkIndex2 = 0; chunkIndex2 < chunksAct2.Count; chunkIndex2++)
                {
                    var chunk2 = chunksAct2[chunkIndex2];
                    var guesses = new Dictionary<int, int>();
                    var match = true;

                    for (var blockIndex = 0; blockIndex < 0x40; blockIndex++)
                    {
                        var block1 = chunk1.Definition[blockIndex];
                        var block2 = chunk2.Definition[blockIndex];
                        var expectedBlock2 = blocksMapping[blockIndex];

                        if (block1.Id < blocksCommonCount)
                        {
                            if (block1.Id != block2.Id) { match = false; break; }
                        }
                        else if (expectedBlock2.HasValue)
                        {
                            if (expectedBlock2.GetValueOrDefault() != block2.Id) throw new Exception(
                                $"Act 1 Chunk: {chunkIndex1:X}\r\n" +
                                $"Act 2 Chunk: {chunkIndex2:X}\r\n" +
                                $"Found block: {block2.Id:X}\r\n" +
                                $"Expected block: {expectedBlock2:X}"
                            );
                        }
                        else if (guesses.TryGetValue(block1.Id, out var guessedBlock2))
                        {
                            if (block2.Id != guessedBlock2) { match = false; break; }
                        }
                        else guesses.Add(block1.Id, block2.Id);
                    }

                    if (match)
                    {
                        chunk1.Unique = false;
                        chunk1.Match = (byte)chunkIndex2;

                        foreach (var guess in guesses)
                            blocksMapping[guess.Key] = guess.Value;

                        break;
                    }
                }

                if (!chunk1.Unique)
                    continue;

                chunk1.Unique = true;
            }

            return blocksMapping;
        }

        static void ReadSolids(string filename, IList<BlockInfo> blocks)
        {
            var file = File.OpenRead(Path.Combine(WorkingDir, filename));

            foreach (var block in blocks)
                block.Solidity = ReadWord(file);
        }

        static IList<BlockInfo> ReadBlocks(string filename)
        {
            var compressed = $"{filename}.bin";
            var uncompressed = $"{filename} unc.bin";
            ProcessKosFile(compressed, uncompressed, moduled: false, extract: true);

            Thread.Sleep(3000);

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

            Thread.Sleep(3000);

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

    internal class Report
    {
        public int Id { get; set; }

        public byte Match { get; set; }

        public bool Confirmed { get; set; }
    }

    internal class ChunkInfo
    {
        public IList<BlockRef> Definition { get; set; }

        public bool Used { get; set; }

        public bool Unique { get; set; }

        public byte Match { get; set; }

        public bool Confirmed { get; set; }

        public ChunkInfo(IList<BlockRef> definition)
        {
            Definition = definition;
        }

        public IEnumerable<int> Words => Definition.Select(blockRef => blockRef.Word);
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

        public int Solidity { get; set; }

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